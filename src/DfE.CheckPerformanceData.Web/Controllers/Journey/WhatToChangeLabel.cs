using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The words for each change type. Kept word-for-word in step with the radios on What to change,
/// because the summary row, the submitted-request view and the evidence page caption (#625) all
/// echo the answer the school gave there.
/// </summary>
public static class WhatToChangeLabel
{
    public static string For(WhatToChange change, LearnerNoun noun) => change switch
    {
        WhatToChange.Remove => $"Remove a {noun.Singular} from data",
        WhatToChange.Include => $"Include a {noun.Singular} in data",
        WhatToChange.Merge => $"Merge duplicate {noun.Singular} records",
        WhatToChange.Add => $"Add a {noun.Singular} to data",
        _ => change.ToString()
    };
}
