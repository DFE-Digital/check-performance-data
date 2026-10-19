namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// The rule every pupil search box uses: the journey autocomplete, the pupil data tabs, the results
/// table and the schema-driven exercise tabs. The Add-pupil duplicate check does not use it: it is
/// not a search box, and it matches names fuzzily on purpose
/// (see <see cref="PupilSuggestionFormat.NameMatchesForDuplicateCheck"/>).
///
/// The query is split into words on spaces, tabs and commas. Each word must match the start of a
/// word in at least one field, ignoring case. So "John Smith", "Smith John" and "Smith, John" all
/// find John Smith, "Berg" finds "Van Der Berg", "Brien" finds "O'Brien", and "ohn" finds nothing.
/// Field words start after a space, tab, hyphen or apostrophe.
///
/// This is start-of-word matching on every field, identifiers too: "A860" finds the UPN
/// "A8604070001B" but "8604" does not, and "12/03" finds the date "12/03/2010" but "03" does not.
/// Because a comma separates words, a typed comma is never part of a word, so it can never match a
/// field value that holds a comma.
///
/// The rule names no field, so a caller that only knows a schema's searchable columns can use it
/// as well as one that knows the pupil's names. Parse the query once with <see cref="Parse"/>, then
/// call <see cref="Query.Matches(ReadOnlySpan{string?})"/> for each row.
/// </summary>
public static class PupilSearchTerms
{
    private static readonly char[] QuerySeparators = [' ', ',', '\t'];

    public static Query Parse(string? query)
        => new(string.IsNullOrWhiteSpace(query)
            ? []
            : query.Split(QuerySeparators, StringSplitOptions.RemoveEmptyEntries));

    public static bool MatchesEveryWord(string? query, ReadOnlySpan<string?> fields)
        => Parse(query).Matches(fields);

    public sealed class Query
    {
        private readonly string[] _words;

        internal Query(string[] words) => _words = words;

        /// <summary>False when nothing was typed, so an empty search matches nothing.</summary>
        public bool IsEmpty => _words.Length == 0;

        public bool Matches(ReadOnlySpan<string?> fields)
        {
            if (IsEmpty) return false;

            foreach (var word in _words)
            {
                var found = false;
                foreach (var field in fields)
                {
                    if (!string.IsNullOrEmpty(field) && StartsAWord(field, word))
                    {
                        found = true;
                        break;
                    }
                }

                if (!found) return false;
            }

            return true;
        }
    }

    private static bool StartsAWord(string value, string word)
    {
        for (var i = 0; i + word.Length <= value.Length; i++)
        {
            var atWordStart = i == 0 || value[i - 1] is ' ' or '\t' or '-' or '\'' or '’';
            if (atWordStart && string.Compare(value, i, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0)
                return true;
        }

        return false;
    }
}
