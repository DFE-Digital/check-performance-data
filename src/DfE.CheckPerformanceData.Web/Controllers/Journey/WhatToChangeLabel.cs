using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.WindowManagement;

namespace DfE.CheckPerformanceData.Web.Controllers.Journey;

/// <summary>
/// The words for each change type. The radios on What to change render them, and the summary row,
/// the submitted-request view and the evidence page caption (#625) echo the same answer.
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
