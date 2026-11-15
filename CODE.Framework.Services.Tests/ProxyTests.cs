using System.Reflection;
using CODE.Framework.Fundamentals.Utilities;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class ProxyTests
{
    [Fact]
    public void Cached_type_creates_independent_instances_with_the_supplied_handlers()
    {
        TransparentProxyGenerator.ClearProxyCache(typeof(IProxyContract));
        var first = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = 1 });
        var second = (IProxyContract)TransparentProxyGenerator.GetProxy(typeof(IProxyContract), new RecordingHandler { Result = 2 });
        Assert.NotSame(first, second);
        Assert.Same(first.GetType(), second.GetType());
        Assert.Equal(1, first.Value(0, 0));
        Assert.Equal(2, second.Value(0, 0));
    }

    [Fact]
    public async Task Concurrent_first_use_reuses_one_type_and_keeps_handlers_separate()
    {
        TransparentProxyGenerator.ClearProxyCache(typeof(IProxyContract));
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = Enumerable.Range(0, 16).Select(async index =>
        {
            await start.Task;
            return TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = index });
        }).ToArray();
        start.SetResult();
        var proxies = await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(proxies.Select(proxy => proxy.GetType()).Distinct());
        for (var index = 0; index < proxies.Length; index++)
            Assert.Equal(index, proxies[index].Value(0, 0));
    }

    [Fact]
    public void Bypassing_cache_does_not_replace_the_cached_type()
    {
        var cached = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler());
        var uncached = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler(), useProxyCache: false);
        var next = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler());
        Assert.NotSame(cached.GetType(), uncached.GetType());
        Assert.Same(cached.GetType(), next.GetType());
    }

    [Fact]
    public void Clearing_type_cache_preserves_existing_instances()
    {
        var first = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = 1 });
        TransparentProxyGenerator.ClearProxyCache(typeof(IProxyContract));
        var second = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = 2 });
        Assert.NotSame(first.GetType(), second.GetType());
        Assert.Equal(1, first.Value(0, 0));
        Assert.Equal(2, second.Value(0, 0));
    }

    [Fact]
    public void Clearing_service_settings_does_not_regenerate_proxy_types()
    {
        var first = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler());
        CODE.Framework.Services.Client.ServiceClient.ClearSettingsCache();
        var second = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler());
        Assert.NotSame(first, second);
        Assert.Same(first.GetType(), second.GetType());
    }

    [Fact]
    public void Reference_return_dispatches_exact_method_and_request()
    {
        var request = new CalculationRequest { RequestId = Guid.NewGuid() };
        var response = new CalculationResponse { RequestId = request.RequestId };
        var handler = new RecordingHandler { Result = response };
        var proxy = TransparentProxyGenerator.GetProxy<IProxyContract>(handler, useProxyCache: false);
        Assert.Same(response, proxy.Reference(request));
        Assert.Equal(typeof(IProxyContract).GetMethod(nameof(IProxyContract.Reference)), handler.Method);
        Assert.Same(request, Assert.Single(handler.Arguments));
    }

    [Fact]
    public void Value_parameters_are_boxed_and_value_return_is_unboxed()
    {
        var handler = new RecordingHandler { Result = 42 };
        var proxy = TransparentProxyGenerator.GetProxy<IProxyContract>(handler, useProxyCache: false);
        Assert.Equal(42, proxy.Value(17, 25));
        Assert.Equal(new object[] { 17, 25 }, handler.Arguments);
    }

    [Fact]
    public void Void_method_discards_handler_result()
    {
        var handler = new RecordingHandler { Result = new object() };
        var proxy = TransparentProxyGenerator.GetProxy<IProxyContract>(handler, useProxyCache: false);
        proxy.Notify();
        Assert.Equal(typeof(IProxyContract).GetMethod(nameof(IProxyContract.Notify)), handler.Method);
        Assert.Empty(handler.Arguments);
    }

    [Fact]
    public async Task Task_return_preserves_pending_task_and_eventual_result()
    {
        var completion = new TaskCompletionSource<CalculationResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new RecordingHandler { Result = completion.Task };
        var proxy = TransparentProxyGenerator.GetProxy<IProxyContract>(handler, useProxyCache: false);
        var pending = proxy.Deferred(new CalculationRequest());
        Assert.Same(completion.Task, pending);
        Assert.False(pending.IsCompleted);
        var response = new CalculationResponse { Total = 42 };
        completion.SetResult(response);
        Assert.Same(response, await pending.WaitAsync(TimeSpan.FromSeconds(5)));
    }

    [Fact]
    public void Uncached_instances_dispatch_to_their_own_handlers()
    {
        var first = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = 1 }, useProxyCache: false);
        var second = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Result = 2 }, useProxyCache: false);
        Assert.Equal(1, first.Value(0, 0));
        Assert.Equal(2, second.Value(0, 0));
    }

    [Fact]
    public void Handler_exception_reaches_caller()
    {
        var error = new InvalidOperationException("Handler failed");
        var proxy = TransparentProxyGenerator.GetProxy<IProxyContract>(new RecordingHandler { Error = error }, useProxyCache: false);
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => proxy.Notify()));
    }

    public interface IProxyContract
    {
        CalculationResponse Reference(CalculationRequest request);
        int Value(int left, int right);
        void Notify();
        Task<CalculationResponse> Deferred(CalculationRequest request);
    }

    private class RecordingHandler : IProxyHandler
    {
        public object Result { get; init; }
        public Exception Error { get; init; }
        public MethodInfo Method { get; private set; }
        public object[] Arguments { get; private set; }

        public object OnMethod(MethodInfo method, object[] args)
        {
            Method = method;
            Arguments = args;
            if (Error != null) throw Error;
            return Result;
        }
    }
}
