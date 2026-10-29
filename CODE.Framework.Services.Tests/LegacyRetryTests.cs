using System.Diagnostics;
using CODE.Framework.Services.Client;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class LegacyRetryTests(HttpServiceFixture host) : IClassFixture<HttpServiceFixture>, IDisposable
{
    private readonly bool _retry = ServiceClient.AutoRetryFailedCalls;
    private readonly int _delay = ServiceClient.AutoRetryDelay;
    private readonly List<Type> _exceptions = ServiceClient.AutoRetryFailedCallsForExceptions;

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Null_and_empty_filters_retry_once_and_honor_delay(bool nullFilter)
    {
        ServiceClient.AutoRetryFailedCalls = true;
        ServiceClient.AutoRetryFailedCallsForExceptions = nullFilter ? null : [];
        ServiceClient.AutoRetryDelay = 80;
        var attempts = 0;
        var elapsed = Stopwatch.StartNew();
        ServiceClient.Call<ITestService>(_ => { attempts++; throw new InvalidOperationException(); }, host.ServiceUrl);
        Assert.Equal(2, attempts);
        // Only assert the configured minimum, never an upper scheduling bound.
        Assert.True(elapsed.ElapsedMilliseconds >= 80);
    }

    [Theory]
    [InlineData(false, false, 1)]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 2)]
    public void Legacy_call_swallows_final_failure_and_keeps_exact_type_matching(bool retry, bool exactMatch, int expectedAttempts)
    {
        ServiceClient.AutoRetryFailedCalls = retry;
        ServiceClient.AutoRetryDelay = -1;
        ServiceClient.AutoRetryFailedCallsForExceptions = [exactMatch ? typeof(InvalidOperationException) : typeof(Exception)];
        var attempts = 0;
        ServiceClient.Call<ITestService>(_ => { attempts++; throw new InvalidOperationException(); }, host.ServiceUrl);
        Assert.Equal(expectedAttempts, attempts);
    }

    public void Dispose()
    {
        ServiceClient.AutoRetryFailedCalls = _retry;
        ServiceClient.AutoRetryDelay = _delay;
        ServiceClient.AutoRetryFailedCallsForExceptions = _exceptions;
    }
}
