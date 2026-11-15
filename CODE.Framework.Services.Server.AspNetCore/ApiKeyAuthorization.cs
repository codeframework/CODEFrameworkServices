namespace CODE.Framework.Services.Server.AspNetCore;

internal static class ApiKeyAuthorization
{
    internal static async Task<bool> AuthorizeAsync(ServiceHandlerRequestContext context, Type contractType)
    {
        var configuration = context.ServiceInstanceConfiguration;
        if (configuration.ApiKeyHandler == null) return true;
        var request = context.HttpRequest;
        var response = context.HttpResponse;
        var values = request.Headers[configuration.ApiKeyHeaderName];
        if (values.Count > 1)
        {
            response.StatusCode = StatusCodes.Status400BadRequest;
            return false;
        }

        try
        {
            var token = context.HttpContext.RequestAborted;
            token.ThrowIfCancellationRequested();
            var accepted = await configuration.ApiKeyHandler.CheckApiKeyAsync(contractType, values.Count == 0 ? null : values[0], context, token);
            token.ThrowIfCancellationRequested();
            if (accepted) return true;
            response.StatusCode = StatusCodes.Status401Unauthorized;
            response.Headers.WWWAuthenticate = "ApiKey realm=\"services\"";
        }
        catch (OperationCanceledException) when (context.HttpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            // Validator failures are infrastructure failures, never application failure messages.
            response.StatusCode = StatusCodes.Status500InternalServerError;
        }
        return false;
    }
}
