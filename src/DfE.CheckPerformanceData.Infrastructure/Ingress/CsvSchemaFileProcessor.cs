using System.Globalization;
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
    public async Task<ProcessingResult> ProcessAsync(
        Guid checkingWindowId,
        string inputCsvFile,
        string schemaFile,
        CancellationToken cancellationToken = default)    
    {
        if (!blobClients.TryGetValue("app", out var sourceBlobClient))
        {
            logger.LogWarning("Ingress storage client is not configured");
            throw new InvalidOperationException("Ingress storage is not configured.");
        }
        
        string errorLogBlobName = $"{checkingWindowId}_error_log.txt";
        
        BlobContainerClient? container = sourceBlobClient.GetBlobContainerClient(checkingWindowId.ToString());

        await using var inputCsvStream =
            await OpenReadAsync(container, $"ingress/{inputCsvFile}", cancellationToken);
        
        await using var schemaStream =
            await OpenReadAsync(container, $"schema/{schemaFile}", cancellationToken);
        
        // await using var pupilInclusionFlagsStream =
        //     await OpenReadAsync(container, "schema/inclusion.json", cancellationToken);
        
        using var schemaReader = new StreamReader(schemaStream, leaveOpen: true);
        string schemaJson = await schemaReader.ReadToEndAsync(cancellationToken);

        JSchema schema = JSchema.Parse(schemaJson);
        schema.AllowAdditionalProperties = false;

        // PupilInclusionFlagLookup pupilInclusionFlags =
        //     await PupilInclusionFlagLookup.LoadFromStreamAsync(
        //         pupilInclusionFlagsStream,
        //         cancellationToken);

        using var reader = new StreamReader(inputCsvStream, leaveOpen: true);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);

        List<IDictionary<string, object>> records = csv.GetRecords<dynamic>()
            .Cast<IDictionary<string, object>>()
            .ToList();

        IEnumerable<IGrouping<string, IDictionary<string, object>>> groupedSchools = records
            .GroupBy(r => r["LAESTAB"]?.ToString() ?? "UnknownSchool");

        StringBuilder errorLogBuilder = new StringBuilder();

        int totalErrorCount = 0;
        int filesWritten = 0;

        foreach (var group in groupedSchools)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string schoolId = group.Key ?? "UnknownSchool";
            List<string> schoolErrors = new List<string>();

            string serializedPayload = JsonConvert.SerializeObject(group);
            JArray jsonArray = JArray.Parse(serializedPayload);

            bool isGroupValid = true;

            foreach (JObject record in jsonArray.Children<JObject>())
            {
                RemoveFieldsNotInSchema(record, schema);
                EnsureSchemaFieldsExist(record, schema);
                SchemaTypeConvertor.ApplySchemaTypes(record, schema);

                // if (schema.Properties.ContainsKey("P_INCL") && schema.Properties.ContainsKey("P_INCL_DESC"))
                // {
                //     string? pupilInclusionFlagRaw = record["P_INCL"]?.Value<string>();
                //
                //     if (int.TryParse(
                //             pupilInclusionFlagRaw,
                //             NumberStyles.Integer,
                //             CultureInfo.InvariantCulture,
                //             out int pupilInclusionFlagId))
                //     {
                //         string? description = pupilInclusionFlags.GetDescription(pupilInclusionFlagId);
                //
                //         if (!string.IsNullOrWhiteSpace(description))
                //         {
                //             record["P_INCL_DESC"] = description;
                //         }
                //     }
                // }
                
                //Add in the Id and Checking Window
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
                    isGroupValid = false;
                    schoolErrors.AddRange(errorMessages);
                    totalErrorCount += errorMessages.Count;
                }
            }

            if (isGroupValid)
            {
                string outputBlobName = $"data/{schoolId}_pupils.json";
                string json = jsonArray.ToString(Formatting.Indented);

                await WriteAsync(
                    container,
                    outputBlobName,
                    json,
                    cancellationToken);

                filesWritten++;
            }
            else
            {
                errorLogBuilder.AppendLine($"--- Validation Failed for School: {schoolId} ---");

                foreach (var errorMessage in schoolErrors)
                {
                    errorLogBuilder.AppendLine($"Row Error: {errorMessage}");
                }

                errorLogBuilder.AppendLine();
            }
        }

        if (errorLogBuilder.Length > 0)
        {
            await WriteAsync(
                container,
                errorLogBlobName,
                errorLogBuilder.ToString(),
                cancellationToken);
        }

        return new ProcessingResult(
            RecordsRead: records.Count,
            FilesWritten: filesWritten,
            ErrorCount: totalErrorCount, 
            ErrorLogs:errorLogBuilder);
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
    
    public async Task<Stream> OpenReadAsync(BlobContainerClient container, string blobName, CancellationToken cancellationToken)
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
    
    public async Task WriteAsync(
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