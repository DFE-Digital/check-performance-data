using DfE.CheckPerformanceData.Application.CurrentUser;

namespace DfE.CheckPerformanceData.IntegrationTests.Fixtures;

public sealed class FakeCurrentUserService : ICurrentUserService
{
    public string? UserId => "test-user";
    public string? DisplayName => "Test User";
}
