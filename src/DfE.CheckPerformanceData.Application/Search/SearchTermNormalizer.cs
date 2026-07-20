namespace DfE.CheckPerformanceData.Application.Search;

// Normalises a user-typed search term before it is handed to Postgres'
// websearch_to_tsquery. websearch_to_tsquery treats whitespace-separated words as AND by
// default (Google-style), so "merge booga" requires BOTH words in a row's tsvector — one
// missing word wipes out the whole result set. Users typing multiple words almost always
// want "either of these", so this helper transforms plain whitespace-separated queries
// into an OR-joined query. Rows matching more terms still outrank rows matching one via
// ts_rank, so the "both" hits still float to the top.
//
// Queries that ALREADY use websearch syntax (an explicit OR, a quoted "phrase", a leading
// -negation) are passed through untouched — the user has signalled intent and we must
// respect it.
public static class SearchTermNormalizer
{
    public static string OrJoinWhitespace(string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return query ?? string.Empty;
        var trimmed = query.Trim();
        if (ContainsWebSearchOperator(trimmed)) return trimmed;

        var tokens = trimmed.Split(
            [' ', '\t', '\n', '\r'],
            StringSplitOptions.RemoveEmptyEntries);
        return tokens.Length <= 1 ? trimmed : string.Join(" OR ", tokens);
    }

    private static bool ContainsWebSearchOperator(string q)
    {
        if (q.Contains('"')) return true;
        foreach (var t in q.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (t == "OR") return true;
            if (t.Length > 0 && t[0] == '-') return true;
        }
        return false;
    }
}
