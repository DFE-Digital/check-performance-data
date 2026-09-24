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
        Guid? checkingExerciseId = null, CheckingDataType? dataType = null, Guid? releaseId = null)
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
            yield return Failed("No ingress datasets are configured for this window.");
            yield break;
        }

        if (!blobClients.TryGetValue("app", out var sourceBlobClient))
        {
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

        // Records from every dataset are merged per school, so one run produces the complete file.
        // On a release run only the datasets that feed the journey are merged, and every dataset
        // also gets its own per-school file (byDataset), which is what the display reads.
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

        foreach (IngressDataset dataset in datasets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string label = multipleDatasets ? $" [{dataset.Name}]" : string.Empty;

            // re-validate the stored files against the checksums captured at upload time.
            byte[]? csvBytes = null;
            string? schemaJson = null;
            string? loadError = null;
            string? checksumError = null;
            try
            {
                csvBytes = await DownloadBytesAsync(container,
                    CheckingExerciseBlobPaths.IngressBlobName(dataset.InputCsvFile), cancellationToken);
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
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
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
                yield return Failed(checksumError);
                yield break;
            }

            yield return new ValidationProgress("Checksums", $"Checksum passed{label}", recordsRead, recordsValidated, 0, totalErrors, false, false);

            JSchema schema = JSchema.Parse(schemaJson!);
            schema.AllowAdditionalProperties = false;

            // Newtonsoft.Json.Schema counts licensed validations per call, not per row, and caps an
            // unlicensed process at 1,000 an hour. A school's rows are therefore validated as one
            // array, one call per school per dataset, rather than one call per record. The messages
            // are the same, and each already carries its row index within the school
            // ("Path '[12].ULN'") because the record's path includes its parent array.
            JSchema schoolSchema = new() { Type = JSchemaType.Array, Items = { schema } };

            // Read the records and report how many there are.
            List<IDictionary<string, object>> records;
            using (var reader = new StreamReader(new MemoryStream(csvBytes!)))
            using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
            {
                records = csv.GetRecords<dynamic>()
                    .Cast<IDictionary<string, object>>()
                    .ToList();
            }

            recordsRead += records.Count;

            yield return new ValidationProgress("Counting", $"{records.Count} records found{label}", recordsRead, recordsValidated, 0, totalErrors, false, false);

            // Every feed keys its rows to a school by a LAESTAB column, which is what lets one
            // supplier file be split into one blob per school. A file without it cannot be split at
            // all, so it fails the run by name rather than throwing out of the group-by.
            if (records.Count > 0 && !records[0].ContainsKey("LAESTAB"))
            {
                yield return Failed(
                    $"Ingress file '{dataset.InputCsvFile}' has no LAESTAB column, so its records " +
                    "cannot be grouped by school.");
                yield break;
            }

            List<IGrouping<string, IDictionary<string, object>>> groupedSchools = records
                .GroupBy(r => r.TryGetValue("LAESTAB", out object? laestab)
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

                    // Inclusion by file of origin: the 16-19 non-included file has no P_INCL
                    // column, so the marker is stamped here. Stamped BEFORE validation because
                    // AllowAdditionalProperties is false — both 16-19 schemas must declare
                    // INCLUDED as a boolean. Guarded by the schema check so KS4 is untouched.
                    if (dataset.Included is bool included && schema.Properties.ContainsKey("INCLUDED"))
                    {
                        record["INCLUDED"] = included;
                    }

                    // Provenance by file of origin, the exact analogue of INCLUDED above: the
                    // results CSVs carry no SOURCE column, so the tag comes from the dataset slot
                    // the file was uploaded to. Stamped BEFORE validation for the same reason
                    // (AllowAdditionalProperties is false), and guarded by the schema check so a
                    // pupil-data schema is untouched. StudentResultRecord.SourceFile, the result
                    // picker's file column and ILateResultsAvailability all read this.
                    if (dataset.SourceFile is { Length: > 0 } sourceFile && schema.Properties.ContainsKey("SOURCE"))
                    {
                        record["SOURCE"] = sourceFile;
                    }

                    // The KS4 supplier file has no P_INCL_DESC column, but the school's CSV has a
                    // "Pupil Inclusion description" column. Its words come from the P_INCL code.
                    // A description the supplier did send is kept. Guarded by the schema check,
                    // for the same reason as INCLUDED and SOURCE above.
                    if (schema.Properties.ContainsKey("P_INCL_DESC")
                        && string.IsNullOrWhiteSpace(record["P_INCL_DESC"]?.ToString())
                        && int.TryParse(record["P_INCL"]?.ToString(), out var pinclCode)
                        && PupilInclusion.Ks4Description(pinclCode) is { Length: > 0 } pinclDescription)
                    {
                        record["P_INCL_DESC"] = pinclDescription;
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

                    if (releaseId is not null)
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
            yield return new ValidationProgress("Counting", "No records found", 0, 0, 0, 0, true, true);
            yield break;
        }

        // Any error in ANY dataset means nothing is saved. Persist the full error log so every
        // error can be retrieved, then report a failure summary. A validate-only run writes
        // nothing to storage, so its errors come back on the stream only.
        if (totalErrors > 0)
        {
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
        // dataset's own file per school. Records are counted once: from the dataset files on a
        // release run, where every record lands exactly once, and from the merged files otherwise.
        IEnumerable<(string BlobName, JArray Records, bool Counted)> outputs = mergedBySchool
            .Select(entry => (
                checkingExerciseId is { } outputId
                    ? CheckingExerciseBlobPaths.DataBlobName(outputId,
                        dataType ?? CheckingExerciseBlobPaths.DefaultDataType(exercise), entry.Key, releaseId)
                    : CheckingExerciseBlobPaths.DataBlobName(exercise!.Value, entry.Key),
                entry.Value,
                releaseId is null))
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
