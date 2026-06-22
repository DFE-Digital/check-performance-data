using DfE.CheckPerformanceData.Web.Admin.Nav;

namespace DfE.CheckPerformanceData.Application.UnitTests.Web.Admin;

// The "Stages / Queues" nav group lists all six pipeline positions in animation order: Submit,
// Rules-queue, Rules engine, Zendesk-queue, Zendesk ticket, Dead-letter queue. The three queues
// link to their list pages; the other three stages link to the transactions page filtered to that
// stage, so every stage is reachable from the nav (so the per-stage views can be tested).
public sealed class StagesQueuesNavTests
{
    [Fact]
    public void Group_IsRenamedStagesSlashQueues()
    {
        Assert.Equal("Stages / Queues", new RulesEngineNavEntry().Title);
    }

    [Fact]
    public void Group_HasAllSixStages_InAnimationOrder_WithTheRightUrls()
    {
        IAdminNavEntry[] children =
        {
            new SubmitStageNavEntry(),
            new RulesEngineQueueNavEntry(),
            new RulesEngineStageNavEntry(),
            new ZendeskQueueNavEntry(),
            new ZendeskTicketStageNavEntry(),
            new DeadLetterQueueNavEntry(),
        };

        // All six hang off the Stages / Queues group.
        Assert.All(children, c => Assert.Equal(AdminNavKeys.RulesEngine, c.ParentKey));

        // Ordered by the nav Order, they read in animation order with the expected destinations.
        var ordered = children.OrderBy(c => c.Order)
            .Select(c => (c.Title, c.Url))
            .ToArray();

        Assert.Equal(new[]
        {
            ("Submit", "/admin/observability/transactions?stage=Submitted"),
            ("Rules Engine Queue", "/admin/queues/list/rules-engine"),
            ("Rules engine", "/admin/observability/transactions?stage=RulesEvaluated"),
            ("Zendesk Queue", "/admin/queues/list/zendesk"),
            ("Zendesk ticket", "/admin/observability/transactions?stage=TicketCreated"),
            ("Dead Letter Queue", "/admin/queues/dlq"),
        }, ordered);
    }
}
