using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Single source of truth for the two record displays shown when merging duplicate
/// pupils. Both the in-journey summary and the submitted-request view delegate here so
/// the format can never drift between surfaces. Both records read
/// "Name (CYPMD ID: 1234567, UPN: 9900000081, date of birth: 27 July 2020)" (#800).
/// Two duplicate records often share a name and a date of birth, so the identifiers tell them apart.
/// A 16-19 student has a ULN and no UPN, so the label is "ULN" on a 16-19 window, as in
/// <see cref="PupilSuggestionFormat"/>. A detail with no value is left out.
/// </summary>
public static class MergeRecordDisplays
{
    public static string First(PupilDto pupil, CheckingWindowType? windowType) => Format(pupil, windowType);

    public static string Second(PupilDto pupil, CheckingWindowType? windowType) => Format(pupil, windowType);

    private static string Format(PupilDto pupil, CheckingWindowType? windowType)
    {
        var name = $"{pupil.Firstname} {pupil.Surname}".Trim();
        string[] details =
        [
            Detail("CYPMD ID", pupil.Cypmd_Id),
            Detail(IdentifierLabel(windowType), pupil.Identifier),
            Detail("date of birth", FormatDob(pupil.DateOfBirth))
        ];
        var shown = string.Join(", ", details.Where(d => d.Length > 0));
        return shown.Length > 0 ? $"{name} ({shown})".Trim() : name;
    }

    // A journey cannot start without a window (IsSessionReady). With no window the label is
    // "UPN", the same default as RequestState.LearnerNoun, which falls back to "pupil".
    private static string IdentifierLabel(CheckingWindowType? windowType) =>
        windowType == CheckingWindowType.Post16 ? "ULN" : "UPN";

    private static string Detail(string label, string? value) =>
        string.IsNullOrWhiteSpace(value) ? string.Empty : $"{label}: {value}";

    private static string FormatDob(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        return DateTime.TryParseExact(raw, "dd/MM/yyyy",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out var d)
            ? d.ToString("d MMMM yyyy", CultureInfo.InvariantCulture)
            : raw;
    }
}