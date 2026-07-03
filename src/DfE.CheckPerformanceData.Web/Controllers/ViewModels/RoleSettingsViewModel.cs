namespace DfE.CheckPerformanceData.Web.Controllers.ViewModels;

public sealed class RoleSettingsViewModel
{
    public required IReadOnlyList<string> Sections { get; init; }
    public required IReadOnlyList<string> Roles { get; init; }
    public required Func<string, string, bool> IsGranted { get; init; }
    public string? SuccessMessage { get; init; }
}

public sealed class RoleSettingsFormModel
{
    // The list of role columns as submitted; drives which roles' grants get replaced. Any role
    // not in this list is untouched (won't accidentally lose access if the form is a stale post).
    public List<string>? Roles { get; set; }

    // Dictionary keyed by role → list of section keys checked in the grid. Absent role or empty
    // list means "no access for this role".
    public Dictionary<string, List<string>?>? Grants { get; set; }

    // Optional new-role name to register (with empty grants).
    public string? NewRoleName { get; set; }
}
