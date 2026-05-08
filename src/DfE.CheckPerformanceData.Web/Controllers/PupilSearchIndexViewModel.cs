using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class PupilSearchIndexViewModel
{
    public Guid WindowId { get; set; }
    public string? SelectedPupilId { get; set; }
    public string? SelectedPupilLabel { get; set; }
    public WhatToChange WhatToChange { get; set; }

    public string ErrorMessage => WhatToChange switch
    {
        WhatToChange.Include => "Enter the name of the pupil to be included",
        WhatToChange.Merge   => "Enter the name of the pupil to be merged",
        _                    => "Enter the name of the pupil to be removed"
    };
}
