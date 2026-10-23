using DfE.CheckPerformanceData.Infrastructure.Ingress;

namespace DfE.CheckPerformanceData.UnitTests.Ingress;

public sealed class IngressSummaryCaptureTests
{
    private static readonly DateTime StartedAt = new(2026, 10, 22, 9, 30, 0, DateTimeKind.Utc);

    [Fact]
    public void Build_AggregatesRowsByFileLaestabAndUrnInStableOrder()
    {
        var windowId = Guid.NewGuid();
        var exerciseId = Guid.NewGuid();
        var datasetId = Guid.NewGuid();
        var capture = new IngressSummaryCapture(windowId, exerciseId,
            [new IngressDataset("pupils", "ingress/pupils.csv", "csv-hash", "schema/pupils.json", "schema-hash",
                Included: null, DatasetId: datasetId)], StartedAt);

        capture.SetUrnColumn(0, true);
        capture.MarkFileStarted(0);
        capture.AddRow(0, "8604070", "100001");
        capture.AddRow(0, "8604070", "100001");
        capture.AddRow(0, "8604070", "100002");
        capture.AddRow(0, "8604071", "200001");
        capture.MarkFileRead(0);

        var summary = capture.Build("succeeded", Guid.NewGuid(), StartedAt.AddMinutes(1));

        Assert.Equal("succeeded", summary.Outcome);
        Assert.Equal(windowId.ToString(), summary.CheckingWindowId);
        Assert.Equal(exerciseId.ToString(), summary.CheckingExerciseId);
        Assert.NotEqual(Guid.Empty, Guid.Parse(summary.AttemptId));
        Assert.True(summary.ReadComplete);
        Assert.Equal(4, summary.Totals.RowsRead);
        Assert.Equal(4, summary.Totals.RowsImported);

        var file = Assert.Single(summary.Files);
        Assert.Equal(datasetId.ToString(), file.DatasetId);
        Assert.Equal("pupils.csv", file.SourceFile);
        Assert.Equal("ingress/pupils.csv", file.InputFile);
        Assert.Equal("read", file.State);
        Assert.Equal(4, file.Totals.RowsRead);
        Assert.Equal(4, file.Totals.RowsImported);

        Assert.Equal(["8604070", "8604071"], file.Laestabs.Select(item => item.Laestab));
        Assert.Equal(3, file.Laestabs[0].Totals.RowsRead);
        Assert.Equal(3, file.Laestabs[0].Totals.RowsImported);
        Assert.Equal(["100001", "100002"], file.Laestabs[0].Urns!.Select(item => item.Urn));
        Assert.Equal(2, file.Laestabs[0].Urns![0].RowsRead);
        Assert.Equal(2, file.Laestabs[0].Urns![0].RowsImported);
        Assert.Null(summary.Failure);
    }

    [Fact]
    public void Build_ForFailedPartialRead_KeepsReadCountsAndDoesNotClaimImports()
    {
        var capture = new IngressSummaryCapture(Guid.NewGuid(), Guid.NewGuid(),
            [new IngressDataset("pupils", "pupils.csv", "csv-hash", "pupils.json", "schema-hash", null)],
            StartedAt);
        capture.MarkFileStarted(0);
        capture.AddRow(0, null, null);
        capture.SetFailure("parsing", "malformed_row");

        var summary = capture.Build("failed", releaseId: null, StartedAt.AddMinutes(1));

        Assert.False(summary.ReadComplete);
        Assert.Equal(1, summary.Totals.RowsRead);
        Assert.Equal(0, summary.Totals.RowsImported);
        Assert.Equal("parsing", summary.Failure!.Stage);
        Assert.Equal("malformed_row", summary.Failure.Code);
        var file = Assert.Single(summary.Files);
        Assert.Equal("partially_read", file.State);
        Assert.Equal(1, file.Totals.RowsRead);
        Assert.Equal(0, file.Totals.RowsImported);
        var laestab = Assert.Single(file.Laestabs);
        Assert.Null(laestab.Laestab);
        Assert.Equal(1, laestab.Totals.RowsRead);
        Assert.Equal(0, laestab.Totals.RowsImported);
        Assert.Null(laestab.Urns);
        Assert.Null(summary.ReleaseId);
    }
}
