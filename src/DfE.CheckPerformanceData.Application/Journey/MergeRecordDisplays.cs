using System.Globalization;
using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// Single source of truth for the two record displays shown when merging duplicate
/// pupils. Both the in-journey summary and the submitted-request view delegate here so
/// the format can never drift between surfaces.
/// </summary>
public static class MergeRecordDisplays
{
    public static string First(PupilDto pupil)
    {
        var dob = FormatDob(pupil.DateOfBirth);
        return $"{pupil.Firstname} {pupil.Surname}, {dob}".Trim();
    }

    public static string Second(PupilDto pupil)
    {
        var dob = FormatDob(pupil.DateOfBirth);
        var name = $"{pupil.Firstname} {pupil.Surname}".Trim();
        var display = dob.Length > 0 ? $"{name} {dob} ({pupil.Cypmd_Id})" : $"{name} ({pupil.Cypmd_Id})";
        return display.Trim();
    }

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