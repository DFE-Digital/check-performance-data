using DfE.CheckPerformanceData.Application.Media.Reports;

namespace DfE.CheckPerformanceData.Application.UnitTests.Media.Reports;

// The rules that decide whether alt text describes its image: missing, a generic word on its own,
// a file name, too short, or the same as the caption. The accessibility report lists anything
// these rules flag, so each rule and the order they are tried in are pinned here.
public sealed class WeakAltTextTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BlankText_IsMissing(string? alt)
    {
        Assert.Equal(AltTextProblem.Missing, WeakAltText.Check(alt, "photo.png", null));
    }

    [Theory]
    [InlineData("image")]
    [InlineData("Image")]
    [InlineData("PHOTO")]
    [InlineData("picture")]
    [InlineData("graphic")]
    [InlineData("Logo.")]
    [InlineData("screenshot")]
    [InlineData("  photograph  ")]
    [InlineData("an image")]
    [InlineData("The logo")]
    public void AGenericWordOnItsOwn_IsGeneric(string alt)
    {
        Assert.Equal(AltTextProblem.Generic, WeakAltText.Check(alt, "school-front.png", null));
    }

    [Theory]
    [InlineData("Logo of the Department for Education")]
    [InlineData("Photo of pupils in a classroom")]
    [InlineData("A screenshot of the results table")]
    public void AGenericWordInsideADescription_IsFine(string alt)
    {
        Assert.Equal(AltTextProblem.None, WeakAltText.Check(alt, "school-front.png", null));
    }

    [Theory]
    [InlineData("IMG_1234.JPG")]
    [InlineData("chart.png")]
    [InlineData("diagram.svg")]
    [InlineData("hero.webp")]
    [InlineData("banner.jpeg")]
    [InlineData("loading.gif")]
    public void TextWithAnImageExtension_IsAFileName(string alt)
    {
        Assert.Equal(AltTextProblem.FileName, WeakAltText.Check(alt, "something-else.png", null));
    }

    [Theory]
    [InlineData("school-front.png")]
    [InlineData("school-front")]
    [InlineData("SCHOOL-FRONT")]
    public void TheAssetsOwnFileName_WithOrWithoutItsExtension_IsAFileName(string alt)
    {
        Assert.Equal(AltTextProblem.FileName, WeakAltText.Check(alt, "school-front.png", null));
    }

    [Fact]
    public void AFileNameThatIsAlsoAGenericWord_IsReportedAsAFileName()
    {
        Assert.Equal(AltTextProblem.FileName, WeakAltText.Check("photo", "photo.png", null));
    }

    [Theory]
    [InlineData("Map")]
    [InlineData("  a b ")]
    [InlineData("Dog")]
    public void UnderFiveCharactersAfterTrimming_IsTooShort(string alt)
    {
        Assert.Equal(AltTextProblem.TooShort, WeakAltText.Check(alt, "x.png", null));
    }

    [Fact]
    public void FiveCharacters_IsLongEnough()
    {
        Assert.Equal(AltTextProblem.None, WeakAltText.Check("Ducks", "x.png", null));
    }

    [Theory]
    [InlineData("Pupils in the library", "Pupils in the library")]
    [InlineData("pupils in  the library", "Pupils in the library.")]
    public void TheSameTextAsTheCaption_IgnoringCaseSpacesAndEndPunctuation_IsReported(string alt, string caption)
    {
        Assert.Equal(AltTextProblem.SameAsCaption, WeakAltText.Check(alt, "x.png", caption));
    }

    [Fact]
    public void ADifferentCaption_IsFine()
    {
        Assert.Equal(AltTextProblem.None,
            WeakAltText.Check("Pupils reading in the school library", "x.png", "Our new library opened in May"));
    }

    [Fact]
    public void GoodText_WithNoCaption_IsFine()
    {
        Assert.Equal(AltTextProblem.None, WeakAltText.Check("A bar chart of results by year", "chart.png", "  "));
    }

    [Theory]
    [InlineData(AltTextProblem.Missing)]
    [InlineData(AltTextProblem.Generic)]
    [InlineData(AltTextProblem.FileName)]
    [InlineData(AltTextProblem.TooShort)]
    [InlineData(AltTextProblem.SameAsCaption)]
    public void EveryProblem_HasAPlainEnglishDescription(AltTextProblem problem)
    {
        Assert.False(string.IsNullOrWhiteSpace(WeakAltText.Describe(problem)));
    }
}
