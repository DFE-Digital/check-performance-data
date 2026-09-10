using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

/// <summary>
/// How a pupil is matched against a search query and rendered as an autocomplete suggestion.
///
/// Extracted from <c>CheckYourPupilDataRepository.SearchPupilsAsync</c> so the format — which is
/// user-visible text pinned by tests — can be exercised without blob storage or a database.
///
/// 16-19 (AB#297004) needs more than KS4: a school identifies a student by ULN, CYPMD ID or date of
/// birth as readily as by name, and the suggestion has to show those identifiers plus the inclusion
/// tag so two students with similar names can be told apart. KS4 behaviour is deliberately left
/// exactly as it was — those journeys are live and are not in this ticket's scope.
/// </summary>
public static class PupilSuggestionFormat
{
    public static string Label(IPupilRecord pupil, CheckingWindowType windowType)
    {
        var dob = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth);

        if (windowType != CheckingWindowType.Post16)
            return $"{pupil.Surname}, {pupil.Firstname}, {dob}";

        // AB#297004 specifies "UPN" here, but 16-19 students have a ULN and no UPN — Identifier is
        // the ULN for Post16. FLAGGED to the BA: the label says ULN because that is what the value is.
        var inclusion = pupil.IsIncluded ? "INCLUDED" : "NOT INCLUDED";
        return $"{pupil.Firstname}, {pupil.Surname}, " +
               $"(CYPMD ID:{pupil.Cypmd_Id}, ULN:{pupil.Identifier}, DOB:{dob}, {inclusion})";
    }

    public static bool Matches(IPupilRecord pupil, string query, CheckingWindowType windowType)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return false;

        if (trimmed.Contains(' '))
        {
            if (NameMatchesSplitQuery(pupil.Firstname, pupil.Surname, trimmed))
                return true;
        }

        if (pupil.Identifier.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
            pupil.Cypmd_Id.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
            pupil.Surname.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ||
            pupil.Firstname.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            return true;

        // Date-of-birth search is 16-19 only for now. Matched against the DISPLAYED date, because
        // that is the form the user reads off the screen and types back — the raw supplier value is
        // an ISO timestamp nobody would enter.
        return windowType == CheckingWindowType.Post16 && MatchesDateOfBirth(pupil, trimmed);
    }

    /// <summary>
    /// Autocomplete split-query matching used by <see cref="Matches"/> only (the pupil-search
    /// dropdown). When the query contains a space the first token is matched against the first
    /// name and the rest against the surname, both via case-insensitive startsWith — a deliberately
    /// prefix-only rule. A single token (no space) falls back to matching either name part.
    ///
    /// NOT shared with the Add-pupil duplicate check, which needs substring (Contains) matching
    /// and a dual split — see <see cref="NameMatchesForDuplicateCheck"/>.
    /// </summary>
    public static bool NameMatchesSplitQuery(string? firstname, string? surname, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return false;

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (firstname?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (surname?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        var firstNamePart = trimmed[..spaceIndex];
        var surnamePart = trimmed[(spaceIndex + 1)..].TrimStart();

        if (firstNamePart.Length == 0 || surnamePart.Length == 0)
            return (firstname?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (surname?.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase) ?? false);

        return (firstname?.StartsWith(firstNamePart, StringComparison.OrdinalIgnoreCase) ?? false) &&
               (surname?.StartsWith(surnamePart, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    /// <summary>
    /// Matcher for the Add-pupil duplicate check (<see cref="CheckYourPupilDataService.DuplicateCheckAsync"/>).
    ///
    /// Unlike the autocomplete <see cref="Matches"/>, which keeps its deliberate prefix-only
    /// StartsWith behaviour, this matches on substrings (Contains) so a partial or variant spelling
    /// still surfaces a duplicate, and it tries BOTH a first-space and a last-space split of the
    /// typed full name so a multi-word first or last name is caught whichever way the clerk grouped
    /// it. The caller also requires the date of birth to match, so a slightly fuzzy name match
    /// cannot create a false positive.
    /// </summary>
    public static bool NameMatchesForDuplicateCheck(string? firstname, string? surname, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return false;

        var firstSpace = trimmed.IndexOf(' ');
        if (firstSpace < 0)
            return ContainsEither(firstname, surname, trimmed);

        // Try both groupings; when they coincide (exactly one space) the second is a harmless repeat.
        var lastSpace = trimmed.LastIndexOf(' ');
        return GroupingMatches(firstname, surname, trimmed, firstSpace)
            || GroupingMatches(firstname, surname, trimmed, lastSpace);
    }

    private static bool ContainsEither(string? first, string? surname, string part)
        => (first is not null && first.Contains(part, StringComparison.OrdinalIgnoreCase)) ||
           (surname is not null && surname.Contains(part, StringComparison.OrdinalIgnoreCase));

    private static bool GroupingMatches(string? firstname, string? surname, string trimmed, int spaceIndex)
    {
        var firstNamePart = trimmed[..spaceIndex].Trim();
        var surnamePart = trimmed[(spaceIndex + 1)..].Trim();

        if (firstNamePart.Length == 0 || surnamePart.Length == 0)
            return ContainsEither(firstname, surname, trimmed);

        return firstname is not null
               && firstname.Contains(firstNamePart, StringComparison.OrdinalIgnoreCase)
               && surname is not null
               && surname.Contains(surnamePart, StringComparison.OrdinalIgnoreCase);
    }

    private static bool MatchesDateOfBirth(IPupilRecord pupil, string query)
    {
        var dob = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth);
        return dob.Length > 0 && dob.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }
}
