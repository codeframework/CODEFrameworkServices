using CODE.Framework.Fundamentals.Configuration;
using CODE.Framework.Services.Client;
using CODE.Framework.Services.Contracts;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class AsyncServiceTests(HttpServiceFixture host) : IClassFixture<HttpServiceFixture>, IDisposable
{
    private DictionarySettings _source;
    private AsyncServiceState _state;
    private readonly List<AsyncInvocation> _invocations = [];

    private void Configure(bool local)
    {
        _state = local ? AsyncTestService.LocalState : host.AsyncState;
        _source = new DictionarySettings(new Dictionary<string, string>
        {
            ["ServiceProtocol:IAsyncTestService"] = local ? "InProcess" : "Rest",
            ["RestServiceUrl:IAsyncTestService"] = host.ServiceUrl.Replace("/test", "/async")
        });
        ConfigurationSettings.Sources.Insert(0, _source);
        ServiceClient.ClearSettingsCache();
        if (local && ServiceGardenLocal.GetService<IAsyncTestService>() == null)
            ServiceGardenLocal.AddServiceHost(typeof(AsyncTestService));
    }

    private (CalculationRequest Request, AsyncInvocation Invocation) Prepare(string text = "async")
    {
        var request = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 17, Right = 25, Text = text };
        var invocation = new AsyncInvocation();
        _state.Invocations[request.RequestId] = invocation;
        _invocations.Add(invocation);
        return (request, invocation);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Call_returns_pending_task_until_actual_service_is_released(bool local, bool forwarded)
    {
        Configure(local);
        var (request, invocation) = Prepare();
        var call = ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => forwarded ? service.Forward(request) : service.Delayed(request));
        await invocation.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(call.IsCompleted);
        invocation.Release.SetResult();
        var response = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(42, response.Total);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(_state.ServerId, response.ServerId);
        Assert.Equal(1, invocation.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Callback_is_awaited_through_its_own_asynchronous_work(bool local)
    {
        Configure(local);
        var (request, invocation) = Prepare();
        invocation.Release.SetResult();
        var callbackReceived = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var callbackRelease = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var error = new InvalidOperationException("Callback failure after await");
        var call = ServiceClient.CallAsync<IAsyncTestService>(async service =>
        {
            await service.Delayed(request);
            callbackReceived.SetResult();
            await callbackRelease.Task;
            throw error;
        });
        await callbackReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(call.IsCompleted);
        callbackRelease.SetResult();
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => call.WaitAsync(TimeSpan.FromSeconds(5))));
        Assert.Equal(1, invocation.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standard_handling_converts_failure_after_await_to_response(bool local)
    {
        Configure(local);
        var (request, invocation) = Prepare("throw");
        var call = ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.Handled(request));
        await invocation.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        invocation.Release.SetResult();
        var response = await call.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.FailureInformation));
        Assert.Equal(1, invocation.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Standard_handling_also_handles_throw_before_a_task_is_returned(bool local)
    {
        Configure(local);
        var response = await ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.ImmediateThrow(new CalculationRequest())).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(response.Success);
        Assert.False(string.IsNullOrEmpty(response.FailureInformation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unhandled_service_fault_is_observed_after_await(bool local)
    {
        Configure(local);
        var (request, invocation) = Prepare("throw");
        var call = ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.Delayed(request));
        await invocation.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        invocation.Release.SetResult();
        if (local)
            Assert.Equal("Failure after await", (await Assert.ThrowsAsync<InvalidOperationException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)))).Message);
        else
            Assert.Equal(System.Net.HttpStatusCode.InternalServerError, (await Assert.ThrowsAsync<ServiceTransportException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)))).StatusCode);
        Assert.Equal(1, invocation.Executions);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Task_file_response_preserves_bytes_and_metadata(bool local)
    {
        Configure(local);
        var (request, invocation) = Prepare();
        invocation.Release.SetResult();
        var response = await ServiceClient.CallAsync<IAsyncTestService, FileResponse>(service => service.File(request)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(request.RequestId.ToByteArray(), response.FileBytes);
        Assert.Equal("async.bin", response.FileName);
        Assert.Equal("application/octet-stream", response.ContentType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_stops_waiting_without_replaying_service(bool local)
    {
        Configure(local);
        using var cancellation = new CancellationTokenSource();
        var (request, invocation) = Prepare();
        var options = new ServiceCallOptions { CancellationToken = cancellation.Token, ExceptionBehavior = ServiceExceptionBehavior.Suppress };
        var call = ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(options, service => service.Delayed(request));
        await invocation.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(call.IsCompleted);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, invocation.Executions);
        invocation.Release.SetResult(); // Cancellation cannot forcibly terminate service work.
    }

    [Fact]
    public async Task Inprocess_service_cancellation_is_not_converted_to_failure_response()
    {
        Configure(true);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.Canceled(new CalculationRequest())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Concurrent_pending_calls_keep_request_results_separate(bool local)
    {
        Configure(local);
        var inputs = Enumerable.Range(0, 8).Select(_ => Prepare()).ToArray();
        var calls = inputs.Select(input => ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.Forward(input.Request))).ToArray();
        await Task.WhenAll(inputs.Select(input => input.Invocation.Received.Task)).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.All(calls, call => Assert.False(call.IsCompleted));
        foreach (var input in inputs.Reverse()) input.Invocation.Release.SetResult();
        var responses = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(inputs.Select(input => input.Request.RequestId), responses.Select(response => response.RequestId));
        Assert.All(inputs, input => Assert.Equal(1, input.Invocation.Executions));
    }

    [Fact]
    public async Task Async_retry_does_not_replay_callback_or_prior_operation()
    {
        Configure(false);
        var (request, invocation) = Prepare();
        invocation.Release.SetResult();
        var attempts = 0;
        var callbacks = 0;
        host.InterceptRequest = context =>
        {
            if (context.Request.Path.Value.Contains("/Forward/") && Interlocked.Increment(ref attempts) == 1)
            {
                context.Response.StatusCode = 503;
                return Task.FromResult(true);
            }
            return Task.FromResult(false);
        };
        var options = new ServiceCallOptions { MaxAttempts = 3, RetryDelay = TimeSpan.FromMilliseconds(10), CanRetryOperation = method => method.Name == "Forward" };
        await ServiceClient.CallAsync<IAsyncTestService>(options, async service =>
        {
            callbacks++;
            await service.Delayed(request);
            await service.Forward(request);
        }).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(1, callbacks);
        Assert.Equal(2, attempts);
        Assert.Equal(2, invocation.Executions);
    }

    [Fact]
    public async Task Async_deserialization_failure_is_not_retried()
    {
        Configure(false);
        var attempts = 0;
        host.InterceptRequest = async context => { Interlocked.Increment(ref attempts); await context.Response.WriteAsync("not JSON"); return true; };
        await Assert.ThrowsAnyAsync<JsonException>(() => ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(new ServiceCallOptions { MaxAttempts = 3, CanRetryOperation = _ => true }, service => service.Forward(new CalculationRequest())));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Explicit_suppression_returns_default_and_precancellation_still_propagates()
    {
        Configure(true);
        var (request, invocation) = Prepare("throw");
        invocation.Release.SetResult();
        var options = new ServiceCallOptions { ExceptionBehavior = ServiceExceptionBehavior.Suppress };
        Assert.Null(await ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(options, service => service.Delayed(request)));
        options.CancellationToken = new CancellationToken(true);
        var callbacks = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ServiceClient.CallAsync<IAsyncTestService>(options, _ => { callbacks++; return Task.CompletedTask; }));
        Assert.Equal(0, callbacks);
    }

    [Fact]
    public async Task Async_api_rejects_sync_operations_instead_of_blocking()
    {
        await Assert.ThrowsAsync<NotSupportedException>(() => ServiceClient.CallAsync<ITestService>(service => { service.Calculate(new CalculationRequest()); return Task.CompletedTask; }, host.ServiceUrl));
    }

    [Fact]
    public async Task Invalid_arguments_and_options_fault_the_returned_task()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(() => ServiceClient.CallAsync<IAsyncTestService>((Func<IAsyncTestService, Task>)null));
        await Assert.ThrowsAsync<ArgumentNullException>(() => ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>((Func<IAsyncTestService, Task<CalculationResponse>>)null));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => ServiceClient.CallAsync<IAsyncTestService>(new ServiceCallOptions { MaxAttempts = 0 }, _ => Task.CompletedTask));
    }

    [Fact]
    public async Task Nonresult_overload_explicitly_suppresses_async_callback_failure()
    {
        Configure(true);
        var (request, invocation) = Prepare();
        invocation.Release.SetResult();
        var callbacks = 0;
        await ServiceClient.CallAsync<IAsyncTestService>(new ServiceCallOptions { ExceptionBehavior = ServiceExceptionBehavior.Suppress }, async service =>
        {
            callbacks++;
            await service.Delayed(request);
            throw new InvalidOperationException("Suppressed by request");
        });
        Assert.Equal(1, callbacks);
        Assert.Equal(1, invocation.Executions);
    }

    [Fact]
    public async Task Callback_continuations_preserve_the_callers_synchronization_context()
    {
        Configure(false);
        var (request, invocation) = Prepare();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            using var context = new PumpContext();
            SynchronizationContext.SetSynchronizationContext(context);
            try
            {
                var call = ServiceClient.CallAsync<IAsyncTestService>(async service =>
                {
                    Assert.Same(context, SynchronizationContext.Current);
                    await service.Forward(request);
                    Assert.Same(context, SynchronizationContext.Current);
                });
                context.RunUntil(call);
                finished.SetResult();
            }
            catch (Exception error) { finished.TrySetException(error); }
            finally { SynchronizationContext.SetSynchronizationContext(null); }
        }) { IsBackground = true };
        thread.Start();
        await invocation.Received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(finished.Task.IsCompleted);
        invocation.Release.SetResult();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Cancellation_prevents_a_delayed_async_retry()
    {
        Configure(false);
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
        var options = new ServiceCallOptions { MaxAttempts = 3, CanRetryOperation = _ => true, RetryDelay = TimeSpan.FromSeconds(30), CancellationToken = cancellation.Token };
        var call = ServiceClient.CallAsync<IAsyncTestService>(options, service => service.Forward(new CalculationRequest()));
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task Exhausted_async_retries_report_final_transport_failure()
    {
        Configure(false);
        var attempts = 0;
        host.InterceptRequest = context => { Interlocked.Increment(ref attempts); context.Response.StatusCode = 503; return Task.FromResult(true); };
        var options = new ServiceCallOptions { MaxAttempts = 2, CanRetryOperation = _ => true, RetryDelay = TimeSpan.Zero };
        var error = await Assert.ThrowsAsync<ServiceTransportException>(() => ServiceClient.CallAsync<IAsyncTestService>(options, service => service.Forward(new CalculationRequest())));
        Assert.Equal(System.Net.HttpStatusCode.ServiceUnavailable, error.StatusCode);
        Assert.Equal(2, attempts);
    }

    private class PumpContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object State)> _work = new();

        public override void Post(SendOrPostCallback callback, object state) => _work.Add((callback, state));

        public void RunUntil(Task task)
        {
            _ = task.ContinueWith(_ => _work.CompleteAdding(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            foreach (var item in _work.GetConsumingEnumerable()) item.Callback(item.State);
            task.GetAwaiter().GetResult();
        }

        public void Dispose() => _work.Dispose();
    }

    public void Dispose()
    {
        foreach (var invocation in _invocations) invocation.Release.TrySetResult();
        host.InterceptRequest = null;
        if (_source != null) ConfigurationSettings.Sources.Remove(_source);
        ServiceClient.ClearSettingsCache();
    }
}
