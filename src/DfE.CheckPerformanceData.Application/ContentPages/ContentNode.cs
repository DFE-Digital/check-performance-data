using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// A node in a content page's tree. Two kinds: a region (layout container with columns of child
// nodes, nestable) or a widget (a typed content component). Persisted as JSON with a "kind"
// discriminator so the tree snapshots trivially and new widget/region shapes need no migration.
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(RegionNode), "region")]
[JsonDerivedType(typeof(WidgetNode), "widget")]
public abstract class ContentNode
{
}
