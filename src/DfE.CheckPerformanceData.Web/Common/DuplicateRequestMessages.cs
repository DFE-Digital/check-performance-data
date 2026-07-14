namespace DfE.CheckPerformanceData.Web.Common;

public static class DuplicateRequestMessages
{
    public const string SelfSubmittedPupilSelection =
        "You already have a pending request for this pupil.";

    public const string OtherSubmittedPupilSelection =
        "Another user at your school has a pending request for this pupil.";

    public const string SelfSubmittedSummary =
        "You already have a pending request for this pupil. Select a different pupil.";

    public const string OtherSubmittedSummary =
        "Another user at your school has a pending request for this pupil. Select a different pupil.";

    public const string SelfSubmittedGuidance =
        "You can view your existing request in your requests list.";

    public const string OtherSubmittedGuidance =
        "Please coordinate with colleagues or contact support if this appears to be in error.";
}
