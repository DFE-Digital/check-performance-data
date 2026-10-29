namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// #712: the single owner of the two results-enquiry grade question ids. The ids are a
/// serialization contract between the journey flow files (IncorrectGrade_Post16.json,
/// MissingQualification_Post16.json) and the code that reads the saved answers — the flow tests
/// pin them against the JSON. The Web constants delegate here so one owner holds the contract.
/// </summary>
public static class EnquiryQuestionIds
{
    public const string RevisedGrade = "q-revised-grade";
    public const string MissingGrade = "q-missing-grade";
}
