using DfE.CheckPerformanceData.Web.Admin.Rules;
using DfE.CheckPerformanceData.Web.Views.Admin.Rules;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin.Rules;

public sealed class EditorNodeContextTests
{
    [Fact]
    public void SummaryChildren_Returns_One_Describer_Line_Per_Child_Without_Group_Header()
    {
        // group AllOf(1) -> [ hasTerminalIllness = false, hasSatExamsAsYear11 = true ]
        var nodes = new List<PredicateNodeForm>
        {
            new() { Id = 1, ParentId = null, Kind = PredicateKind.AllOf },
            new() { Id = 2, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "hasTerminalIllness", Operator = "eq", Value = "false" },
            new() { Id = 3, ParentId = 1, Kind = PredicateKind.FieldEq, Field = "hasSatExamsAsYear11", Operator = "eq", Value = "true" },
        };

        var context = new EditorNodeContext
        {
            Nodes = nodes,
            AllFields = new[] { "hasTerminalIllness", "hasSatExamsAsYear11" },
            Node = nodes[0]
        };

        var lines = context.SummaryChildren();

        // One line per child, and NOT the group's own "All of the following are true:" header.
        Assert.Equal(2, lines.Count);
        Assert.DoesNotContain(lines, l => l.Text.Contains("All of the following"));
        Assert.StartsWith("hasTerminalIllness is", lines[0].Text);
        Assert.StartsWith("hasSatExamsAsYear11 is", lines[1].Text);
        Assert.All(lines, l => Assert.True(l.IsLeaf));
    }
}
