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
        if (pupil.Identifier.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            pupil.Cypmd_Id.StartsWith(query, StringComparison.OrdinalIgnoreCase) ||
            pupil.Surname.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            pupil.Firstname.Contains(query, StringComparison.OrdinalIgnoreCase))
            return true;

        // Date-of-birth search is 16-19 only for now. Matched against the DISPLAYED date, because
        // that is the form the user reads off the screen and types back — the raw supplier value is
        // an ISO timestamp nobody would enter.
        return windowType == CheckingWindowType.Post16 && MatchesDateOfBirth(pupil, query);
    }

    private static bool MatchesDateOfBirth(IPupilRecord pupil, string query)
    {
        var dob = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth);
        return dob.Length > 0 && dob.StartsWith(query, StringComparison.OrdinalIgnoreCase);
    }
}
