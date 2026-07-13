using Newtonsoft.Json;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress;

public class PupilInclusionFlag
{
    [JsonProperty("id")]
    public int Id { get; set; }

    [JsonProperty("inclusion")]
    public string Inclusion { get; set; } = string.Empty;

    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    public bool IsIncluded =>
        Inclusion switch
        {
            "Included" => true,
            "Not Included" => false,
            _ => throw new ArgumentOutOfRangeException(
                nameof(Inclusion),
                Inclusion,
                "Inclusion must be either 'Included' or 'Not Included'.")
        };
}

public class PupilInclusionFlagLookup
{
    private readonly IReadOnlyList<PupilInclusionFlag> _flags;

    private PupilInclusionFlagLookup(IReadOnlyList<PupilInclusionFlag> flags)
    {
        _flags = flags;
    }

    public static PupilInclusionFlagLookup LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Pupil inclusion flag file missing at {filePath}", filePath);
        }

        string json = File.ReadAllText(filePath);

        var flags = JsonConvert.DeserializeObject<List<PupilInclusionFlag>>(json)
            ?? [];

        return new PupilInclusionFlagLookup(flags);
    }

    public static async Task<PupilInclusionFlagLookup> LoadFromStreamAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        string json = await reader.ReadToEndAsync(cancellationToken);

        var flags = JsonConvert.DeserializeObject<List<PupilInclusionFlag>>(json)
            ?? [];

        return new PupilInclusionFlagLookup(flags);
    }

    public bool? GetIsIncludedById(int id) =>
        _flags.FirstOrDefault(flag => flag.Id == id)?.IsIncluded;
    

    public PupilInclusionFlag? GetById(int id) =>
        _flags.FirstOrDefault(flag => flag.Id == id);

    public string? GetDescription(int id) =>
        _flags.FirstOrDefault(flag => flag.Id == id)?.Description;
}