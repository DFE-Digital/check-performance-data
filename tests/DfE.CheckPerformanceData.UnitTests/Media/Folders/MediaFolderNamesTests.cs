using DfE.CheckPerformanceData.Application.Media.Folders;

namespace DfE.CheckPerformanceData.UnitTests.Media.Folders;

public sealed class MediaFolderNamesTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ABlankName_IsMissing(string? name)
    {
        Assert.Equal((null, MediaFolderNames.Missing), MediaFolderNames.Check(name));
    }

    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("a\tb")]
    public void ASlashOrControlCharacter_IsRefused(string name)
    {
        Assert.Equal((null, MediaFolderNames.BadCharacter), MediaFolderNames.Check(name));
    }

    [Fact]
    public void ALongName_IsRefused()
    {
        Assert.Equal((null, MediaFolderNames.TooLong), MediaFolderNames.Check(new string('x', MediaFolderNames.MaxLength + 1)));
        Assert.Equal(MediaFolderNames.MaxLength, MediaFolderNames.Check(new string('x', MediaFolderNames.MaxLength)).Name!.Length);
    }

    [Fact]
    public void AName_IsTrimmed()
    {
        Assert.Equal(("School photos", null), MediaFolderNames.Check("  School photos "));
    }
}
