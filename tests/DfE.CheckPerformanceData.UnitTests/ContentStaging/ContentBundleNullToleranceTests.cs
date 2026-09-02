using DfE.CheckPerformanceData.Application.ContentStaging;

namespace DfE.CheckPerformanceData.Application.UnitTests.ContentStaging;

// Every collection and string on the bundle DTOs is declared non-nullable and carries a
// default, but a default only applies when the property is ABSENT. System.Text.Json will
// happily assign null when the JSON says null, and from that point the type is lying: code
// downstream reads a non-nullable member and dereferences it.
//
// `"content": null` is a one-token edit of a legitimate bundle, so this is not an exotic
// payload — and an uploaded bundle is untrusted input regardless. Whatever the shape, the
// operator has to get a validation banner rather than a 500.
public class ContentBundleNullToleranceTests
{
    private static string BundleWith(string pagesJson, string blocksJson = "[]") => $$"""
        {
          "$schema": "cpd-content-v2",
          "schemaVersion": 2,
          "pageNodes": {{pagesJson}},
          "contentBlocks": {{blocksJson}}
        }
        """;

    private const string ValidPage = """
        { "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
          "title": "Help", "pageType": "folder", "versions": [] }
        """;

    public static TheoryData<string, string> NullShapes() => new()
    {
        { "pageNodes null", BundleWith("null") },
        { "contentBlocks null", BundleWith("[]", "null") },
        { "null element in pageNodes", BundleWith($"[{ValidPage}, null]") },
        { "null element in contentBlocks", BundleWith("[]", "[null]") },
        {
            "page.versions null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": "Help", "pageType": "content", "versions": null }]
                """)
        },
        {
            "page.segment null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": null,
                   "title": "Help", "pageType": "folder", "versions": [] }]
                """)
        },
        {
            "page.title null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": null, "pageType": "folder", "versions": [] }]
                """)
        },
        {
            "page.pageType null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": "Help", "pageType": null, "versions": [] }]
                """)
        },
        {
            "version.content null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": "Help", "pageType": "content",
                   "versions": [{ "versionId": 1, "content": null }] }]
                """)
        },
        {
            "version.bodyPlainText null",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": "Help", "pageType": "content",
                   "versions": [{ "versionId": 1, "content": "x", "bodyPlainText": null }] }]
                """)
        },
        {
            "null element in versions",
            BundleWith("""
                [{ "id": "11111111-1111-1111-1111-111111111111", "segment": "help",
                   "title": "Help", "pageType": "content", "versions": [null] }]
                """)
        },
        {
            "block.value null",
            BundleWith("[]", """
                [{ "id": "22222222-2222-2222-2222-222222222222", "key": "banner",
                   "blockType": "Content", "value": null }]
                """)
        },
        {
            "block.key null",
            BundleWith("[]", """
                [{ "id": "22222222-2222-2222-2222-222222222222", "key": null,
                   "blockType": "Content", "value": "x" }]
                """)
        },
        {
            "block.blockType null",
            BundleWith("[]", """
                [{ "id": "22222222-2222-2222-2222-222222222222", "key": "banner",
                   "blockType": null, "value": "x" }]
                """)
        },
    };

    // Parsing must never hand back a bundle whose "non-nullable" members are null.
    [Theory]
    [MemberData(nameof(NullShapes))]
    public void Deserialize_NullMembers_AreNormalisedToEmpty(string shape, string json)
    {
        var bundle = ContentStagingJson.Deserialize(json);

        Assert.NotNull(bundle);
        Assert.NotNull(bundle!.PageNodes);
        Assert.NotNull(bundle.ContentBlocks);
        Assert.DoesNotContain(bundle.PageNodes, p => p is null);
        Assert.DoesNotContain(bundle.ContentBlocks, b => b is null);

        foreach (var page in bundle.PageNodes)
        {
            Assert.NotNull(page.Segment);
            Assert.NotNull(page.Title);
            Assert.NotNull(page.PageType);
            Assert.NotNull(page.Versions);
            Assert.DoesNotContain(page.Versions, v => v is null);
            foreach (var version in page.Versions)
            {
                Assert.NotNull(version.Content);
                Assert.NotNull(version.BodyPlainText);
            }
        }

        foreach (var block in bundle.ContentBlocks)
        {
            Assert.NotNull(block.Key);
            Assert.NotNull(block.BlockType);
            Assert.NotNull(block.Value);
        }

        _ = shape;
    }

    // The validator is the gate an uploaded bundle passes through, so it must reach a verdict
    // on any of these rather than throw.
    [Theory]
    [MemberData(nameof(NullShapes))]
    public void Validate_NullMembers_ReachesAVerdictInsteadOfThrowing(string shape, string json)
    {
        var bundle = ContentStagingJson.Deserialize(json);

        var issues = ContentBundleValidator.Validate(bundle!);

        Assert.NotNull(issues);
        _ = shape;
    }

    // Serialising a normalised bundle and reading it back must be a fixed point — otherwise a
    // stored preview session could reintroduce the nulls on the confirm step.
    [Theory]
    [MemberData(nameof(NullShapes))]
    public void Deserialize_ThenSerialize_ThenDeserialize_StaysNormalised(string shape, string json)
    {
        var once = ContentStagingJson.Deserialize(json);
        var twice = ContentStagingJson.Deserialize(ContentStagingJson.Serialize(once!));

        Assert.NotNull(twice);
        Assert.DoesNotContain(twice!.PageNodes, p => p is null);
        Assert.All(twice.PageNodes, p => Assert.NotNull(p.Versions));
        _ = shape;
    }
}
