using System.Collections.Concurrent;
using CODE.Framework.Services.Contracts;

namespace CODE.Framework.Services.Tests;

public interface IAsyncTestService
{
    Task<CalculationResponse> Delayed(CalculationRequest request);
    Task<CalculationResponse> Forward(CalculationRequest request);
    [StandardExceptionHandling]
    Task<CalculationResponse> Handled(CalculationRequest request);
    [StandardExceptionHandling]
    Task<CalculationResponse> ImmediateThrow(CalculationRequest request);
    Task<FileResponse> File(CalculationRequest request);
    [StandardExceptionHandling]
    Task<CalculationResponse> Canceled(CalculationRequest request);
}

public class AsyncInvocation
{
    public TaskCompletionSource Received { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Executions;
}

public class AsyncServiceState
{
    public Guid ServerId { get; } = Guid.NewGuid();
    public ConcurrentDictionary<Guid, AsyncInvocation> Invocations { get; } = new();
}

public class AsyncTestService : IAsyncTestService
{
    public static AsyncServiceState LocalState { get; } = new();
    private readonly AsyncServiceState _state;

    public AsyncTestService() : this(LocalState) { }

    public AsyncTestService(AsyncServiceState state) => _state = state;

    public async Task<CalculationResponse> Delayed(CalculationRequest request) => await Execute(request);

    // Deliberately no async keyword: hosting must inspect the return type.
    public Task<CalculationResponse> Forward(CalculationRequest request) => Execute(request);

    public Task<CalculationResponse> Handled(CalculationRequest request) => Execute(request);

    public Task<CalculationResponse> ImmediateThrow(CalculationRequest request) => throw new InvalidOperationException("Immediate failure");

    public Task<CalculationResponse> Canceled(CalculationRequest request) => Task.FromCanceled<CalculationResponse>(new CancellationToken(true));

    public async Task<FileResponse> File(CalculationRequest request)
    {
        await Execute(request);
        return new FileResponse { FileBytes = request.RequestId.ToByteArray(), FileName = "async.bin", ContentType = "application/octet-stream" };
    }

    private async Task<CalculationResponse> Execute(CalculationRequest request)
    {
        var invocation = _state.Invocations[request.RequestId];
        Interlocked.Increment(ref invocation.Executions);
        invocation.Received.TrySetResult();
        await invocation.Release.Task;
        if (request.Text == "throw") throw new InvalidOperationException("Failure after await");
        return new CalculationResponse { RequestId = request.RequestId, ServerId = _state.ServerId, Total = request.Left + request.Right, Text = request.Text, Success = true };
    }
}
