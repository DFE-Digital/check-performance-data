using DfE.CheckPerformanceData.Application.Queue;

namespace DfE.CheckPerformanceData.Application.UnitTests.Queue;

// Pulls the request reference out of a queued RequestDocument payload so admin surfaces can
// link a message to its journey timeline. Extraction is best-effort: any malformed payload
// yields null (no link) rather than an exception — the queue admin pages must render whatever
// the payload looks like.
public sealed class QueuePayloadReferenceTests
{
    [Fact]
    public void TryExtract_ReturnsTheReferenceNumber()
    {
        var payload = """{ "ReferenceNumber": "KS4-1234", "RequestTypeCode": "Attainment" }""";

        Assert.Equal("KS4-1234", QueuePayloadReference.TryExtract(payload));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("[1,2,3]")]
    [InlineData("{ \"WhatToChange\": \"Attainment\" }")]
    [InlineData("{ \"ReferenceNumber\": 42 }")]
    [InlineData("{ \"ReferenceNumber\": \"\" }")]
    public void TryExtract_MalformedOrMissing_ReturnsNull(string? payload)
    {
        Assert.Null(QueuePayloadReference.TryExtract(payload));
    }
}
