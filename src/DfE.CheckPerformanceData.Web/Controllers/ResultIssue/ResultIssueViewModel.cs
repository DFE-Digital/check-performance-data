using DfE.CheckPerformanceData.Application.ResultsEnquiry;
using DfE.CheckPerformanceData.Application.WindowManagement;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Web.Common;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace DfE.CheckPerformanceData.Web.Controllers;

public sealed class ResultIssueViewModel
{
    /// <summary>AB#296648: the "report an incorrect grade" enquiry.</summary>
    public const string IncorrectGrade = "incorrect-grade";

    /// <summary>AB#297848: the missing-qualification enquiry.</summary>
    public const string MissingQualification = "missing-qualification";

    /// <summary>AB#298704: the "result does not belong to student" enquiry.</summary>
    public const string ResultDoesNotBelong = "result-does-not-belong";

    public Guid WindowId { get; set; }

    /// <summary>The posted radio value. Null on first render and on a validation redisplay.</summary>
    public string? IssueType { get; set; }

    [BindNever]
    public CheckingWindowType WindowType { get; set; }

    public LearnerNoun LearnerNoun => LearnerNoun.For(WindowType);

    public LateResultsGuidanceBlocks LateResultsGuidance => LateResultsGuidanceBlocks.For(WindowType);

    public string ResultDoesNotBelongLabel =>
        EnquiryTypeLabels.For(Application.CheckYourPupilData.WhatToChange.ResultDoesNotBelong, LearnerNoun);
}
