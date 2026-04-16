namespace DfE.CheckPerformanceData.Web.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? DisplayName { get; }
}
