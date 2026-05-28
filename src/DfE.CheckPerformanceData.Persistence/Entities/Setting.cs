namespace DfE.CheckPerformanceData.Persistence.Entities;

// A single persisted key/value setting. Absence of a row means "use the code-declared
// default" (resolved in the Application layer). Key is the natural primary key.
public sealed class Setting
{
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
}
