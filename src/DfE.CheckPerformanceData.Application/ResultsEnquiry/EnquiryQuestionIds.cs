namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// #712: the single owner of the two results-enquiry grade question ids. The ids are a
/// serialization contract between the journey flow files (each IncorrectGrade and
/// MissingQualification file) and the code that reads the saved answers — the flow tests
/// pin them against the JSON. The Web constants delegate here so one owner holds the contract.
/// </summary>
public static class EnquiryQuestionIds
{
    public const string RevisedGrade = "q-revised-grade";
    public const string MissingGrade = "q-missing-grade";
}
