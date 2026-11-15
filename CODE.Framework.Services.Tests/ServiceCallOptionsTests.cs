using System.Net;
using System.Runtime.Serialization;
using CODE.Framework.Services.Client;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class ServiceCallOptionsTests(HttpServiceFixture host) : IClassFixture<HttpServiceFixture>, IDisposable
{
    private readonly bool _legacyRetry = ServiceClient.AutoRetryFailedCalls;
    private readonly List<Type> _legacyFilter = ServiceClient.AutoRetryFailedCallsForExceptions;
    private readonly int _legacyDelay = ServiceClient.AutoRetryDelay;

    [Fact]
    public async Task Retry_repeats_only_failed_operation_not_callback_or_prior_operation()
    {
        var attempts = 0;
        host.InterceptRequest = context =>
        {
            if (context.Request.Path.Value.Contains("/Calculate/") && Interlocked.Increment(ref attempts) == 1)
            {
                context.Response.StatusCode = 503;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        };
        var request = Request();
        var callbackCount = 0;
        CalculationResponse result = null;
        await Run(() => ServiceClient.Call<ITestService>(RetryOptions(), service =>
        {
            callbackCount++;
            service.Lookup(request);
            result = service.Calculate(request);
        }, host.ServiceUrl));
        Assert.Equal(1, callbackCount);
        Assert.Equal(2, attempts);
        Assert.Equal(42, result.Total);
        Assert.Equal(host.Log.ServerId, result.ServerId);
        Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId && entry.Operation == "Lookup");
        Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId && entry.Operation == "Calculate");
    }

    [Fact]
    public async Task Caller_exception_propagates_without_replaying_success_even_with_legacy_retries_enabled()
    {
        ServiceClient.AutoRetryFailedCalls = true;
        ServiceClient.AutoRetryFailedCallsForExceptions = [];
        ServiceClient.AutoRetryDelay = -1;
        var request = Request();
        var expected = new InvalidOperationException("Application processing failed");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => Run(() => ServiceClient.Call<ITestService>(RetryOptions(), service =>
        {
            service.Calculate(request);
            throw expected;
        }, host.ServiceUrl)));
        Assert.Same(expected, error);
        Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Theory]
    [InlineData(503, true, 3, 3)]
    [InlineData(429, true, 3, 3)]
    [InlineData(401, true, 3, 1)]
    [InlineData(403, true, 3, 1)]
    [InlineData(400, true, 3, 1)]
    [InlineData(500, true, 3, 1)]
    [InlineData(503, false, 3, 1)]
    [InlineData(503, true, 1, 1)]
    public async Task Http_failures_have_bounded_attempts_and_preserve_status(int status, bool repeatable, int maxAttempts, int expectedAttempts)
    {
        var attempts = 0;
        host.InterceptRequest = context =>
        {
            Interlocked.Increment(ref attempts);
            context.Response.StatusCode = status;
            return Task.FromResult(true);
        };
        var options = new ServiceCallOptions { MaxAttempts = maxAttempts, RetryDelay = TimeSpan.Zero, CanRetryOperation = repeatable ? _ => true : null };
        var error = await Assert.ThrowsAsync<ServiceTransportException>(() => Run(() => ServiceClient.Call<ITestService>(options, service => service.Calculate(Request()), host.ServiceUrl)));
        Assert.Equal((HttpStatusCode)status, error.StatusCode);
        Assert.IsType<HttpRequestException>(error.InnerException);
        Assert.Equal(expectedAttempts, attempts);
    }

    [Fact]
    public async Task Default_options_do_not_retry()
    {
        var attempts = 0;
        host.InterceptRequest = context => { Interlocked.Increment(ref attempts); context.Response.StatusCode = 503; return Task.FromResult(true); };
        await Assert.ThrowsAsync<ServiceTransportException>(() => Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service => service.Calculate(Request()), host.ServiceUrl)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Explicit_suppression_still_does_not_replay_callback()
    {
        var callbacks = 0;
        var options = RetryOptions();
        options.ExceptionBehavior = ServiceExceptionBehavior.Suppress;
        await Run(() => ServiceClient.Call<ITestService>(options, _ => { callbacks++; throw new InvalidOperationException(); }, host.ServiceUrl));
        Assert.Equal(1, callbacks);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("null")]
    [InlineData("")]
    public async Task Invalid_response_is_not_retried(string payload)
    {
        var attempts = 0;
        host.InterceptRequest = async context =>
        {
            Interlocked.Increment(ref attempts);
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(payload);
            return true;
        };
        await Assert.ThrowsAnyAsync<JsonException>(() => Run(() => ServiceClient.Call<ITestService>(RetryOptions(), service => service.Calculate(Request()), host.ServiceUrl)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Request_serialization_failure_never_reaches_server()
    {
        var attempts = 0;
        host.InterceptRequest = _ => { Interlocked.Increment(ref attempts); return Task.FromResult(false); };
        await Assert.ThrowsAsync<JsonSerializationException>(() => Run(() => ServiceClient.Call<ISerializationService>(RetryOptions(), service => service.Send(new CircularRequest()), host.ServiceUrl)));
        Assert.Equal(0, attempts);
    }

    [Fact]
    public async Task Failure_response_remains_a_response_and_is_not_retried()
    {
        var request = Request();
        CalculationResponse result = null;
        await Run(() => ServiceClient.Call<ITestService>(RetryOptions(), service => result = service.Fail(request), host.ServiceUrl));
        Assert.NotNull(result);
        Assert.False(result.Success);
        Assert.False(string.IsNullOrEmpty(result.FailureInformation));
        Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Fact]
    public async Task File_bytes_and_headers_survive_the_strict_transport()
    {
        var request = Request();
        CODE.Framework.Services.Contracts.FileResponse result = null;
        await Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service => result = service.Download(request), host.ServiceUrl));
        Assert.Equal(request.RequestId.ToByteArray(), result.FileBytes);
        Assert.Equal("receipt.bin", result.FileName);
        Assert.Equal("application/octet-stream", result.ContentType);
    }

    [Fact]
    public async Task Cancellation_after_transient_failure_prevents_delayed_retry_and_is_never_suppressed()
    {
        using var cancellation = new CancellationTokenSource();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        host.InterceptRequest = context =>
        {
            Interlocked.Increment(ref attempts);
            context.Response.StatusCode = 503;
            received.TrySetResult();
            return Task.FromResult(true);
        };
        var options = RetryOptions();
        options.CancellationToken = cancellation.Token;
        options.RetryDelay = TimeSpan.FromSeconds(30);
        options.ExceptionBehavior = ServiceExceptionBehavior.Suppress;
        var call = Task.Run(() => ServiceClient.Call<ITestService>(options, service => service.Calculate(Request()), host.ServiceUrl));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public void Precancelled_call_does_not_run_callback()
    {
        var callbacks = 0;
        Assert.ThrowsAny<OperationCanceledException>(() => ServiceClient.Call<ITestService>(new ServiceCallOptions { CancellationToken = new CancellationToken(true) }, _ => callbacks++, host.ServiceUrl));
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public void Invalid_options_are_rejected_before_callback()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClient.Call<ITestService>(new ServiceCallOptions { MaxAttempts = 0 }, _ => throw new Exception("Must not run"), host.ServiceUrl));
        Assert.Throws<ArgumentOutOfRangeException>(() => ServiceClient.Call<ITestService>(new ServiceCallOptions { RetryDelay = TimeSpan.FromMilliseconds(-1) }, _ => throw new Exception("Must not run"), host.ServiceUrl));
    }

    [Fact]
    public async Task Cancellation_aborts_an_outstanding_http_request()
    {
        using var cancellation = new CancellationTokenSource();
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = 0;
        host.InterceptRequest = async context =>
        {
            Interlocked.Increment(ref attempts);
            received.TrySetResult();
            try { await Task.Delay(Timeout.Infinite, context.RequestAborted); }
            catch (OperationCanceledException) { disconnected.TrySetResult(); }
            return true;
        };
        var options = RetryOptions();
        options.CancellationToken = cancellation.Token;
        var call = Task.Run(() => ServiceClient.Call<ITestService>(options, service => service.Calculate(Request()), host.ServiceUrl));
        try
        {
            await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
            await disconnected.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, attempts);
        }
        finally
        {
            cancellation.Cancel();
        }
    }

    [Fact]
    public async Task Options_are_snapshotted_before_callback_runs()
    {
        var attempts = 0;
        host.InterceptRequest = context => { Interlocked.Increment(ref attempts); context.Response.StatusCode = 503; return Task.FromResult(true); };
        var options = new ServiceCallOptions();
        await Assert.ThrowsAsync<ServiceTransportException>(() => Run(() => ServiceClient.Call<ITestService>(options, service =>
        {
            options.MaxAttempts = 10;
            options.CanRetryOperation = _ => true;
            options.ExceptionBehavior = ServiceExceptionBehavior.Suppress;
            service.Calculate(Request());
        }, host.ServiceUrl)));
        Assert.Equal(1, attempts);
    }

    [Theory]
    [InlineData("Lookup")]
    [InlineData("Replace")]
    [InlineData("Remove")]
    public async Task Strict_transport_preserves_verbs_and_message_properties(string operation)
    {
        var request = Request();
        request.Text = "Maui café & tea + / ?";
        CalculationResponse response = null;
        await Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service =>
        {
            response = operation switch
            {
                "Lookup" => service.Lookup(request),
                "Replace" => service.Replace(request),
                _ => service.Remove(request)
            };
        }, host.ServiceUrl));
        Assert.Equal(42, response.Total);
        Assert.Equal(request.Text, response.Text);
        Assert.Equal(host.Log.ServerId, response.ServerId);
        Assert.Equal(new Execution(request.RequestId, 17, 25, request.Text, operation), Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId));
    }

    public void Dispose()
    {
        host.InterceptRequest = null;
        ServiceClient.AutoRetryFailedCalls = _legacyRetry;
        ServiceClient.AutoRetryFailedCallsForExceptions = _legacyFilter;
        ServiceClient.AutoRetryDelay = _legacyDelay;
    }

    private static Task Run(Action action) => Task.Run(action).WaitAsync(TimeSpan.FromSeconds(15));
    private static CalculationRequest Request() => new() { RequestId = Guid.NewGuid(), Left = 17, Right = 25, Text = "retry café" };
    private static ServiceCallOptions RetryOptions() => new() { MaxAttempts = 3, RetryDelay = TimeSpan.Zero, CanRetryOperation = _ => true };

    public interface ISerializationService
    {
        CalculationResponse Send(CircularRequest request);
    }

    [DataContract]
    public class CircularRequest
    {
        [DataMember]
        public string Name { get; set; } = "cycle";

        [DataMember]
        public CircularRequest Self => this;
    }
}
