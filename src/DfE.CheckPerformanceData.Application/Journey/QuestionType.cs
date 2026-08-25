namespace DfE.CheckPerformanceData.Application.Journey;

public enum QuestionType
{
    Radio,
    FreeText,
    Date,
    FileUpload,
    TextArea,
    Autocomplete,
    // AB#296648: a grade picker whose options come from the AODC reference data for the
    // selected result's QAN. Appended last so existing flow JSON values are unmoved.
    GradeSelect
}
