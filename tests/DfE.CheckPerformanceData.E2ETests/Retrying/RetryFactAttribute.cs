using System;
using Xunit;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Retrying;

/// <summary>
/// A fact that is run up to <see cref="RetrySettings.MaxRetries"/> times.
///
/// The attempt count comes from the <c>CPD_E2E_RETRY_ATTEMPTS</c> environment
/// variable (default 1 = no retrying), never from a literal here — pass the
/// xRetry-style numeric argument and the compiler stops you. Effectively a
/// zero-cost <see cref="FactAttribute"/> when the default is left in place.
/// </summary>
[XunitTestCaseDiscoverer(
    "DfE.CheckPerformanceData.E2ETests.Retrying.RetryFactDiscoverer",
    "DfE.CheckPerformanceData.E2ETests")]
[AttributeUsage(AttributeTargets.Method)]
public sealed class RetryFactAttribute : FactAttribute
{
}