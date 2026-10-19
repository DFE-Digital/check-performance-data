using DfE.CheckPerformanceData.Application.Journey;
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
/// tag so two students with similar names can be told apart. #623 gave KS4 the same: date-of-birth
/// search, and a suggestion that shows CYPMD ID, UPN, date of birth and inclusion status. Matching
/// no longer depends on the window type; the label still does (ULN on 16-19, UPN elsewhere).
/// </summary>
public static class PupilSuggestionFormat
{
    public static string Label(IPupilRecord pupil, CheckingWindowType windowType, PupilSearchField searchField = PupilSearchField.All)
    {
        var dob = PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth);

        if (windowType != CheckingWindowType.Post16)
        {
            // #623: the row shows every identifier the hint says a school can search by, in the
            // format UCD gave. It carries the CYPMD ID, so a page narrowed to the ID (AB#304118: the
            // only part that tells two duplicate records apart) needs no label of its own.
            var status = pupil.IsIncluded ? "INCLUDED" : "NOT INCLUDED";
            return $"{pupil.Surname}, {pupil.Firstname}, " +
                   $"(CYPMD ID: {pupil.Cypmd_Id}, UPN: {pupil.Identifier}, DOB: {dob}, Status: {status})";
        }

        // AB#297004 specifies "UPN" here, but 16-19 students have a ULN and no UPN — Identifier is
        // the ULN for Post16. FLAGGED to the BA: the label says ULN because that is what the value is.
        var inclusion = pupil.IsIncluded ? "INCLUDED" : "NOT INCLUDED";
        return $"{pupil.Firstname}, {pupil.Surname}, " +
               $"(CYPMD ID:{pupil.Cypmd_Id}, ULN:{pupil.Identifier}, DOB:{dob}, {inclusion})";
    }

    public static bool Matches(IPupilRecord pupil, string query, CheckingWindowType windowType, PupilSearchField searchField = PupilSearchField.All)
        => Matcher(query, searchField)(pupil);

    /// <summary>
    /// The <see cref="Matches"/> rule with the query parsed once, for a caller that tests every
    /// pupil in a school against the same query.
    /// </summary>
    public static Func<IPupilRecord, bool> Matcher(string query, PupilSearchField searchField = PupilSearchField.All)
    {
        var trimmed = query.Trim();
        if (trimmed.Length == 0) return _ => false;

        // AB#304118: a page can restrict the search to the CYPMD ID alone — the KS4 merge journey's
        // second-record page asks for one by name. Returned before every other rule, because a page
        // that promises an ID search must not also answer to a name, a UPN or a date of birth. It
        // bypasses PupilSearchTerms on purpose: an ID is one word, so splitting into words adds
        // nothing, and a typed space or comma must not let half an ID match.
        if (searchField == PupilSearchField.CypmdId)
            return pupil => pupil.Cypmd_Id.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase);

        // Every word must start a word in a name or an identifier, in any order (PupilSearchTerms).
        // The date of birth is the DISPLAYED date, because that is the form the user reads off the
        // screen and types back — the raw supplier value is an ISO timestamp nobody would enter.
        // Every key stage since #623, whose hint names the date of birth.
        var terms = PupilSearchTerms.Parse(trimmed);
        return pupil => terms.Matches(
        [
            pupil.Firstname,
            pupil.Surname,
            pupil.Identifier,
            pupil.Cypmd_Id,
            PupilDateFormatter.ToDisplayDate(pupil.DateOfBirth)
        ]);
    }

    /// <summary>
    /// Matcher for the Add-pupil duplicate check (<see cref="CheckYourPupilDataService.DuplicateCheckAsync"/>).
    ///
    /// Unlike the autocomplete <see cref="Matches"/>, which matches the start of a word only
    /// (<see cref="PupilSearchTerms"/>), this matches on substrings (Contains) so a partial or variant spelling
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
}
