namespace CODE.Framework.Services.Client;

/// <summary>Options for the opt-in client overload. Values are copied at the start of a call.</summary>
public class ServiceCallOptions
{
    /// <summary>Controls final failures. Cancellation always propagates.</summary>
    public ServiceExceptionBehavior ExceptionBehavior { get; set; } = ServiceExceptionBehavior.Propagate;

    /// <summary>Maximum attempts per REST operation, including the original attempt. Default is one (no retries).</summary>
    public int MaxAttempts { get; set; } = 1;

    /// <summary>Delay between eligible retries. Default is 200 milliseconds.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(200);

    /// <summary>Explicitly identifies operations safe to repeat. Without this predicate, no operations are retried.</summary>
    /// <remarks>This is an idempotency decision, not an exception filter. A lost response may follow successful server execution. A true result permits retrying only the client's narrowly classified transient transport failures.</remarks>
    public Func<MethodInfo, bool> CanRetryOperation { get; set; }

    /// <summary>Cancels REST I/O and retry delays. For in-process calls, checks before invocation and cancels waiting for task results; service execution itself is not forcibly interrupted. Does not interrupt application callback code.</summary>
    public CancellationToken CancellationToken { get; set; }

    internal ServiceCallOptions Snapshot()
    {
        if (!Enum.IsDefined(typeof(ServiceExceptionBehavior), ExceptionBehavior)) throw new ArgumentOutOfRangeException(nameof(ExceptionBehavior));
        if (MaxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(MaxAttempts));
        if (RetryDelay < TimeSpan.Zero || RetryDelay.TotalMilliseconds > int.MaxValue) throw new ArgumentOutOfRangeException(nameof(RetryDelay));
        return new ServiceCallOptions
        {
            ExceptionBehavior = ExceptionBehavior,
            MaxAttempts = MaxAttempts,
            RetryDelay = RetryDelay,
            CanRetryOperation = CanRetryOperation,
            CancellationToken = CancellationToken
        };
    }
}

public enum ServiceExceptionBehavior
{
    Propagate,
    Suppress
}
