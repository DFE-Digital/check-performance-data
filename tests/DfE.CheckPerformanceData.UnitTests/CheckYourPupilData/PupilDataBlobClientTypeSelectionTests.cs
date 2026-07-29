using DfE.CheckPerformanceData.Application.CheckYourPupilData;
using DfE.CheckPerformanceData.Domain.Enums;
using DfE.CheckPerformanceData.Infrastructure.BlobStorage;

namespace DfE.CheckPerformanceData.Application.UnitTests.CheckYourPupilData;

// The per-school JSON blob holds a different record shape per window type. Guards the mapping
// from CheckingWindowType to the concrete deserialisation target without needing blob storage.
public class PupilDataBlobClientTypeSelectionTests
{
    [Theory]
    [InlineData(CheckingWindowType.KS4June, typeof(PupilRecord))]
    [InlineData(CheckingWindowType.KS4Autumn, typeof(PupilRecord))]
    [InlineData(CheckingWindowType.KS2, typeof(PupilRecord))]
    [InlineData(CheckingWindowType.Post16, typeof(Post16PupilRecord))]
    public void Selects_the_record_type_for_the_window_type(CheckingWindowType type, Type expected)
        => Assert.Equal(expected, PupilDataBlobClient.RecordTypeFor(type));

    [Fact]
    public void Deserialises_post16_json_as_post16_records()
    {
        const string json = """[ { "INCLUDED": true, "FORENAMES": "Alice", "ULN": "9900112233" } ]""";

        var pupils = PupilDataBlobClient.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(json), CheckingWindowType.Post16);

        var pupil = Assert.Single(pupils);
        Assert.IsType<Post16PupilRecord>(pupil);
        Assert.Equal("Alice", pupil.Firstname);
        Assert.Equal("9900112233", pupil.Identifier);
        Assert.True(pupil.IsIncluded);
    }

    [Fact]
    public void Deserialises_ks4_json_as_ks4_records()
    {
        const string json = """[ { "P_INCL": 401, "FORENAME": "Alice", "UPN": "A860407000001B" } ]""";

        var pupils = PupilDataBlobClient.Deserialize(
            System.Text.Encoding.UTF8.GetBytes(json), CheckingWindowType.KS4June);

        var pupil = Assert.Single(pupils);
        Assert.IsType<PupilRecord>(pupil);
        Assert.Equal("A860407000001B", pupil.Identifier);
        Assert.True(pupil.IsIncluded);
    }
}
