namespace DfE.CheckPerformanceData.Web.Admin.Rules;

public sealed class LookupRowEditForm
{
    public string Code { get; set; } = string.Empty;
    public bool IsNew { get; set; }
    public string? LoadETag { get; set; }
    public List<string> Languages { get; set; } = new();
    public string? Action { get; set; } // save | addLanguage | removeLanguage:<index>
}

public sealed class LookupRowEditViewModel
{
    public required LookupRowEditForm Form { get; init; }
    public required IReadOnlyList<string> Errors { get; init; }

    public static LookupRowEditViewModel For(LookupRowEditForm form, IReadOnlyList<string>? errors = null) =>
        new() { Form = form, Errors = errors ?? Array.Empty<string>() };
}
