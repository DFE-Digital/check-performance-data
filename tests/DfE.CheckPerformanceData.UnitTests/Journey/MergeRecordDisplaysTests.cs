using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Application.Journey;
using DfE.CheckPerformanceData.Domain.Enums;

namespace DfE.CheckPerformanceData.Application.UnitTests.Journey;

public class MergeRecordDisplaysTests
{
    private static PupilDto Pupil(
        string? dob = "02/02/2010", string cypmdId = "CYPMD456", string identifier = "UPN002") => new()
    {
        Id = Guid.NewGuid(),
        Firstname = "John",
        Surname = "Doe",
        Sex = "M",
        DateOfBirth = dob!,
        Age = 16,
        Cypmd_Id = cypmdId,
        Identifier = identifier
    };

    // #800: name, then the CYPMD ID, the UPN and the date of birth in brackets.
    [Fact]
    public void First_RendersNameThenCypmdIdUpnAndDateOfBirth()
    {
        var result = MergeRecordDisplays.First(Pupil(), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002, date of birth: 2 February 2010)", result);
    }

    [Fact]
    public void Second_RendersTheSameFormatAsFirst()
    {
        var result = MergeRecordDisplays.Second(Pupil(), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002, date of birth: 2 February 2010)", result);
    }

    // A 16-19 student has a ULN and no UPN. The value is the same field; the label follows the window.
    [Theory]
    [InlineData(CheckingWindowType.KS4June, "UPN")]
    [InlineData(CheckingWindowType.KS4Autumn, "UPN")]
    [InlineData(CheckingWindowType.Post16, "ULN")]
    [InlineData(null, "UPN")]
    public void IdentifierLabel_FollowsTheWindowType(CheckingWindowType? windowType, string label)
    {
        var result = MergeRecordDisplays.Second(Pupil(), windowType);

        Assert.Equal($"John Doe (CYPMD ID: CYPMD456, {label}: UPN002, date of birth: 2 February 2010)", result);
    }

    [Fact]
    public void UnparseableDob_ShowsTheStoredValue()
    {
        var result = MergeRecordDisplays.First(Pupil(dob: "unknown"), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002, date of birth: unknown)", result);
    }

    [Fact]
    public void SingleDigitDayAndMonth_HaveNoLeadingZeros()
    {
        var result = MergeRecordDisplays.Second(Pupil(dob: "05/06/2010"), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002, date of birth: 5 June 2010)", result);
    }

    [Fact]
    public void EmptyDob_LeavesTheDateOfBirthOut()
    {
        var result = MergeRecordDisplays.Second(Pupil(dob: ""), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002)", result);
    }

    [Fact]
    public void EmptyIdentifier_LeavesTheUpnOut()
    {
        var result = MergeRecordDisplays.Second(Pupil(identifier: ""), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, date of birth: 2 February 2010)", result);
    }

    [Fact]
    public void EmptyCypmdId_LeavesTheCypmdIdOut()
    {
        var result = MergeRecordDisplays.Second(Pupil(cypmdId: ""), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (UPN: UPN002, date of birth: 2 February 2010)", result);
    }

    [Fact]
    public void NoDetails_RendersTheNameAlone()
    {
        var result = MergeRecordDisplays.Second(
            Pupil(dob: "", cypmdId: "", identifier: ""), CheckingWindowType.KS4June);

        Assert.Equal("John Doe", result);
    }

    // A value that is only spaces is no value.
    [Fact]
    public void WhitespaceOnlyDetail_IsLeftOut()
    {
        var result = MergeRecordDisplays.Second(Pupil(identifier: "   "), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, date of birth: 2 February 2010)", result);
    }

    // A supplier value can have a space at either end. The row does not show it.
    [Fact]
    public void DetailWithSpacesAround_IsTrimmed()
    {
        var result = MergeRecordDisplays.Second(
            Pupil(cypmdId: " CYPMD456 ", identifier: " UPN002 "), CheckingWindowType.KS4June);

        Assert.Equal("John Doe (CYPMD ID: CYPMD456, UPN: UPN002, date of birth: 2 February 2010)", result);
    }
}