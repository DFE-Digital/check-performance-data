namespace DfE.CheckPerformanceData.Application.Journey;

/// <summary>
/// One CMS block of an EvidenceUpload page's requirements (#591). <see cref="OptionLabel"/> is the
/// label of the contentKey Checkbox option the block is for, or null when no Checkbox made it. It
/// names the option in a new block's default text, so a guidance writer can tell the blocks apart.
/// </summary>
public sealed record EvidenceContentBlock(string Key, string? OptionLabel);
