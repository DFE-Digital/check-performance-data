using System;
using System.Collections.Generic;
using System.Linq;
using xRetry;
using Xunit.Abstractions;
using Xunit.Sdk;

namespace DfE.CheckPerformanceData.E2ETests.Retrying;

/// <summary>
/// Mirrors xRetry's <c>RetryFactDiscoverer</c> but drives the attempt count from
/// <see cref="RetrySettings.MaxRetries"/> instead of a literal on the attribute.
/// Reuses <see cref="xRetry.RetryTestCase"/> so the retry loop, delay handling and
/// skip-on-exception behaviour are byte-for-byte xRetry's.
/// </summary>
public sealed class RetryFactDiscoverer : IXunitTestCaseDiscoverer
{
    private readonly IMessageSink messageSink;

    public RetryFactDiscoverer(IMessageSink messageSink)
    {
        this.messageSink = messageSink;
    }

    public IEnumerable<IXunitTestCase> Discover(
        ITestFrameworkDiscoveryOptions discoveryOptions,
        ITestMethod testMethod,
        IAttributeInfo factAttribute)
    {
        if (testMethod.Method.GetParameters().Any())
        {
            return new[]
            {
                new ExecutionErrorTestCase(
                    messageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    "[RetryFact] methods are not allowed to have parameters. Did you mean to use [RetryTheory]?")
            };
        }

        if (testMethod.Method.IsGenericMethodDefinition)
        {
            return new[]
            {
                new ExecutionErrorTestCase(
                    messageSink,
                    discoveryOptions.MethodDisplayOrDefault(),
                    discoveryOptions.MethodDisplayOptionsOrDefault(),
                    testMethod,
                    "[RetryFact] methods are not allowed to be generic.")
            };
        }

        return new[]
        {
            new RetryTestCase(
                messageSink,
                discoveryOptions.MethodDisplayOrDefault(),
                discoveryOptions.MethodDisplayOptionsOrDefault(),
                testMethod,
                RetrySettings.MaxRetries,
                delayBetweenRetriesMs: 0,
                skipOnExceptions: Type.EmptyTypes)
        };
    }
}