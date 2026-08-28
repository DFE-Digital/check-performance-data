using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class WhatToChangeViewModel
{
    public Guid WindowId { get; set; }
    public WhatToChange? SelectedWhatToChange { get; set; }

    /// <summary>AB#297310: gates whether the view offers the Add-a-pupil radio.</summary>
    public CheckingWindowType? CheckingWindowType { get; set; }
}
