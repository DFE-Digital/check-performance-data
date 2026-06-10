using System.Text.RegularExpressions;

namespace DfE.CheckPerformanceData.Web.Admin.Rules;

/// <summary>
/// Validates a proposed new outcome key. Keys appear in rules.json, audit rows and Zendesk
/// tickets, so they must be a simple identifier (letter then letters/digits) and unique.
/// </summary>
public static class OutcomeKeyValidator
{
    private static readonly Regex KeyPattern = new("^[A-Za-z][A-Za-z0-9]*$", RegexOptions.Compiled);

    public static IReadOnlyList<string> Validate(string? key, IEnumerable<string> existingKeys)
    {
        var errors = new List<string>();
        var k = key?.Trim() ?? string.Empty;

        if (string.IsNullOrEmpty(k))
        {
            errors.Add("Enter an outcome key.");
        }
        else if (!KeyPattern.IsMatch(k))
        {
            errors.Add("The key must start with a letter and contain only letters and numbers (no spaces or punctuation).");
        }
        else if (existingKeys.Contains(k, StringComparer.Ordinal))
        {
            errors.Add($"An outcome with the key '{k}' already exists.");
        }

        return errors;
    }
}
