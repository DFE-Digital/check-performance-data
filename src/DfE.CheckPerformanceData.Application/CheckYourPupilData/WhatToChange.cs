namespace DfE.CheckPerformanceData.Application.CheckYourPupilData;

public enum WhatToChange
{
    Merge,
    Include,
    Remove,
    // The Add journey (AB#297310) drives this member; see Add_*.json flow configs.
    Add,
    // AB#296648: the 16-19 "report an incorrect grade" results-enquiry journey. Belongs to the
    // ResultsEnquiry checking exercise rather than pupil-data checking — see
    // WhatToChangeCheckingExerciseMap. Appended last so existing values are unmoved.
    IncorrectGrade,
    // AB#297848: the 16-19 "missing qualification" results-enquiry journey. Belongs to the
    // ResultsEnquiry checking exercise — see WhatToChangeCheckingExerciseMap. Exactly 20 chars,
    // the AmendmentType column's HasMaxLength — pinned by EnumContractTests. Appended last.
    MissingQualification
}
