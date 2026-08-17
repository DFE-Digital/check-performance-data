using System.Text.Json;
using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// The single <see cref="JsonSerializerOptions"/> instance the results-enquiry blob readers use.
/// Held in Application (rather than on the Infrastructure client, as the pupil schema does) so the
/// serialization contract for the read model lives beside the read model, and so the unit tests can
/// bind fixtures through the exact instance production uses without depending on the blob client.
/// <c>StudentResultsBlobClient.JsonOptions</c> and <c>GradeReferenceBlobClient.JsonOptions</c> both
/// forward here.
/// </summary>
public static class ResultsEnquiryJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(), new TolerantStringJsonConverter() }
    };
}
