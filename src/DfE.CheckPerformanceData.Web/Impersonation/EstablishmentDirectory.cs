using System.Globalization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.VisualBasic.FileIO;

namespace DfE.CheckPerformanceData.Web.Impersonation;

public sealed class EstablishmentDirectory
{
    private const int MaximumResults = 25;
    private readonly EstablishmentDetails[] establishments;

    public EstablishmentDirectory(IWebHostEnvironment environment)
    {
        var path = Path.Combine(environment.ContentRootPath, "Data", "Impersonation", "EstablishmentDetails.csv");
        using var parser = new TextFieldParser(path);
        parser.TextFieldType = FieldType.Delimited;
        parser.SetDelimiters(",");
        parser.HasFieldsEnclosedInQuotes = true;

        var headers = parser.ReadFields() ?? throw new InvalidDataException("The establishment directory has no header row.");
        var columns = headers.Select((header, index) => (header, index))
            .ToDictionary(item => item.header.Trim(), item => item.index, StringComparer.OrdinalIgnoreCase);
        var urnColumn = Column("URN");
        var laColumn = Column("LA (code)");
        var establishmentNumberColumn = Column("EstablishmentNumber");
        var nameColumn = Column("EstablishmentName");
        var lowestAgeColumn = Column("StatutoryLowAge");
        var highestAgeColumn = Column("StatutoryHighAge");

        var rows = new List<EstablishmentDetails>();
        while (!parser.EndOfData)
        {
            var fields = parser.ReadFields();
            if (fields is null || fields.Length <= Math.Max(nameColumn, highestAgeColumn)) continue;

            var laCode = Value(fields, laColumn);
            var establishmentNumber = Value(fields, establishmentNumberColumn);
            var laestab = string.IsNullOrWhiteSpace(laCode) || string.IsNullOrWhiteSpace(establishmentNumber)
                ? string.Empty
                : $"{laCode.PadLeft(3, '0')}{establishmentNumber.PadLeft(4, '0')}";

            rows.Add(new EstablishmentDetails(
                Value(fields, urnColumn),
                laestab,
                Value(fields, nameColumn),
                Age(fields, lowestAgeColumn),
                Age(fields, highestAgeColumn)));
        }

        establishments = rows.ToArray();

        int Column(string name) => columns.TryGetValue(name, out var index)
            ? index
            : throw new InvalidDataException($"The establishment directory is missing the {name} column.");
    }

    public IReadOnlyList<EstablishmentDetails> Search(string? query)
    {
        var term = query?.Trim();
        if (string.IsNullOrEmpty(term) || term.Length < 2) return [];

        return establishments
            .Where(item => item.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                || item.Urn.Contains(term, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Urn.Equals(term, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(item => item.Name.StartsWith(term, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(MaximumResults)
            .ToArray();
    }

    private static string Value(string[] fields, int index) => index < fields.Length ? fields[index].Trim() : string.Empty;

    private static int? Age(string[] fields, int index) =>
        int.TryParse(Value(fields, index), NumberStyles.None, CultureInfo.InvariantCulture, out var age) ? age : null;
}

public sealed record EstablishmentDetails(string Urn, string Laestab, string Name, int? LowestAge, int? HighestAge);
