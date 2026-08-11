namespace DfE.CheckPerformanceData.Application.UnitTests.Analytics;

using DfE.CheckPerformanceData.Application.Analytics;

public sealed class ClientEventsTests
{
    [Fact]
    public void HelpDetailsExpandedEvent_projects_text_and_path()
    {
        var e = new HelpDetailsExpandedEvent { ExpandText = "How can I find a DfE number?", PagePath = "/Journey/x/page/y" };
        Assert.Equal("help_details_expanded", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("How can I find a DfE number?", byName["expand_text"].Value);
        Assert.Equal("/Journey/x/page/y", byName["page_path"].Value);
    }

    [Fact]
    public void ExternalLinkClickedEvent_projects_destination_and_path()
    {
        var e = new ExternalLinkClickedEvent { Destination = "gias", PagePath = "/Journey/x/page/y" };
        Assert.Equal("external_link_clicked", e.EventType);
        var byName = e.Fields.ToDictionary(f => f.Name);
        Assert.Equal("gias", byName["destination"].Value);
    }

    [Fact]
    public void EvidenceFileSelectedEvent_projects_path()
    {
        var e = new EvidenceFileSelectedEvent { PagePath = "/Journey/x/page/evidence" };
        Assert.Equal("evidence_file_selected", e.EventType);
        Assert.Equal("/Journey/x/page/evidence", e.Fields.Single(f => f.Name == "page_path").Value);
    }
}
