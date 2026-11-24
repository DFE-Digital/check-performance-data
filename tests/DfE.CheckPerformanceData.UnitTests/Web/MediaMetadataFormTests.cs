using System.ComponentModel.DataAnnotations;
using DfE.CheckPerformanceData.Application.Media;
using DfE.CheckPerformanceData.Web.Controllers.ViewModels.Media;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web;

public sealed class MediaMetadataFormTests
{
    private static List<ValidationResult> Validate(MediaMetadataForm form)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(form, new ValidationContext(form), results, validateAllProperties: true);
        return results;
    }

    [Fact]
    public void AnEmptyForm_IsValid()
    {
        Assert.Empty(Validate(new MediaMetadataForm()));
    }

    // The limits are the database column sizes, so a valid form always saves.
    [Theory]
    [InlineData(nameof(MediaMetadataForm.AltText), 1000, "Alt text must be 1000 characters or fewer")]
    [InlineData(nameof(MediaMetadataForm.Caption), 2000, "Caption must be 2000 characters or fewer")]
    [InlineData(nameof(MediaMetadataForm.Credit), 500, "Credit must be 500 characters or fewer")]
    public void TextLongerThanItsColumn_IsRefused_WithAGovUkMessage(string field, int limit, string message)
    {
        var form = new MediaMetadataForm();
        var property = typeof(MediaMetadataForm).GetProperty(field)!;
        property.SetValue(form, new string('a', limit));
        Assert.Empty(Validate(form));

        property.SetValue(form, new string('a', limit + 1));
        var error = Assert.Single(Validate(form));
        Assert.Equal(message, error.ErrorMessage);
        Assert.Equal([field], error.MemberNames);
    }

    [Fact]
    public void TooManyTags_OrATagThatIsTooLong_IsRefused()
    {
        var many = new MediaMetadataForm { Tags = string.Join(",", Enumerable.Range(1, MediaMetadataForm.MaxTags + 1).Select(i => $"tag{i}")) };
        var error = Assert.Single(Validate(many));
        Assert.Equal("Enter 20 tags or fewer", error.ErrorMessage);
        Assert.Equal([nameof(MediaMetadataForm.Tags)], error.MemberNames);

        var longTag = new MediaMetadataForm { Tags = "ok, " + new string('a', MediaMetadataForm.MaxTagLength + 1) };
        Assert.Equal("Each tag must be 100 characters or fewer", Assert.Single(Validate(longTag)).ErrorMessage);
    }

    [Fact]
    public void ToMetadata_SplitsTheTagsOnCommas_AndDropsBlankOnes()
    {
        var metadata = new MediaMetadataForm { AltText = "Bar chart", Caption = "Results", Credit = "DfE", Tags = " schools, KS2 ,, " }.ToMetadata();

        Assert.Equal("Bar chart", metadata.DefaultAltText);
        Assert.Equal("Results", metadata.Caption);
        Assert.Equal("DfE", metadata.Credit);
        Assert.Equal(["schools", "KS2"], metadata.Tags);
    }

    [Fact]
    public void From_FillsTheFormFromTheImage_WithTheTagsOnOneLine()
    {
        var id = Guid.NewGuid();
        var version = new MediaAssetVersionDto(id, 1, "k", "image/png", 1, 1, 1, "sha", false, DateTime.UtcNow, null);
        var asset = new MediaAssetDto(id, "a.png", "a", "Alt", "Cap", "Cred", ["x", "y z"], 1, version, DateTime.UtcNow, null, DateTime.UtcNow, null);

        var form = MediaMetadataForm.From(asset);

        Assert.Equal("Alt", form.AltText);
        Assert.Equal("Cap", form.Caption);
        Assert.Equal("Cred", form.Credit);
        Assert.Equal("x, y z", form.Tags);
    }
}
