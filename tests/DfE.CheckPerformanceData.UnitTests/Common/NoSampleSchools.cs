using DfE.CheckPerformanceData.Application.WindowManagement.SampleData;
using NSubstitute;

namespace DfE.CheckPerformanceData.Application.UnitTests.Common;

/// <summary>#938: a sample school list that is empty for every window, for ingress tests about other things.</summary>
public static class NoSampleSchools
{
    public static ISampleSchoolRepository Repository()
    {
        var repository = Substitute.For<ISampleSchoolRepository>();
        repository.ListAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns([]);
        return repository;
    }
}
