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
            pupil.Surname.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ||
            pupil.Firstname.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            return true;

        // Date-of-birth search is 16-19 only for now. Matched against the DISPLAYED date, because
        // that is the form the user reads off the screen and types back — the raw supplier value is
        // an ISO timestamp nobody would enter.
        return windowType == CheckingWindowType.Post16 && MatchesDateOfBirth(pupil, trimmed);
    }

    /// <summary>
    /// Shared split-query matching used by both <see cref="Matches"/> (autocomplete) and
    /// <see cref="CheckYourPupilDataService.DuplicateCheckAsync"/> (Add Pupil).
    ///
    /// When the query contains a space the first token is matched against the first name and the
    /// rest against the surname, both via case-insensitive contains.  A single token (no space)
    /// falls back to matching either name part, preserving existing single-term behaviour.
    /// </summary>
    public static bool NameMatchesSplitQuery(string? firstname, string? surname, string query)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return false;

        var spaceIndex = trimmed.IndexOf(' ');
        if (spaceIndex < 0)
        {
            return (firstname?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (surname?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        var firstNamePart = trimmed[..spaceIndex];
        var surnamePart = trimmed[(spaceIndex + 1)..].TrimStart();

        if (firstNamePart.Length == 0 || surnamePart.Length == 0)
            return (firstname?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false) ||
                   (surname?.Contains(trimmed, StringComparison.OrdinalIgnoreCase) ?? false);

        return (firstname?.Contains(firstNamePart, StringComparison.OrdinalIgnoreCase) ?? false) &&
               (surname?.Contains(surnamePart, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool MatchesDateOfBirth(IPupilRecord pupil, string query)
    {
        var dob = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth);
        return dob.Length > 0 && dob.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }
}
