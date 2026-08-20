using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class MergeRecordDisplaysTests
{
    private static PupilDto Pupil(string? dob = "02/02/2010", string cypmdId = "CYPMD456") => new()
    {
        Id = Guid.NewGuid(),
        Firstname = "John",
        Surname = "Doe",
        Sex = "M",
        DateOfBirth = dob!,
        Age = 16,
        Cypmd_Id = cypmdId,
        Identifier = "UPN002"
    };

    [Fact]
    public void First_ParseableDob_RendersNameCommaDate()
    {
        var result = MergeRecordDisplays.First(Pupil());

        Assert.Equal("John Doe, 2 February 2010", result);
    }

    [Fact]
    public void Second_ParseableDob_RendersNameDateInParenthesisedId()
    {
        var result = MergeRecordDisplays.Second(Pupil());

        Assert.Equal("John Doe 2 February 2010 (CYPMD456)", result);
    }

    [Fact]
    public void First_UnparseableDob_EchoesRawValue()
    {
        var result = MergeRecordDisplays.First(Pupil(dob: "unknown"));

        Assert.Equal("John Doe, unknown", result);
    }

    [Fact]
    public void Second_UnparseableDob_EchoesRawValue()
    {
        var result = MergeRecordDisplays.Second(Pupil(dob: "unknown"));

        Assert.Equal("John Doe unknown (CYPMD456)", result);
    }

    [Fact]
    public void Second_EmptyDob_OmitsDobSegmentWithoutArtifacts()
    {
        var result = MergeRecordDisplays.Second(Pupil(dob: ""));

        Assert.Equal("John Doe (CYPMD456)", result);
    }

    [Fact]
    public void Second_EmptyCypmdId_StillRendersNameAndDob()
    {
        var result = MergeRecordDisplays.Second(Pupil(cypmdId: ""));

        Assert.Equal("John Doe 2 February 2010 ()", result);
    }

    [Fact]
    public void Second_SingleDigitDayAndMonth_FormatsWithoutLeadingZeros()
    {
        var result = MergeRecordDisplays.Second(Pupil(dob: "05/06/2010"));

        Assert.Equal("John Doe 5 June 2010 (CYPMD456)", result);
    }
}