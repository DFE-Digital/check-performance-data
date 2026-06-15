namespace DfE.CheckPerformanceData.Application.RulesConfig;

/// <summary>
/// Thrown by <see cref="IRulesConfigStore"/> when a write's expected ETag no longer matches
/// the blob — i.e. someone else saved since this edit session loaded.
/// </summary>
public sealed class RulesConfigConflictException : Exception
{
    public RulesConfigConflictException(string message) : base(message) { }
}
