using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CsvHelper;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public class CsvSchemaFileProcessor(ILogger<CsvSchemaFileProcessor> logger, IReadOnlyDictionary<string, BlobServiceClient> blobClients) : ICsvSchemaFileProcessor
{
    public async IAsyncEnumerable<ValidationProgress> ProcessAsync(
        Guid checkingWindowId,
        CheckingExerciseType? exercise,
        IReadOnlyList<IngressDataset> datasets,
        bool validateOnly = false,
        bool clearExistingFiles = false,
        [EnumeratorCancellation] CancellationToken cancellationToken = default,
        Guid? checkingExerciseId = null, CheckingDataType? dataType = null, Guid? releaseId = null,
        IngressSummaryCapture? summaryCapture = null)
    {
        if (releaseId is not null && checkingExerciseId is null)
            throw new ArgumentException("A release belongs to an exercise, so it needs the exercise id.", nameof(releaseId));

        // A release writes each dataset to a folder named by its slot id, so two datasets with no
        // id, or the same id, would write over each other.
        if (releaseId is not null
            && (datasets.Any(d => d.DatasetId == Guid.Empty) || datasets.Select(d => d.DatasetId).Distinct().Count() != datasets.Count))
            throw new ArgumentException("A release run needs a distinct dataset id for every dataset.", nameof(datasets));

        if (datasets.Count == 0)
        {
            summaryCapture?.SetFailure("configuration", "no_datasets");
            yield return Failed("No ingress datasets are configured for this window.");
            yield break;
        }

        if (!blobClients.TryGetValue("app", out var sourceBlobClient))
        {
            summaryCapture?.SetFailure("configuration", "storage_not_configured");
            logger.LogWarning("Ingress storage client is not configured");
            yield return Failed("App storage is not configured.");
            yield break;
        }

        // Every output path this run touches is scoped to its exercise (#316). Two exercises share
        // one container, so an unscoped name would let one run overwrite or delete another's output.
        // Since #466 a run may instead be scoped to the exercise's own id, so several releases of
        // one kind can each own their output; when no id is given every path stays exactly as it
        // was before this task.
        string errorLogBlobName = checkingExerciseId is { } logId
            ? $"{CheckingExerciseBlobPaths.LogPrefix(logId)}error_log.txt"
            : CheckingExerciseBlobPaths.ErrorLogBlobName(exercise!.Value, checkingWindowId);
        BlobContainerClient container = sourceBlobClient.GetBlobContainerClient(checkingWindowId.ToString());
        bool multipleDatasets = datasets.Count > 1;

        // A release writes one file per dataset, unless the merged file already is that one
        // dataset's file. Writing both would store every record twice.
        bool writesDatasetFiles = releaseId is not null
            && !CheckingExerciseBlobPaths.MergedFileIsDatasetFile([.. datasets.Select(d => d.FeedsJourney)]);

        // Records from every dataset are merged per school, so one run produces the complete file.
        // On a release run only the datasets that feed the journey are merged, and every dataset
        // also gets its own per-school file (byDataset), which is what the display reads. The
        // exception is writesDatasetFiles, above.
        Dictionary<string, JArray> mergedBySchool = new();
        Dictionary<(Guid DatasetId, string School), JArray> byDataset = new();
        Dictionary<string, int> recordCountBySchool = new();
        StringBuilder errorLogBuilder = new StringBuilder();
        int totalErrors = 0;
        int recordsRead = 0;
        int recordsValidated = 0;

        // A fresh, timestamped summary file is written on every real run, so runs never overwrite
        // each other's summary.
        string summaryBlobName =
            $"{(checkingExerciseId is { } summaryId
                ? CheckingExerciseBlobPaths.LogPrefix(summaryId) + "summary_"
                : CheckingExerciseBlobPaths.SummaryPrefix(exercise!.Value, checkingWindowId))}{DateTime.UtcNow:yyyyMMdd_HHmmss_fffffff}.csv";

        var datasetSlot = 0;
        foreach (IngressDataset dataset in datasets)
        {
            var currentDatasetSlot = datasetSlot++;
            cancellationToken.ThrowIfCancellationRequested();
            string label = multipleDatasets ? $" [{dataset.Name}]" : string.Empty;

            // re-validate the stored files against the checksums captured at upload time.
            byte[]? csvBytes = null;
            string? schemaJson = null;
            string? loadError = null;
            string? checksumError = null;
            bool readingSchema = false;
            try
            {
                csvBytes = await DownloadBytesAsync(container,
                    CheckingExerciseBlobPaths.IngressBlobName(dataset.InputCsvFile), cancellationToken);
                readingSchema = true;
                byte[] schemaBytes = await DownloadBytesAsync(container,
                    CheckingExerciseBlobPaths.SchemaBlobName(dataset.SchemaFile), cancellationToken);

                if (!ChecksumMatches(csvBytes, dataset.InputCsvChecksum))
                {
                    checksumError = $"Checksum failed for ingress file '{dataset.InputCsvFile}'.";
                }
                else if (!ChecksumMatches(schemaBytes, dataset.SchemaChecksum))
                {
                    checksumError = $"Checksum failed for schema file '{dataset.SchemaFile}'.";
                }
                else
                {
                    schemaJson = Encoding.UTF8.GetString(schemaBytes);
                }
            }
            catch (FileNotFoundException ex)
            {
                summaryCapture?.SetFailure("input_read", readingSchema ? "schema_not_found" : "input_not_found");
                logger.LogWarning(ex, "Failed to load ingress input for checking window {CheckingWindowId}", checkingWindowId);
                loadError = ex.Message;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                summaryCapture?.SetFailure("input_read", readingSchema ? "schema_read_failed" : "input_read_failed");
                logger.LogWarning(ex, "Failed to load files for checking window {CheckingWindowId}", checkingWindowId);
                loadError = ex.Message;
            }

            if (loadError is not null)
            {
                yield return Failed(loadError);
                yield break;
            }

            if (checksumError is not null)
            {
                summaryCapture?.SetFailure("checksum", "checksum_mismatch");
                yield return Failed(checksumError);
                yield break;
            }

            yield return new ValidationProgress("Checksums", $"Checksum passed{label}", recordsRead, recordsValidated, 0, totalErrors, false, false);

            JSchema? parsedSchema = null;
            try
            {
                parsedSchema = JSchema.Parse(schemaJson!);
            }
            catch (Exception)
            {
                summaryCapture?.SetFailure("schema", "schema_invalid");
            }

            if (parsedSchema is null)
            {
                yield return Failed($"Schema '{dataset.SchemaFile}' is invalid.");
                yield break;
            }

            JSchema schema = parsedSchema;
            schema.AllowAdditionalProperties = false;

            // Newtonsoft.Json.Schema counts licensed validations per call, not per row, and caps an
            // unlicensed process at 1,000 an hour. A school's rows are therefore validated as one
            // array, one call per school per dataset, rather than one call per record. The messages
            // are the same, and each already carries its row index within the school
            // ("Path '[12].ULN'") because the record's path includes its parent array.
            JSchema schoolSchema = new() { Type = JSchemaType.Array, Items = { schema } };

            // The supplier's column names differ by file and key stage (GNUMBER is the QAN; the
            // session is EXAM_YEAR_SEASON in a 16-19 late file, SEASON_AND_YEAR in a KS4 one, and
            // SEASON + EXAMYEAR in the 16-18 results file). A property's x-ingress.source names the
            // CSV column, or the columns to join, it is read from, so every file writes the keys its
            // readers expect and the code holds no list of supplier names.
            IReadOnlyList<SourceColumn> sourceColumns = SourceColumns(schema);

            // A pupil file whose rows carry their own inclusion code (KS4 P_INCL, KS2 PINCL_Flag)
            // names its rule in the schema, so the code here holds no key stage's codes. A rule the
            // schema cannot honour fails the run by name rather than at every row's validation.
            InclusionRule? inclusion = InclusionRule.From(schema);
            if (inclusion?.ErrorFor(schema) is { } inclusionError)
            {
                summaryCapture?.SetFailure("schema", "identifier_mapping_invalid");
                yield return Failed($"Schema '{dataset.SchemaFile}': {inclusionError}");
                yield break;
            }

            // #711: who may be asked to be included. Validated like the inclusion rule, so a schema
            // that cannot be honoured fails by name before any row is read.
            IncludableRule? includable = IncludableRule.From(schema);
            if (includable?.ErrorFor(schema) is { } includableError)
            {
                summaryCapture?.SetFailure("schema", "identifier_mapping_invalid");
                yield return Failed($"Schema '{dataset.SchemaFile}': {includableError}");
                yield break;
            }

            // Capture each complete source record before any schema transformation. CsvHelper
            // counts CSV records, so a quoted field containing line breaks remains one row.
            List<IDictionary<string, object>> records = [];
            string? parseFailureCode = null;
            string? parseFailureMessage = null;
            try
            {
                using var reader = new StreamReader(new MemoryStream(csvBytes!));
                using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
                summaryCapture?.MarkFileStarted(currentDatasetSlot);
                records = ReadCsvRecords(csv, summaryCapture, currentDatasetSlot,
                    InstitutionKey(schema), UrnKey(schema));
            }
            catch (MalformedIngressCsvException ex)
            {
                parseFailureCode = ex.Code;
                parseFailureMessage = ex.Message;
            }
            catch (CsvHelperException)
            {
                parseFailureCode = "malformed_row";
                parseFailureMessage = $"Ingress file '{dataset.InputCsvFile}' contains malformed CSV data.";
            }

            if (parseFailureCode is not null)
            {
                summaryCapture?.SetFailure("parsing", parseFailureCode);
                yield return Failed(parseFailureMessage!);
                yield break;
            }

            summaryCapture?.MarkFileRead(currentDatasetSlot);

            recordsRead += records.Count;

            yield return new ValidationProgress("Counting", $"{records.Count} records found{label}", recordsRead, recordsValidated, 0, totalErrors, false, false);

            // Every feed keys its rows to a school by a DfE number column, which is what lets one
            // supplier file be split into one blob per school. The column is LAESTAB unless the
            // schema's x-ingress.institutionKey names another (the previously published file has
            // LAESTAB_0). A file without it cannot be split at all, so it fails the run by name
            // rather than throwing out of the group-by.
            string institutionKey = InstitutionKey(schema);
            var institutionColumn = records.Count == 0 ? null : FindHeader(records[0].Keys, institutionKey);
            if (records.Count > 0 && institutionColumn is null)
            {
                summaryCapture?.SetFailure("schema", "identifier_mapping_invalid");
                yield return Failed(
                    $"Ingress file '{dataset.InputCsvFile}' has no {institutionKey} column, so its records " +
                    "cannot be grouped by school.");
                yield break;
            }

            List<IGrouping<string, IDictionary<string, object>>> groupedSchools = records
                .GroupBy(r => institutionColumn is not null && r.TryGetValue(institutionColumn, out object? laestab)
                    ? laestab?.ToString() ?? "UnknownSchool"
                    : "UnknownSchool")
                .ToList();

            // Validate every school group up front, collecting all errors rather than stopping at
            // the first. The transformed payload for each clean group is accumulated so we can
            // write it later only once we know every dataset is valid.
            foreach (var group in groupedSchools)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string schoolId = group.Key;
                int groupRecordCount = group.Count();
                List<string> schoolErrors = new List<string>();

                JArray jsonArray = new();
                foreach (var row in group)
                {
                    jsonArray.Add(JObject.FromObject(row));
                }

                foreach (JObject record in jsonArray.Children<JObject>())
                {
                    CopySourceColumns(record, sourceColumns);
                    RemoveFieldsNotInSchema(record, schema);
                    EnsureSchemaFieldsExist(record, schema);
                    SchemaTypeConvertor.ApplySchemaTypes(record, schema);

                    if (schema.Properties.ContainsKey("Id"))
                    {
                        string? id = record["Id"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            record["Id"] = Guid.NewGuid().ToString();
                        }
                    }

                    if (schema.Properties.ContainsKey("CheckingWindowId"))
                    {
                        string? id = record["CheckingWindowId"]?.Value<string>();
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            record["CheckingWindowId"] = checkingWindowId;
                        }
                    }

                    // Inclusion is decided here, once, and stamped as INCLUDED, so every reader
                    // reads the stamp and none knows a code. The slot's file of origin wins (the
                    // 16-19 non-included file has no P_INCL column); otherwise the schema's rule
                    // reads the record's own code. Stamped BEFORE validation because
                    // AllowAdditionalProperties is false, so the schema must declare INCLUDED.
                    if (dataset.Included is bool included && schema.Properties.ContainsKey("INCLUDED"))
                    {
                        record["INCLUDED"] = included;
                    }
                    else if (inclusion is not null)
                    {
                        record["INCLUDED"] = inclusion.IsIncluded(record);
                    }

                    // #711: who may be asked to be included — not included, or a code the schema's
                    // x-ingress.includable rule names (the 16-19 included file's P_INCL 502). Derived
                    // from INCLUDED, so it is stamped only when INCLUDED was, and only when the schema
                    // declares it (AllowAdditionalProperties is false). Readers read the stamp.
                    if (schema.Properties.ContainsKey("INCLUDABLE") &&
                        record["INCLUDED"] is { Type: JTokenType.Boolean } stampedIncluded)
                    {
                        record["INCLUDABLE"] = !stampedIncluded.Value<bool>() || (includable?.Matches(record) ?? false);
                    }

                    // Provenance by file of origin, the exact analogue of INCLUDED above: the
                    // results CSVs carry no SOURCE column, so the tag comes from the dataset slot
                    // the file was uploaded to. Stamped BEFORE validation for the same reason
                    // (AllowAdditionalProperties is false), and guarded by the schema check so a
                    // pupil-data schema is untouched. ResultRecord.SourceFile, the result
                    // picker's file column reads this.
                    if (dataset.SourceFile is { Length: > 0 } sourceFile && schema.Properties.ContainsKey("SOURCE"))
                    {
                        record["SOURCE"] = sourceFile;
                    }

                    // The supplier file may not carry the inclusion description that the school's
                    // CSV shows. Its words come from the schema's rule; a description the supplier
                    // did send is kept.
                    if (inclusion is { DescriptionField: { } descriptionField }
                        && string.IsNullOrWhiteSpace(record[descriptionField]?.ToString())
                        && inclusion.DescriptionOf(record) is { Length: > 0 } description)
                    {
                        record[descriptionField] = description;
                    }

                }

                if (!jsonArray.IsValid(schoolSchema, out IList<string> errorMessages))
                {
                    schoolErrors.AddRange(errorMessages);
                }

                if (schoolErrors.Count > 0)
                {
                    totalErrors += schoolErrors.Count;
                    errorLogBuilder.AppendLine($"--- Validation Failed for School: {schoolId}{label} ---");
                    foreach (var errorMessage in schoolErrors)
                    {
                        errorLogBuilder.AppendLine($"Row Error: {errorMessage}");
                    }
                    errorLogBuilder.AppendLine();
                }
                else
                {
                    // A pupil's Id is always ours, generated here, never the supplier's: the
                    // journeys select a pupil by it. Stamped after validation, so the supplier's
                    // schema need not declare it, and over any Id the file carries.
                    if (exercise == CheckingExerciseType.PupilData)
                    {
                        foreach (JObject record in jsonArray.Children<JObject>())
                        {
                            record["Id"] = Guid.NewGuid().ToString();
                        }
                    }

                    if (releaseId is null || dataset.FeedsJourney)
                    {
                        if (!mergedBySchool.TryGetValue(schoolId, out JArray? existing))
                        {
                            existing = new JArray();
                            mergedBySchool[schoolId] = existing;
                        }

                        foreach (JObject record in jsonArray.Children<JObject>())
                        {
                            existing.Add(record.DeepClone());
                        }
                    }

                    if (writesDatasetFiles)
                    {
                        if (!byDataset.TryGetValue((dataset.DatasetId, schoolId), out JArray? own))
                        {
                            own = new JArray();
                            byDataset[(dataset.DatasetId, schoolId)] = own;
                        }

                        foreach (JObject record in jsonArray.Children<JObject>())
                        {
                            own.Add(record.DeepClone());
                        }
                    }
                }

                // The summary counts every record read, clean or not, across all datasets.
                recordCountBySchool[schoolId] = recordCountBySchool.GetValueOrDefault(schoolId) + groupRecordCount;
                recordsValidated += groupRecordCount;

                yield return new ValidationProgress(
                    "Validating",
                    $"Validated {recordsValidated} of {recordsRead} records{label}",
                    recordsRead,
                    recordsValidated,
                    0,
                    totalErrors,
                    false,
                    false);
            }
        }

        // Per-school record counts for the summary CSV and the on-screen table, across every
        // dataset. Built from all groups so the summary is complete whether the run passes or fails.
        IReadOnlyList<SchoolRecordCount> schoolSummary = recordCountBySchool
            .Select(kv => new SchoolRecordCount(kv.Key, kv.Value))
            .ToList();

        if (recordsRead == 0)
        {
                summaryCapture?.SetFailure("validation", "record_validation_failed");
            yield return new ValidationProgress("Counting", "No records found", 0, 0, 0, 0, true, true);
            yield break;
        }

        // Any error in ANY dataset means nothing is saved. Persist the full error log so every
        // error can be retrieved, then report a failure summary. A validate-only run writes
        // nothing to storage, so its errors come back on the stream only.
        if (totalErrors > 0)
        {
                summaryCapture?.SetFailure("validation", "record_validation_failed");
            // Even on failure we still persist the summary (record counts per school) so there is
            // always a record of the run. A validate-only run writes nothing to storage.
            if (!validateOnly)
            {
                try
                {
                    await WriteAsync(container, errorLogBlobName, errorLogBuilder.ToString(), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to write error log {BlobName}", errorLogBlobName);
                }

                await WriteSummaryAsync(container, summaryBlobName, schoolSummary, checkingWindowId, cancellationToken);
            }

            yield return new ValidationProgress(
                "Failed",
                $"Validation failed with {totalErrors} error(s) across {schoolSummary.Count} school(s). No data files saved.",
                recordsRead,
                recordsValidated,
                0,
                totalErrors,
                true,
                true,
                schoolSummary);
            yield break;
        }

        // Validation passed. On a validate-only run we stop here without writing any data files
        // (or the summary CSV), returning the summary on the stream for display only.
        if (validateOnly)
        {
            yield return new ValidationProgress(
                "Complete",
                $"Validation complete. {recordsRead} record(s) validated with no errors. No files saved.",
                recordsRead,
                recordsValidated,
                0,
                0,
                true,
                false,
                schoolSummary);
            yield break;
        }

        // Every dataset is valid, so write one merged JSON file per school (and, on a release run,
        // one file per dataset per school). Track written files so partial output from an I/O
        // failure can be rolled back.
        List<string> writtenBlobNames = new List<string>();
        int recordsProcessed = 0;
        int filesWritten = 0;
        string? writeError = null;

        // Never clear a dataset that is currently good until every supplied pair has validated.
        // Clearing first meant one bad supplier file emptied the school's page.
        // A release writes under a new prefix, so the output that schools see now stays untouched
        // until the caller switches to the new release. There is nothing to clear.
        if (clearExistingFiles && !validateOnly && releaseId is null)
            await ClearOutputAsync(container, checkingWindowId, exercise, errorLogBlobName,
                cancellationToken, checkingExerciseId);

        // The merged file per school (the journey's file), then, on a release run, each
        // dataset's own file per school. Records are counted once: from the dataset files when
        // they are written, where every record lands exactly once, and from the merged files otherwise.
        IEnumerable<(string BlobName, JArray Records, bool Counted)> outputs = mergedBySchool
            .Select(entry => (
                checkingExerciseId is { } outputId
                    ? CheckingExerciseBlobPaths.DataBlobName(outputId,
                        dataType ?? CheckingExerciseBlobPaths.DefaultDataType(exercise), entry.Key, releaseId)
                    : CheckingExerciseBlobPaths.DataBlobName(exercise!.Value, entry.Key),
                entry.Value,
                !writesDatasetFiles))
            .Concat(byDataset.Select(entry => (
                CheckingExerciseBlobPaths.DatasetBlobName(checkingExerciseId!.Value, releaseId!.Value,
                    entry.Key.DatasetId, entry.Key.School),
                entry.Value,
                true)));

        foreach ((string outputBlobName, JArray jsonArray, bool counted) in outputs)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await WriteAsync(container, outputBlobName, jsonArray.ToString(Formatting.Indented), cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to write {BlobName}", outputBlobName);
                writeError = ex.Message;
                break;
            }

            writtenBlobNames.Add(outputBlobName);
            filesWritten++;
            if (counted) recordsProcessed += jsonArray.Count;

            yield return new ValidationProgress(
                "Processing",
                $"Processed {recordsProcessed} of {recordsRead} records",
                recordsRead,
                recordsProcessed,
                filesWritten,
                0,
                false,
                false);
        }

        if (writeError is not null)
        {
                summaryCapture?.SetFailure("data_write", "data_write_failed");
            await CleanUpAsync(container, writtenBlobNames, cancellationToken);
            yield return new ValidationProgress(
                "Failed",
                $"Processing stopped: {writeError}",
                recordsRead,
                recordsProcessed,
                0,
                1,
                true,
                true);
            yield break;
        }

        // Save the run summary (per-school counts plus totals) alongside the data files.
        await WriteSummaryAsync(container, summaryBlobName, schoolSummary, checkingWindowId, cancellationToken);

        yield return new ValidationProgress(
            "Complete",
            $"Validation complete. {filesWritten} file(s) written from {recordsRead} records.",
            recordsRead,
            recordsProcessed,
            filesWritten,
            0,
            true,
            false,
            schoolSummary);
    }

    private async Task WriteSummaryAsync(
        BlobContainerClient container,
        string summaryBlobName,
        IReadOnlyList<SchoolRecordCount> schoolSummary,
        Guid checkingWindowId,
        CancellationToken cancellationToken)
    {
        try
        {
            await WriteAsync(container, summaryBlobName, BuildSummaryCsv(schoolSummary), cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Failed to write summary CSV for checking window {CheckingWindowId}", checkingWindowId);
        }
    }

    private static string BuildSummaryCsv(IReadOnlyList<SchoolRecordCount> schoolSummary)
    {
        StringBuilder builder = new StringBuilder();
        builder.AppendLine("LAESTAB,RecordsProcessed");

        foreach (SchoolRecordCount school in schoolSummary)
        {
            builder.AppendLine($"{EscapeCsv(school.Laestab)},{school.RecordCount}");
        }

        builder.AppendLine();
        builder.AppendLine($"Total LAESTAB,{schoolSummary.Count}");
        builder.AppendLine($"Total records,{schoolSummary.Sum(s => s.RecordCount)}");

        return builder.ToString();
    }

    private static string EscapeCsv(string value)
    {
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
        {
            return $"\"{value.Replace("\"", "\"\"")}\"";
        }

        return value;
    }

    private async Task ClearOutputAsync(BlobContainerClient container, Guid checkingWindowId, CheckingExerciseType? exercise, string errorLogBlobName, CancellationToken cancellationToken, Guid? checkingExerciseId = null)
    {
        if (!await container.ExistsAsync(cancellationToken))
        {
            return;
        }

        List<string> blobNames = new List<string>();

        // Per-school data files and every timestamped summary from previous runs — both scoped to
        // the running exercise. Blob prefixes match as plain strings, so sweeping "data/" does not
        // reach "results-enquiry/data/" and vice versa.
        foreach (string prefix in new[]
                 {
                     checkingExerciseId is { } outputId
                        ? CheckingExerciseBlobPaths.DataPrefix(outputId)
                        : CheckingExerciseBlobPaths.DataPrefix(exercise!.Value),
                     checkingExerciseId is { } logId
                        ? CheckingExerciseBlobPaths.LogPrefix(logId)
                        : CheckingExerciseBlobPaths.SummaryPrefix(exercise!.Value, checkingWindowId)
                 })
        {
            await foreach (BlobItem blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix, cancellationToken))
            {
                blobNames.Add(blob.Name);
            }
        }

        blobNames.Add(errorLogBlobName);
        await CleanUpAsync(container, blobNames, cancellationToken);
    }

    private static ValidationProgress Failed(string message) =>
        new ValidationProgress("Failed", message, 0, 0, 0, 1, true, true);

    private async Task CleanUpAsync(BlobContainerClient container, IEnumerable<string> blobNames, CancellationToken cancellationToken)
    {
        foreach (string blobName in blobNames)
        {
            try
            {
                await container.GetBlobClient(blobName).DeleteIfExistsAsync(cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Failed to remove {BlobName} during clean up", blobName);
            }
        }
    }

    private static bool ChecksumMatches(byte[] content, string expectedChecksum)
    {
        if (string.IsNullOrWhiteSpace(expectedChecksum))
        {
            return false;
        }

        string actual = Convert.ToHexString(SHA256.HashData(content));
        return string.Equals(actual, expectedChecksum, StringComparison.OrdinalIgnoreCase);
    }

    private static void EnsureSchemaFieldsExist(JObject record, JSchema schema)
    {
        foreach (KeyValuePair<string, JSchema> schemaProperty in schema.Properties)
        {
            if (record.ContainsKey(schemaProperty.Key))
            {
                continue;
            }

            if (schema.Required.Contains(schemaProperty.Key))
            {
                continue;
            }

            if (schemaProperty.Value.Type?.HasFlag(JSchemaType.Null) == true)
            {
                record[schemaProperty.Key] = JValue.CreateNull();
                continue;
            }

            if (schemaProperty.Value.Type?.HasFlag(JSchemaType.String) == true)
            {
                record[schemaProperty.Key] = string.Empty;
                continue;
            }

            record[schemaProperty.Key] = JValue.CreateNull();
        }
    }

    /// <summary>
    /// A property read from other CSV columns: <c>"x-ingress": { "source": "GNUMBER" }</c>, or
    /// <c>{ "source": ["SEASON", "EXAMYEAR"], "separator": "" }</c> to join several.
    /// </summary>
    private sealed record SourceColumn(string Property, IReadOnlyList<string> Columns, string Separator);

    /// <summary>The CSV column that holds the school's DfE number: <c>"x-ingress": { "institutionKey": "LAESTAB_0" }</c>
    /// at the schema's root, or <c>LAESTAB</c>.</summary>
    private static List<IDictionary<string, object>> ReadCsvRecords(CsvReader csv,
        IngressSummaryCapture? summaryCapture, int slotIndex, string institutionKey, string urnKey)
    {
        if (!csv.Read())
            throw new MalformedIngressCsvException("malformed_header", "Ingress CSV has no header row.");

        csv.ReadHeader();
        var headers = csv.HeaderRecord;
        if (headers is null || headers.Length == 0)
            throw new MalformedIngressCsvException("malformed_header", "Ingress CSV has no header row.");

        var normalizedHeaders = headers.ToArray();
        normalizedHeaders[0] = normalizedHeaders[0].TrimStart('\uFEFF');
        if (normalizedHeaders.Any(string.IsNullOrWhiteSpace) ||
            normalizedHeaders.Distinct(StringComparer.OrdinalIgnoreCase).Count() != normalizedHeaders.Length)
            throw new MalformedIngressCsvException("malformed_header", "Ingress CSV has a blank or duplicate header.");

        var institutionColumn = FindHeader(normalizedHeaders, institutionKey);
        var urnColumn = FindHeader(normalizedHeaders, urnKey);
        summaryCapture?.SetUrnColumn(slotIndex, urnColumn is not null);

        var records = new List<IDictionary<string, object>>();
        while (csv.Read())
        {
            if (csv.Parser.Count != normalizedHeaders.Length)
                throw new MalformedIngressCsvException("malformed_row", "Ingress CSV has a row with the wrong number of columns.");

            var values = Enumerable.Range(0, csv.Parser.Count).Select(index => csv.GetField(index)).ToArray();
            if (values.Length == 0 || values.All(string.IsNullOrWhiteSpace))
                throw new MalformedIngressCsvException("malformed_row", "Ingress CSV contains a blank row.");

            var record = new Dictionary<string, object>(StringComparer.Ordinal);
            for (var column = 0; column < normalizedHeaders.Length; column++)
                record.Add(normalizedHeaders[column], values[column] ?? string.Empty);

            records.Add(record);
            summaryCapture?.AddRow(slotIndex,
                ReadIdentifier(record, institutionColumn), ReadIdentifier(record, urnColumn));
        }

        return records;
    }

    private static string? FindHeader(IEnumerable<string> headers, string requested)
        => headers.FirstOrDefault(header => string.Equals(header, requested, StringComparison.OrdinalIgnoreCase));

    private static string? ReadIdentifier(IDictionary<string, object> record, string? column)
    {
        if (column is null || !record.TryGetValue(column, out var value))
            return null;

        var identifier = value.ToString();
        return string.IsNullOrWhiteSpace(identifier) ? null : identifier.Trim();
    }

    private sealed class MalformedIngressCsvException(string code, string message) : Exception(message)
    {
        public string Code { get; } = code;
    }

    private static string InstitutionKey(JSchema schema) =>
        schema.ExtensionData.TryGetValue("x-ingress", out JToken? ingress) &&
        ingress["institutionKey"] is JValue { Type: JTokenType.String } key &&
        !string.IsNullOrWhiteSpace(key.Value<string>())
            ? key.Value<string>()!
            : "LAESTAB";

    private static string UrnKey(JSchema schema) =>
        schema.ExtensionData.TryGetValue("x-ingress", out JToken? ingress) &&
        ingress["urnKey"] is JValue { Type: JTokenType.String } key &&
        !string.IsNullOrWhiteSpace(key.Value<string>())
            ? key.Value<string>()!
            : "URN";

    /// <summary>
    /// Which pupils a file includes, by their own code: <c>"x-ingress": { "inclusion": { "field":
    /// "P_INCL", "included": ["201"], "descriptionField": "P_INCL_DESC", "descriptions": { "201":
    /// "..." } } }</c> at the schema's root. Codes are compared as trimmed text.
    /// </summary>
    private sealed record InclusionRule(string Field, IReadOnlySet<string> Included, string? DescriptionField,
        IReadOnlyDictionary<string, string> Descriptions)
    {
        public static InclusionRule? From(JSchema schema)
        {
            if (!schema.ExtensionData.TryGetValue("x-ingress", out JToken? ingress) ||
                ingress["inclusion"] is not JObject rule)
                return null;

            return new InclusionRule(
                rule["field"]?.Value<string>() ?? string.Empty,
                (rule["included"] as JArray ?? []).Select(code => code.ToString().Trim()).ToHashSet(StringComparer.Ordinal),
                rule["descriptionField"]?.Value<string>(),
                (rule["descriptions"] as JObject ?? [])
                    .Properties().ToDictionary(p => p.Name.Trim(), p => p.Value.ToString(), StringComparer.Ordinal));
        }

        public string? ErrorFor(JSchema schema) =>
            string.IsNullOrWhiteSpace(Field) ? "x-ingress.inclusion names no field."
            : !schema.Properties.ContainsKey(Field) ? $"x-ingress.inclusion reads {Field}, which the schema does not declare."
            : !schema.Properties.ContainsKey("INCLUDED") ? "x-ingress.inclusion needs the schema to declare INCLUDED, the stamp it writes."
            : DescriptionField is not null && !schema.Properties.ContainsKey(DescriptionField)
                ? $"x-ingress.inclusion writes {DescriptionField}, which the schema does not declare."
            : null;

        public bool IsIncluded(JObject record) => Included.Contains(Code(record));

        public string? DescriptionOf(JObject record) => Descriptions.GetValueOrDefault(Code(record));

        private string Code(JObject record) =>
            record[Field] is { Type: not JTokenType.Null } value ? value.ToString().Trim() : string.Empty;
    }

    /// <summary>
    /// Which records a school may ask to include although their file counts them as included:
    /// <c>"x-ingress": { "includable": { "field": "P_INCL", "codes": ["502"] } }</c> at the
    /// schema's root. Codes are compared as trimmed text. A record that is not included is
    /// includable without a rule; the rule only adds to that (#711).
    /// </summary>
    private sealed record IncludableRule(string Field, IReadOnlySet<string> Codes)
    {
        public static IncludableRule? From(JSchema schema)
        {
            if (!schema.ExtensionData.TryGetValue("x-ingress", out JToken? ingress) ||
                ingress["includable"] is not JObject rule)
                return null;

            return new IncludableRule(
                rule["field"]?.Value<string>() ?? string.Empty,
                (rule["codes"] as JArray ?? []).Select(code => code.ToString().Trim()).ToHashSet(StringComparer.Ordinal));
        }

        public string? ErrorFor(JSchema schema) =>
            string.IsNullOrWhiteSpace(Field) ? "x-ingress.includable names no field."
            : !schema.Properties.ContainsKey(Field) ? $"x-ingress.includable reads {Field}, which the schema does not declare."
            : !schema.Properties.ContainsKey("INCLUDABLE") ? "x-ingress.includable needs the schema to declare INCLUDABLE, the stamp it writes."
            : null;

        public bool Matches(JObject record) =>
            Codes.Contains(record[Field] is { Type: not JTokenType.Null } value ? value.ToString().Trim() : string.Empty);
    }

    private static IReadOnlyList<SourceColumn> SourceColumns(JSchema schema)
    {
        List<SourceColumn> sources = [];
        foreach (KeyValuePair<string, JSchema> property in schema.Properties)
        {
            if (!property.Value.ExtensionData.TryGetValue("x-ingress", out JToken? ingress)) continue;

            string[] columns = ingress["source"] switch
            {
                JArray list => [.. list.Values<string>().OfType<string>()],
                JValue { Type: JTokenType.String } single => [single.Value<string>()!],
                _ => []
            };
            if (columns.Length > 0)
                sources.Add(new SourceColumn(property.Key, columns, ingress["separator"]?.Value<string>() ?? string.Empty));
        }
        return sources;
    }

    // A copy, not a move: a CSV that already carries the property by its own name keeps its value,
    // and the source columns are then dropped with every other column the schema does not declare.
    // Blank parts are skipped when joining, so a missing season does not leave a stray separator.
    private static void CopySourceColumns(JObject record, IReadOnlyList<SourceColumn> sourceColumns)
    {
        foreach (SourceColumn source in sourceColumns)
        {
            if (record.ContainsKey(source.Property)) continue;

            List<string> parts = [.. source.Columns
                .Select(c => record.TryGetValue(c, out JToken? value) ? value.Type == JTokenType.Null ? null : value.ToString() : null)
                .Where(v => !string.IsNullOrWhiteSpace(v))
                .Select(v => v!.Trim())];
            if (parts.Count > 0 || source.Columns.Any(record.ContainsKey))
                record[source.Property] = parts.Count > 0 ? string.Join(source.Separator, parts) : null;
        }
    }

    private static void RemoveFieldsNotInSchema(JObject record, JSchema schema)
    {
        foreach (JProperty property in record.Properties().ToList())
        {
            if (!schema.Properties.ContainsKey(property.Name))
            {
                property.Remove();
            }
        }
    }

    private async Task<byte[]> DownloadBytesAsync(BlobContainerClient container, string blobName, CancellationToken cancellationToken)
    {
        await using Stream stream = await OpenReadAsync(container, blobName, cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        return buffer.ToArray();
    }

    private async Task<Stream> OpenReadAsync(BlobContainerClient container, string blobName, CancellationToken cancellationToken)
    {
        BlobClient? blob = container.GetBlobClient(blobName);

        if (!await blob.ExistsAsync(cancellationToken))
        {
            throw new FileNotFoundException(
                $"Source blob '{blobName}' was not found in container '{container.Name}'.",
                blobName);
        }

        Response<BlobDownloadStreamingResult>? response = await blob.DownloadStreamingAsync(cancellationToken: cancellationToken);
        return response.Value.Content;
    }

    private async Task WriteAsync(
        BlobContainerClient container,
        string blobName,
        string content,
        CancellationToken cancellationToken = default)
    {
        await container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);

        var blob = container.GetBlobClient(blobName);

        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        await blob.UploadAsync(stream, overwrite: true, cancellationToken);
    }
}


public sealed record ProcessingResult(
    int RecordsRead,
    int FilesWritten,
    int ErrorCount,
    StringBuilder ErrorLogs,
    IReadOnlyList<SchoolRecordCount>? SchoolSummary = null
    );
