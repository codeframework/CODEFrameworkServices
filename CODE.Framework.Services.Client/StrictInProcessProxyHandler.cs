using System.Runtime.ExceptionServices;
using System.Threading.Tasks;

namespace CODE.Framework.Services.Client;

internal class StrictInProcessProxyHandler(object service, CancellationToken cancellationToken) : IProxyHandler
{
    public object OnMethod(MethodInfo method, object[] args)
    {
        cancellationToken.ThrowIfCancellationRequested();
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
}
