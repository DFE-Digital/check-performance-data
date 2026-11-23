using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.ResultsEnquiry;

/// <summary>
/// What a results enquiry is about for one student: a qualification and, for a held result, its
/// session. Two enquiries for one student with equal subjects are duplicates. Two results with
/// equal subjects are one result in two files. Values are trimmed and upper-cased.
/// </summary>
public sealed record EnquirySubject
{
    private EnquirySubject(string qan, string? session)
    {
        Qan = qan;
        Session = session;
    }

    public string Qan { get; }

    /// <summary>Null for a missing qualification, which has no held result.</summary>
    public string? Session { get; }

    public bool IsHeldResult => Session is not null;

    public static EnquirySubject ForResult(ResultRecord result) =>
        new(Normalise(result.Qan), Normalise(result.Session));

    public static EnquirySubject ForQualification(QualificationReference qualification) =>
        new(Normalise(qualification.Qan), null);

    /// <summary>The subject of a journey, or null while the journey has not chosen one.</summary>
    public static EnquirySubject? ForJourney(RequestState journey)
    {
        if (journey.SelectedWhatToChange == WhatToChange.MissingQualification)
            return journey.SelectedQualification is { } qualification ? ForQualification(qualification) : null;

        return journey.SelectedResult is { } result ? ForResult(result) : null;
    }

    private static string Normalise(string value) => value.Trim().ToUpperInvariant();
}
