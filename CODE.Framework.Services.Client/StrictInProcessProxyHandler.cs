using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CODE.Framework.Services.Client;

internal class StrictInProcessProxyHandler(object service, CancellationToken cancellationToken, bool asyncOperations = false) : IProxyHandler
{
    public object OnMethod(MethodInfo method, object[] args)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (asyncOperations)
        {
            if (!method.ReturnType.IsGenericType || method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>))
                throw new NotSupportedException("CallAsync requires contract operations returning Task<TResponse>.");
            return typeof(StrictInProcessProxyHandler).GetMethod(nameof(InvokeAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(this, new object[] { method, args });
        }
        if (typeof(Task).IsAssignableFrom(method.ReturnType)) throw new NotSupportedException("The options-based synchronous client requires synchronous contract methods.");
        try
        {
            // Use the registered proxy to preserve configured standard failure-response handling.
            return method.Invoke(service, args);
        }
        catch (TargetInvocationException exception)
        {
            Exception error = exception;
            while (error is TargetInvocationException invocation && invocation.InnerException != null)
                error = invocation.InnerException;
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
    }

    private async Task<TResponse> InvokeAsync<TResponse>(MethodInfo method, object[] args)
    {
        Task<TResponse> task;
        try { task = (Task<TResponse>)method.Invoke(service, args); }
        catch (TargetInvocationException exception)
        {
            Exception error = exception;
            while (error is TargetInvocationException invocation && invocation.InnerException != null) error = invocation.InnerException;
            ExceptionDispatchInfo.Capture(error).Throw();
            throw;
        }
        if (!cancellationToken.CanBeCanceled) return await task.ConfigureAwait(false);
        var canceled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        using (cancellationToken.Register(() => canceled.TrySetResult(true)))
        {
            if (await Task.WhenAny(task, canceled.Task).ConfigureAwait(false) != task)
            {
                // Cancellation stops waiting, not service execution. Observe a later fault.
                _ = task.ContinueWith(completed => { _ = completed.Exception; }, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                cancellationToken.ThrowIfCancellationRequested();
            }
            return await task.ConfigureAwait(false);
        }
    }
}
