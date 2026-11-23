namespace DfE.CheckPerformanceData.Web.Navigation;

/// <summary>
/// One link in the school-facing service navigation. <see cref="Active"/> marks the item for
/// the section that the user is in. <see cref="CurrentPage"/> is true when the link goes to the
/// page itself.
/// </summary>
public sealed record ServiceNavItemModel(string? Href, string Text, bool Active, bool CurrentPage);
