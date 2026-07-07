using DfE.CheckPerformanceData.Application.ContentPages;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentPages;

// The editor addresses a tree position with a path of steps; over the wire (form fields) that path is
// a compact string. TreePath is the round-tripping codec — "col.idx" segments joined by "-" — and it
// rejects anything malformed rather than silently mis-targeting an edit.
public class TreePathTests
{
    [Fact]
    public void Format_SingleStep() =>
        Assert.Equal("0.2", TreePath.Format([new TreeStep(0, 2)]));

    [Fact]
    public void Format_NestedSteps() =>
        Assert.Equal("0.2-1.0", TreePath.Format([new TreeStep(0, 2), new TreeStep(1, 0)]));

    [Fact]
    public void Parse_SingleStep() =>
        Assert.Equal([new TreeStep(0, 2)], TreePath.Parse("0.2"));

    [Fact]
    public void Parse_NestedSteps() =>
        Assert.Equal([new TreeStep(0, 2), new TreeStep(1, 0)], TreePath.Parse("0.2-1.0"));

    [Fact]
    public void Parse_IsInverseOfFormat()
    {
        IReadOnlyList<TreeStep> path = [new TreeStep(0, 2), new TreeStep(1, 0), new TreeStep(0, 3)];

        Assert.Equal(path, TreePath.Parse(TreePath.Format(path)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("x")]
    [InlineData("0")]
    [InlineData("0.")]
    [InlineData("0.a-1.0")]
    [InlineData("-")]
    [InlineData("0.-1")]
    public void Parse_RejectsMalformedInput(string value) =>
        Assert.Throws<FormatException>(() => TreePath.Parse(value));
}
