using System;
using Xunit;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Retrying;

/// <summary>
/// A theory that is run up to <see cref="RetrySettings.MaxRetries"/> times.
///
/// The attempt count comes from the <c>CPD_E2E_RETRY_ATTEMPTS</c> environment
/// variable (default 1 = no retrying), never from a literal here. Effectively a
/// zero-cost <see cref="TheoryAttribute"/> when the default is left in place.
/// </summary>
[XunitTestCaseDiscoverer(
    "DfE.CheckPerformanceData.E2ETests.Retrying.RetryTheoryDiscoverer",
    "DfE.CheckPerformanceData.E2ETests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class RetryTheoryAttribute : TheoryAttribute
{
}