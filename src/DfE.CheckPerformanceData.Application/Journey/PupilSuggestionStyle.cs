namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// The layout of the text of a pupil search suggestion row. <see cref="Standard"/> is the zero
/// value, so a caller that names no style gets the row that its window type has.
/// </summary>
public enum PupilSuggestionStyle
{
    /// <summary>The row of the window type: the #623 row on KS2 and KS4, the 16-19 row on 16-19.</summary>
    Standard = 0,

    /// <summary>The results enquiry row (#902): the 16-19 row on each window type, with the UPN where the window is not 16-19.</summary>
    ResultsEnquiry = 1
}
