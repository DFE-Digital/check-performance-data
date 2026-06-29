using System.Text.Json.Serialization;

namespace DfE.CheckPerformanceData.Application.ContentPages;

// How a region splits its area into columns. Each value maps to a GDS grid column class in the
// renderer; the set is a closed allow-list so a layout can never carry arbitrary CSS. Serialised
// as kebab-case to read naturally in the stored JSON.
public enum RegionLayout
{
    [JsonStringEnumMemberName("single")] Single,
    [JsonStringEnumMemberName("halves")] Halves,
    [JsonStringEnumMemberName("thirds")] Thirds,
    [JsonStringEnumMemberName("quarters")] Quarters,
    [JsonStringEnumMemberName("one-third-two-thirds")] OneThirdTwoThirds,
    [JsonStringEnumMemberName("two-thirds-one-third")] TwoThirdsOneThird
}
