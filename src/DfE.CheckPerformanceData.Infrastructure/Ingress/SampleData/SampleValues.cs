using System.Globalization;

namespace DfE.CheckPerformanceData.Infrastructure.Ingress.SampleData;

/// <summary>
/// #939: the values that more than one sample data generator uses. Every value is made up. The
/// dev seed data uses the same values.
/// </summary>
public static class SampleValues
{
    public static readonly IReadOnlyList<string> Firstnames =
    [
        "Alice", "Bob", "Charlie", "Diana", "Edward", "Fiona", "George", "Hannah", "Ian", "Julia",
        "Kevin", "Laura", "Michael", "Nina", "Oscar", "Paula", "Quinn", "Rachel", "Steven", "Tina"
    ];

    public static readonly IReadOnlyList<string> Surnames =
    [
        "Smith", "Jones", "Williams", "Taylor", "Brown", "Davies", "Evans", "Wilson", "Thomas", "Roberts",
        "Johnson", "Lewis", "Walker", "Robinson", "Wood", "Thompson", "White", "Watson", "Jackson", "Harris"
    ];

    /// <summary>
    /// A small number (0 to 4) from the school's LAESTAB. The generators use it to make schools
    /// differ, for example in the size of the cohort. The same LAESTAB always gives the same number.
    /// </summary>
    public static int SchoolIndex(string laestab)
    {
        var total = 0;
        for (var i = 0; i < laestab.Length; i++)
        {
            if (char.IsAsciiDigit(laestab[i]))
                total += (laestab[i] - '0') * (i + 1);
        }
        return total % 5;
    }

    public static string Number(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string TwoPlaces(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>A percentage as schools see it: a whole number with a % sign.</summary>
    public static string Pct(int value) => $"{Number(value)}%";

    public static string Percent(int count, int of) =>
        Pct(of == 0 ? 0 : (int)Math.Round(100m * count / of, MidpointRounding.AwayFromZero));

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);
}
