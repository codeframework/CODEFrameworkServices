using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace CODE.Framework.Services.Client;

internal class StrictRestProxyHandler(Uri serviceUri, ServiceCallOptions options, bool asyncOperations = false) : IProxyHandler
{
    // Cookie state must not leak between unrelated calls sharing this client.
    private static readonly HttpClient Client = new(new HttpClientHandler { UseCookies = false });
    private static readonly HttpClient KeyedClient = new(new HttpClientHandler { UseCookies = false, AllowAutoRedirect = false });

    public object OnMethod(MethodInfo method, object[] args)
    {
        if (args.Length != 1) throw new ArgumentException("REST operations require exactly one request message.");
        if (asyncOperations)
        {
            if (!method.ReturnType.IsGenericType || method.ReturnType.GetGenericTypeDefinition() != typeof(Task<>))
                throw new NotSupportedException("CallAsync requires contract operations returning Task<TResponse>.");
            return typeof(StrictRestProxyHandler).GetMethod(nameof(InvokeTypedAsync), BindingFlags.NonPublic | BindingFlags.Instance)
                .MakeGenericMethod(method.ReturnType.GetGenericArguments()[0]).Invoke(this, new object[] { method, args[0] });
        }
        if (typeof(Task).IsAssignableFrom(method.ReturnType)) throw new NotSupportedException("The options-based synchronous client requires synchronous contract methods.");
        return InvokeLoggedAsync(method, args[0], method.ReturnType).GetAwaiter().GetResult();
    }

    private async Task<TResponse> InvokeTypedAsync<TResponse>(MethodInfo method, object request)
        => (TResponse)await InvokeLoggedAsync(method, request, typeof(TResponse)).ConfigureAwait(false);

    private async Task<object> InvokeLoggedAsync(MethodInfo method, object request, Type responseType)
    {
        try
        {
            return await InvokeAsync(method, request, responseType).ConfigureAwait(false);
        }
        catch (ServiceTransportException exception)
        {
            if (ServiceClient.LogCommunicationErrors) LoggingMediator.Log($"Unable to communicate with REST operation {method.Name} at {serviceUri}.", exception);
            throw;
        }
    }

    private async Task<object> InvokeAsync(MethodInfo method, object request, Type responseType)
    {
        var token = options.CancellationToken;
        token.ThrowIfCancellationRequested();
        var httpMethod = RestHelper.GetHttpMethodFromContract(method.Name, method.DeclaringType);
        var operationName = RestHelper.GetExposedMethodNameFromContract(method.Name, httpMethod, method.DeclaringType);
        var url = serviceUri.AbsoluteUri.TrimEnd('/') + "/" + operationName + RestHelper.SerializeToUrlParameters(request, httpMethod);
        // Serialize once, before any attempt. Every retry sends the same request bytes.
        var payload = httpMethod == "GET" ? null : JsonConvert.SerializeObject(request);
        var repeatable = options.MaxAttempts > 1 && options.CanRetryOperation?.Invoke(method) == true;
        for (var attempt = 1; ; attempt++)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var message = new HttpRequestMessage(new HttpMethod(httpMethod), url);
                if (!string.IsNullOrEmpty(options.ApiKey)) message.Headers.Add(options.ApiKeyHeaderName, options.ApiKey);
                if (payload != null) message.Content = new StringContent(payload, Encoding.UTF8, "application/json");
                var client = string.IsNullOrEmpty(options.ApiKey) ? Client : KeyedClient;
                using var response = await client.SendAsync(message, HttpCompletionOption.ResponseContentRead, token).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    try { response.EnsureSuccessStatusCode(); }
                    catch (HttpRequestException exception)
                    {
                        throw new ServiceTransportException($"REST operation {method.Name} returned HTTP {(int)response.StatusCode}.", response.StatusCode, exception);
                    }
                }
                token.ThrowIfCancellationRequested();
                if (responseType == typeof(FileResponse))
                    return new FileResponse
                    {
                        FileBytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false),
                        FileName = (response.Content.Headers.ContentDisposition?.FileNameStar ?? response.Content.Headers.ContentDisposition?.FileName)?.Trim('"') ?? string.Empty,
                        ContentType = response.Content.Headers.ContentType?.ToString() ?? "application/x-binary"
                    };
                var json = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                return JsonConvert.DeserializeObject(json, responseType) ?? throw new JsonSerializationException("The service returned no response message.");
            }
            catch (ServiceTransportException exception) when (repeatable && attempt < options.MaxAttempts && IsTransient(exception.StatusCode))
            {
                await Task.Delay(options.RetryDelay, token).ConfigureAwait(false);
            }
            catch (HttpRequestException exception)
            {
                token.ThrowIfCancellationRequested();
                if (!repeatable || attempt >= options.MaxAttempts || !IsTransient(exception))
                    throw new ServiceTransportException($"Unable to communicate with REST operation {method.Name}.", null, exception);
                await Task.Delay(options.RetryDelay, token).ConfigureAwait(false);
            }
        }
    }

    private static bool IsTransient(HttpStatusCode? status) => status == HttpStatusCode.RequestTimeout || (int?)status == 429 || status == HttpStatusCode.BadGateway || status == HttpStatusCode.ServiceUnavailable || status == HttpStatusCode.GatewayTimeout;

    private static bool IsTransient(Exception exception)
    {
        for (var current = exception; current != null; current = current.InnerException)
            if (current is SocketException socket)
                return socket.SocketErrorCode == SocketError.ConnectionReset || socket.SocketErrorCode == SocketError.ConnectionAborted || socket.SocketErrorCode == SocketError.ConnectionRefused || socket.SocketErrorCode == SocketError.TimedOut || socket.SocketErrorCode == SocketError.TryAgain;
        return false;
    }
}
