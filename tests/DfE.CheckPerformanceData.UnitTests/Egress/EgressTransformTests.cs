using DfE.CheckPerformanceData.Application.Egress;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Egress;

// The four transforms the preprocessing steps are built from. Each is a pure function so the step
// list can report them one at a time and so a wrong LA split or reason code is caught here, not
// in a CSV LDS has already ingested.
public sealed class EgressTransformTests
{
    [Theory]
    [InlineData("8734603", "873", "4603")]
    [InlineData("873/4603", "873", "4603")]
    [InlineData(" 8604070 ", "860", "4070")]
    public void Laestab_splits_into_three_digit_LA_and_four_digit_establishment(string raw, string la, string estab)
    {
        Assert.True(LaestabSplitter.TrySplit(raw, out var actualLa, out var actualEstab));
        Assert.Equal(la, actualLa);
        Assert.Equal(estab, actualEstab);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("87346")]
    [InlineData("87346031")]
    [InlineData("ABCDEFG")]
    public void Anything_but_seven_digits_is_refused(string? raw)
    {
        Assert.False(LaestabSplitter.TrySplit(raw, out var la, out var estab));
        Assert.Equal(string.Empty, la);
        Assert.Equal(string.Empty, estab);
    }

    [Theory]
    [InlineData("pupil-died", "4")]
    [InlineData("not-on-roll", "6")]
    [InlineData("child-missing-education", "501")]
    [InlineData("permanently-left-england", "3")]
    [InlineData("year-group-change", "17")]
    [InlineData("dual-registered-moved", "332")]
    public void KS4_remove_reason_becomes_the_bare_LDS_code(string reason, string expected)
        => Assert.Equal(expected, CorrectionCodes.RemoveReasonCode(CheckingWindowType.KS4June, reason, null));

    // LDS_CYPMD_Data specification v2.4, "Remove Learner" K11 — the 16-19 codes. Not on roll is
    // split by the journey's not-on-roll-reason; "other" always has evidence in CYPMD → 329.
    [Theory]
    [InlineData("student-died", null, "4")]
    [InlineData("not-at-end-of-16-19-study", null, "325")]
    [InlineData("other", null, "329")]
    [InlineData("not-on-roll", "apprentice", "331")]
    [InlineData("not-on-roll", "external-candidate", "328")]
    [InlineData("not-on-roll", "international-student", "326")]
    [InlineData("not-on-roll", "other", "329")]
    public void Post16_remove_reason_becomes_the_spec_code(string reason, string? notOnRollReason, string expected)
        => Assert.Equal(expected, CorrectionCodes.RemoveReasonCode(CheckingWindowType.Post16, reason, notOnRollReason));

    [Theory]
    [InlineData(CheckingWindowType.KS4June, "other", null)]
    [InlineData(CheckingWindowType.KS4June, "completed-ks4-elsewhere", null)]
    [InlineData(CheckingWindowType.Post16, "not-on-roll", null)]          // sub-reason missing
    [InlineData(CheckingWindowType.Post16, "not-on-roll", "something")]
    [InlineData(CheckingWindowType.Post16, "pupil-died", null)]           // KS4 wording on a 16-19 window
    [InlineData(CheckingWindowType.KS4June, "", null)]
    [InlineData(CheckingWindowType.KS4June, null, null)]
    public void An_unmapped_reason_has_no_code_rather_than_a_guess(CheckingWindowType windowType, string? reason, string? notOnRollReason)
        => Assert.Null(CorrectionCodes.RemoveReasonCode(windowType, reason, notOnRollReason));

    [Theory]
    [InlineData("2007-06-01", "2007-06-01")]
    [InlineData("01/06/2007", "2007-06-01")]
    [InlineData("2007-06-01 00:00:00.0000000", "2007-06-01")]
    public void Dates_standardise_to_iso(string raw, string expected)
    {
        Assert.True(EgressDates.TryToIso(raw, out var iso));
        Assert.Equal(expected, iso);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("31/02/2007")]
    [InlineData("June 2007")]
    public void Unparseable_dates_are_refused(string? raw)
        => Assert.False(EgressDates.TryToIso(raw, out _));

    [Fact]
    public void Answers_flatten_to_strings_with_dates_as_iso()
    {
        var state = new RequestState
        {
            QuestionAnswers =
            {
                ["first-name"] = new QuestionAnswer { TextValue = " Annie " },
                ["date-of-birth"] = new QuestionAnswer { DateValue = new DateAnswer { Day = 7, Month = 9, Year = 2010 } },
                ["admission-date"] = new QuestionAnswer { DateValue = new DateAnswer() },
                ["country"] = new QuestionAnswer { CodeValue = "FR", TextValue = "France" },
                ["years"] = new QuestionAnswer { SelectedValues = ["2024-2025", "2025-2026"] },
                ["blank"] = new QuestionAnswer()
            }
        };

        var flat = EgressAnswers.Flatten(state);

        Assert.Equal("Annie", flat["first-name"]);
        Assert.Equal("2010-09-07", flat["date-of-birth"]);
        Assert.False(flat.ContainsKey("admission-date"));   // incomplete date = no answer
        Assert.Equal("FR", flat["country"]);                // the stable code wins over the label
        Assert.Equal("2024-2025|2025-2026", flat["years"]);
        Assert.False(flat.ContainsKey("blank"));
    }
}
