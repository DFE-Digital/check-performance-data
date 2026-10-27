namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// One CMS block of an EvidenceUpload page's requirements (#591). <see cref="OptionLabel"/> is the
/// label of the contentKey Checkbox option the block is for, or null when no Checkbox made it. It
/// names the option in a new block's default text, so a guidance writer can tell the blocks apart.
/// <see cref="SectionLabel"/> is set only on the shared block a contentKey Checkbox adds after its
/// option blocks (#633): the guidance that shows whatever the school ticks. It names the section in
/// the same way.
/// </summary>
public sealed record EvidenceContentBlock(string Key, string? OptionLabel, string? SectionLabel = null);
