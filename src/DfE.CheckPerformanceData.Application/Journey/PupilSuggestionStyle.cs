namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>The layout of the text of a pupil search suggestion row.</summary>
public enum PupilSuggestionStyle
{
    /// <summary>The row of the window type: surname first on KS2 and KS4, the 16-19 row on 16-19.</summary>
    Standard = 0,

    /// <summary>The 16-19 row on each window type, with the UPN where the window is not 16-19.</summary>
    ResultsEnquiry = 1
}
