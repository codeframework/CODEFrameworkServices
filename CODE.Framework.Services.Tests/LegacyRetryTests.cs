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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Aborted_poll_does_not_escape_or_update_previous_result_and_next_poll_recovers(bool retry)
    {
        ServiceClient.AutoRetryFailedCalls = retry;
        ServiceClient.AutoRetryDelay = 0;
        ServiceClient.AutoRetryFailedCallsForExceptions = [typeof(CommunicationException)];
        var request = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 17, Right = 25 };
        var previous = await Task.Run(() => ServiceClient.GetChannelDedicated<ITestService>(host.ServiceUrl).Lookup(request)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(previous);

        var failedRequest = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 20, Right = 30 };
        var result = previous;
        var callbacks = 0;
        var completed = false;
        var received = 0;
        host.InterceptRequest = context =>
        {
            Interlocked.Increment(ref received);
            context.Abort();
            return Task.FromResult(true);
        };
        await Task.Run(() => ServiceClient.Call<ITestService>(service =>
        {
            callbacks++;
            result = service.Lookup(failedRequest);
            completed = true;
        }, host.ServiceUrl)).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.True(received > 0);
        Assert.Equal(retry ? 2 : 1, callbacks);
        Assert.False(completed);
        Assert.Same(previous, result);
        Assert.DoesNotContain(host.Log.Entries, entry => entry.RequestId == failedRequest.RequestId);

        host.InterceptRequest = null;
        await Task.Run(() => ServiceClient.Call<ITestService>(service => result = service.Lookup(failedRequest), host.ServiceUrl)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(result);
        Assert.Equal(failedRequest.RequestId, result.RequestId);
        Assert.Equal(50, result.Total);
        Assert.Equal(host.Log.ServerId, result.ServerId);
        Assert.Single(host.Log.Entries, entry => entry.RequestId == failedRequest.RequestId);
    }

    public void Dispose()
    {
        host.InterceptRequest = null;
        ServiceClient.AutoRetryFailedCalls = _retry;
        ServiceClient.AutoRetryDelay = _delay;
        ServiceClient.AutoRetryFailedCallsForExceptions = _exceptions;
    }
}
