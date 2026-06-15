namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Thrown by <see cref="IRulesConfigStore.ReadAsync"/> when the requested config blob does
/// not exist (e.g. before it has ever been written). Callers decide how to surface this.
/// </summary>
public sealed class RulesConfigNotFoundException : Exception
{
    public RulesConfigNotFoundException(string message) : base(message) { }
}
