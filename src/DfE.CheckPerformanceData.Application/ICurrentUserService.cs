namespace DfE.CheckPerformanceData.Application;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? DisplayName { get; }
}
