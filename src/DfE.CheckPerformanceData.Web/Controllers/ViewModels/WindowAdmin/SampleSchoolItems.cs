namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels.WindowAdmin;

/// <summary>#937: the add form, and the values that the confirmation page posts back.</summary>
public sealed class SampleSchoolAddItem : AdminPage
{
    public string WindowTitle { get; init; } = string.Empty;
    public string? Laestab { get; init; }
    public string? Urn { get; init; }

    /// <summary>The school search, which fills the LAESTAB and URN fields.</summary>
    public string SearchUrl { get; init; } = string.Empty;
}

/// <summary>#937: the confirmation page of an add. The values are checked and in their stored form.</summary>
public sealed class SampleSchoolConfirmItem : AdminPage
{
    public required string WindowTitle { get; init; }
    public required string Laestab { get; init; }
    public required string LaestabDisplay { get; init; }
    public required string Urn { get; init; }

    /// <summary>The school's name from the establishment directory, or null when the directory does not have the school.</summary>
    public string? SchoolName { get; init; }

    public string? ChangeUrl { get; init; }
}

/// <summary>#937: the confirmation page of a remove.</summary>
public sealed class SampleSchoolRemoveItem : AdminPage
{
    public required string WindowTitle { get; init; }
    public required string LaestabDisplay { get; init; }
    public required string Urn { get; init; }
}

/// <summary>#937: one row of the sample schools table on the window page.</summary>
public sealed class SampleSchoolRow
{
    public required string LaestabDisplay { get; init; }
    public required string Urn { get; init; }
    public required string AddedBy { get; init; }

    /// <summary>UTC: admin pages show UTC.</summary>
    public required DateTime AddedAt { get; init; }

    public required string RemoveLink { get; init; }
}
