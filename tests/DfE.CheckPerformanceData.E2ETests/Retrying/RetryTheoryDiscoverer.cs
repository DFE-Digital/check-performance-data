using System;
using System.Collections.Generic;
using xRetry;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Retrying;

/// <summary>
/// Mirrors xRetry's <c>RetryTheoryDiscoverer</c> but drives the attempt count from
/// <see cref="RetrySettings.MaxRetries"/> instead of a literal on the attribute.
/// Reuses xRetry's test-case classes so racking the retry loop matches xRetry's.
/// </summary>
public sealed class RetryTheoryDiscoverer : TheoryDiscoverer
{
    public RetryTheoryDiscoverer(IMessageSink diagnosticMessageSink)
        : base(diagnosticMessageSink)
    {
    }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForDataRow(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute,
        object[] dataRow)
    {
        return new[]
        {
            new RetryTestCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                RetrySettings.MaxRetries,
                0,
                Type.EmptyTypes,
                dataRow)
        };
    }

    protected override IEnumerable<IXunitTestCase> CreateTestCasesForTheory(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo theoryAttribute)
    {
        return new[]
        {
            new RetryTheoryDiscoveryAtRuntimeCase(
                DiagnosticMessageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                RetrySettings.MaxRetries,
                0,
                Type.EmptyTypes)
        };
    }
}