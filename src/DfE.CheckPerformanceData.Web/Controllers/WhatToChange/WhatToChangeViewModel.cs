using DfE.CheckPerformanceData.Application.CheckYourPupilData;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WhatToChangeViewModel
{
    public Guid WindowId { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }
}
