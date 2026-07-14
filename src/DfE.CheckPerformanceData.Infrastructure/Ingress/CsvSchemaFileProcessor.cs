using System.Globalization;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using CsvHelper;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Schema;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public class CsvSchemaFileProcessor(ILogger<CsvSchemaFileProcessor> logger, IReadOnlyDictionary<string, BlobServiceClient> blobClients) : ICsvSchemaFileProcessor
{
    public async IAsyncEnumerable<ValidationProgress> ProcessAsync(
        Guid checkingWindowId,
        string inputCsvFile,
        string inputCsvChecksum,
        string schemaFile,
        string schemaChecksum,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (!blobClients.TryGetValue("app", out var sourceBlobClient))
        {
            logger.LogWarning("Ingress storage client is not configured");
            yield return Failed("App storage is not configured.");
            yield break;
        }

        string errorLogBlobName = $"{checkingWindowId}_error_log.txt";
        BlobContainerClient container = sourceBlobClient.GetBlobContainerClient(checkingWindowId.ToString());

        // re-validate the stored files against the checksums captured at upload time.
        
        byte[]? csvBytes = null;
        string? schemaJson = null;
        string? loadError = null;
        string? checksumError = null;
        try
        {
            csvBytes = await DownloadBytesAsync(container, $"ingress/{inputCsvFile}", cancellationToken);
            byte[] schemaBytes = await DownloadBytesAsync(container, $"schema/{schemaFile}", cancellationToken);

            if (!ChecksumMatches(csvBytes, inputCsvChecksum))
            {
                checksumError = $"Checksum failed for ingress file '{inputCsvFile}'.";
            }
            else if (!ChecksumMatches(schemaBytes, schemaChecksum))
            {
                checksumError = $"Checksum failed for schema file '{schemaFile}'.";
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

        yield return new ValidationProgress("Checksums", "Checksum passed", 0, 0, 0, 0, false, false);

        JSchema schema = JSchema.Parse(schemaJson!);
        schema.AllowAdditionalProperties = false;

        // Step 4-5: read the records and report how many there are.
        List<IDictionary<string, object>> records;
        using (var reader = new StreamReader(new MemoryStream(csvBytes!)))
        using (var csv = new CsvReader(reader, CultureInfo.InvariantCulture))
        {
            records = csv.GetRecords<dynamic>()
                .Cast<IDictionary<string, object>>()
                .ToList();
        }

        int recordsRead = records.Count;

        if (recordsRead == 0)
        {
            yield return new ValidationProgress("Counting", "No records found", 0, 0, 0, 0, true, true);
            yield break;
        }

        yield return new ValidationProgress("Counting", $"{recordsRead} records found", recordsRead, 0, 0, 0, false, false);

        List<IGrouping<string, IDictionary<string, object>>> groupedSchools = records
            .GroupBy(r => r["LAESTAB"]?.ToString() ?? "UnknownSchool")
            .ToList();

        // Step 6: process each school group, reporting progress. Track written files so we can
        // roll them back if an error is found, and stop at the first invalid group.
        List<string> writtenBlobNames = new List<string>();
        StringBuilder errorLogBuilder = new StringBuilder();
        int recordsProcessed = 0;
        int filesWritten = 0;
        string? failedSchoolId = null;
        string? writeError = null;

        foreach (var group in groupedSchools)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string schoolId = group.Key;
            int groupRecordCount = group.Count();
            List<string> schoolErrors = new List<string>();

            string serializedPayload = JsonConvert.SerializeObject(group);
            JArray jsonArray = JArray.Parse(serializedPayload);

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

                if (!record.IsValid(schema, out IList<string> errorMessages))
                {
                    schoolErrors.AddRange(errorMessages);
                }
            }

            if (schoolErrors.Count > 0)
            {
                // First error: record it, stop processing and stop writing files.
                failedSchoolId = schoolId;
                errorLogBuilder.AppendLine($"--- Validation Failed for School: {schoolId} ---");
                foreach (var errorMessage in schoolErrors)
                {
                    errorLogBuilder.AppendLine($"Row Error: {errorMessage}");
                }
                errorLogBuilder.AppendLine();
                break;
            }

            string outputBlobName = $"data/{schoolId}_pupils.json";
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
            recordsProcessed += groupRecordCount;

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

        // on failure remove everything written this run; on success report the summary.
        if (failedSchoolId is not null || writeError is not null)
        {
            await CleanUpAsync(container, writtenBlobNames, cancellationToken);

            if (errorLogBuilder.Length > 0)
            {
                try
                {
                    await WriteAsync(container, errorLogBlobName, errorLogBuilder.ToString(), cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Failed to write error log {BlobName}", errorLogBlobName);
                }
            }

            string message = writeError is not null
                ? $"Processing stopped: {writeError}"
                : $"Validation failed for school {failedSchoolId}. {writtenBlobNames.Count} written file(s) removed.";

            yield return new ValidationProgress("Failed", message, recordsRead, recordsProcessed, 0, 1, true, true);
            yield break;
        }

        yield return new ValidationProgress(
            "Complete",
            $"Validation complete. {filesWritten} file(s) written from {recordsRead} records.",
            recordsRead,
            recordsProcessed,
            filesWritten,
            0,
            true,
            false);
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
    StringBuilder ErrorLogs
    );
