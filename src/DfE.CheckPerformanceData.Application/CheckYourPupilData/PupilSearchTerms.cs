namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The one rule every pupil search box uses: the journey autocomplete, the pupil data tabs and the
/// schema-driven exercise tabs.
///
/// The query is split into words on spaces and commas. Each word must match the start of a word in
/// at least one field, ignoring case. So "John Smith", "Smith John" and "Smith, John" all find John
/// Smith, "Berg" finds "Van Der Berg", and "ohn" finds nothing. Field words split on spaces and
/// hyphens. The rule names no field, so a caller that only knows a schema's searchable columns can
/// use it as well as one that knows the pupil's names.
///
/// The Add-pupil duplicate check does not use it: it is not a search box, and it matches names
/// fuzzily on purpose (see <see cref="PupilSuggestionFormat.NameMatchesForDuplicateCheck"/>).
/// </summary>
public static class PupilSearchTerms
{
    private static readonly char[] QuerySeparators = [' ', ',', '\t'];

    public static bool MatchesEveryWord(string? query, IEnumerable<string?> fields)
    {
        if (string.IsNullOrWhiteSpace(query)) return false;

        var words = query.Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return false;

        var values = fields.Where(f => !string.IsNullOrEmpty(f)).Select(f => f!).ToList();
        return words.All(word => values.Any(value => StartsAWord(value, word)));
    }

    private static bool StartsAWord(string value, string word)
    {
        for (var i = 0; i + word.Length <= value.Length; i++)
        {
            var atWordStart = i == 0 || value[i - 1] is ' ' or '-';
            if (atWordStart && string.Compare(value, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
        }

        return false;
    }
}
