using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CODE.Framework.Fundamentals.Configuration;
using CODE.Framework.Services.Client;
using CODE.Framework.Services.Contracts;
using CODE.Framework.Services.Server.AspNetCore;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class ApiKeyTests : IClassFixture<HttpServiceFixture>, IDisposable
{
    private readonly HttpServiceFixture _host;
    private readonly DictionarySettings _settings;
    private readonly bool _previousCacheSettings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly RecordingHandler _handler = new();

    public ApiKeyTests(HttpServiceFixture host)
    {
        _host = host;
        _previousCacheSettings = ServiceClient.CacheSettings;
        _settings = new DictionarySettings(new Dictionary<string, string>
        {
            ["ServiceProtocol:ITestService"] = "Rest",
            ["ServiceProtocol:IAsyncTestService"] = "Rest",
            ["RestServiceUrl:ITestService"] = host.ServiceUrl,
            ["RestServiceUrl:IAsyncTestService"] = host.ServiceUrl.Replace("/test", "/async"),
            ["RestServiceApiKey:ITestService"] = "valid",
            ["RestServiceApiKey:IAsyncTestService"] = "valid"
        });
        ConfigurationSettings.Sources.Insert(0, _settings);
        ServiceClient.CacheSettings = true;
        ServiceClient.ClearSettingsCache();
        host.TestConfiguration.ApiKeyHandler = _handler;
        host.AsyncConfiguration.ApiKeyHandler = _handler;
    }

    [Theory]
    [InlineData(null, false, HttpStatusCode.Unauthorized)]
    [InlineData("wrong", false, HttpStatusCode.Unauthorized)]
    [InlineData("valid", false, HttpStatusCode.OK)]
    [InlineData(null, true, HttpStatusCode.OK)]
    public async Task Http_validation_receives_missing_keys_and_context(string key, bool allowMissing, HttpStatusCode expected)
    {
        _handler.Validate = (_, value, _, _) => Task.FromResult(value == "valid" || (allowMissing && value == null));
        var request = Request();
        using var message = new HttpRequestMessage(HttpMethod.Post, _host.ServiceUrl + "/Calculate/" + request.RequestId)
        {
            Content = CreateJsonContent(request)
        };
        if (key != null) message.Headers.Add("x-api-key", key);
        message.Headers.Add("X-Correlation-Id", "audit");
        using var response = await _http.SendAsync(message);
        Assert.Equal(expected, response.StatusCode);
        var seen = Assert.Single(_handler.Seen);
        Assert.Equal(typeof(ITestService), seen.Contract);
        Assert.Equal(key, seen.Key);
        Assert.Equal("audit", seen.CorrelationId);
        Assert.Equal(nameof(ITestService.Calculate), seen.Context.MethodContext.MethodInfo.Name);
        Assert.Equal("POST", seen.Context.Url.HttpMethod);
        Assert.Same(_host.TestConfiguration, seen.Context.ServiceInstanceConfiguration);
        if (expected == HttpStatusCode.OK)
        {
            Assert.Equal(42, (await response.Content.ReadFromJsonAsync<CalculationResponse>()).Total);
            Assert.Single(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
        }
        else
        {
            Assert.Equal("ApiKey", Assert.Single(response.Headers.WwwAuthenticate).Scheme);
            Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
        }
    }

    [Fact]
    public async Task Duplicate_headers_are_rejected_without_validation_or_execution()
    {
        var request = Request();
        // HttpClient combines custom header arrays onto one line; send two actual fields instead.
        var uri = new Uri(_host.ServiceUrl + "/Calculate/" + request.RequestId);
        var json = JsonSerializer.Serialize(request);
        using var tcp = new TcpClient();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await tcp.ConnectAsync(uri.Host, uri.Port, timeout.Token);
        await using var stream = tcp.GetStream();
        var bytes = Encoding.UTF8.GetBytes($"POST {uri.PathAndQuery} HTTP/1.1\r\nHost: {uri.Authority}\r\nX-Api-Key: valid\r\nX-Api-Key: wrong\r\nContent-Type: application/json\r\nContent-Length: {Encoding.UTF8.GetByteCount(json)}\r\nConnection: close\r\n\r\n{json}");
        await stream.WriteAsync(bytes, timeout.Token);
        using var reader = new StreamReader(stream);
        Assert.StartsWith("HTTP/1.1 400", await reader.ReadLineAsync(timeout.Token));
        Assert.Empty(_handler.Seen);
        Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Fact]
    public async Task Validator_exceptions_are_server_errors_even_for_handled_operations()
    {
        _handler.Validate = (_, _, _, _) => throw new InvalidOperationException("private validator details");
        var request = Request();
        using var response = await _http.PostAsync(_host.ServiceUrl + "/Fail/" + request.RequestId, CreateJsonContent(request));
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.DoesNotContain("private", await response.Content.ReadAsStringAsync());
        Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Fact]
    public async Task Successful_key_still_runs_existing_authorization_before_service_hooks()
    {
        var order = new List<string>();
        _handler.Validate = (_, _, _, _) => { order.Add("key"); return Task.FromResult(true); };
        _host.TestConfiguration.OnAuthorize = context => { order.Add("authorize"); context.HttpContext.User = new System.Security.Claims.ClaimsPrincipal(); return Task.FromResult(true); };
        _host.TestConfiguration.OnBeforeMethodInvoke = _ => { order.Add("before"); return Task.CompletedTask; };
        var request = Request();
        using var response = await _http.PostAsync(_host.ServiceUrl + "/Calculate/" + request.RequestId, CreateJsonContent(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(new[] { "key", "authorize", "before" }, order);
        Assert.Single(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Fact]
    public async Task Configured_key_is_transparent_for_legacy_options_and_async_calls_including_files()
    {
        var request = Request();
        CalculationResponse legacy = null;
        FileResponse legacyFile = null;
        await Task.Run(() => ServiceClient.Call<ITestService>(service =>
        {
            legacy = service.Calculate(request);
            legacyFile = service.Download(request);
        })).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(42, legacy.Total);
        Assert.Equal(request.RequestId.ToByteArray(), legacyFile.FileBytes);
        CalculationResponse strict = null;
        FileResponse strictFile = null;
        await Task.Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service =>
        {
            strict = service.Lookup(request);
            strictFile = service.Download(request);
        })).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(42, strict.Total);
        Assert.Equal(request.RequestId.ToByteArray(), strictFile.FileBytes);
        var invocation = _host.AsyncState.Invocations.GetOrAdd(request.RequestId, _ => new AsyncInvocation());
        invocation.Release.TrySetResult();
        var asyncResponse = await ServiceClient.CallAsync<IAsyncTestService, CalculationResponse>(service => service.Delayed(request)).WaitAsync(TimeSpan.FromSeconds(15));
        var asyncFile = await ServiceClient.CallAsync<IAsyncTestService, FileResponse>(service => service.File(request)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(42, asyncResponse.Total);
        Assert.Equal(request.RequestId.ToByteArray(), asyncFile.FileBytes);
        Assert.Equal(6, _handler.Seen.Count);
        Assert.All(_handler.Seen, seen => Assert.Equal("valid", seen.Key));
        Assert.Equal(4, _host.Log.Entries.Count(entry => entry.RequestId == request.RequestId));
        Assert.Equal(2, invocation.Executions);
    }

    [Fact]
    public async Task Override_keys_are_isolated_across_concurrent_calls_and_unauthorized_is_not_retried()
    {
        var bothValidating = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Validate = async (_, key, _, token) =>
        {
            if (_handler.Seen.Count >= 2) bothValidating.TrySetResult();
            await bothValidating.Task.WaitAsync(TimeSpan.FromSeconds(10), token);
            return key == "valid";
        };
        var good = Request();
        var bad = Request();
        var options = new ServiceCallOptions { ApiKey = "wrong", MaxAttempts = 3, RetryDelay = TimeSpan.Zero, CanRetryOperation = _ => true };
        var badCall = Task.Run(() => Assert.Throws<ServiceTransportException>(() => ServiceClient.Call<ITestService>(options, service => service.Calculate(bad))));
        var goodCall = Task.Run(() =>
        {
            CalculationResponse result = null;
            ServiceClient.Call<ITestService>(new ServiceCallOptions { ApiKey = "valid" }, service => result = service.Calculate(good));
            return result;
        });
        await Task.WhenAll(badCall, goodCall).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(HttpStatusCode.Unauthorized, (await badCall).StatusCode);
        Assert.Equal(42, (await goodCall).Total);
        Assert.Equal(2, _handler.Seen.Count);
        Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == bad.RequestId);
        Assert.Single(_host.Log.Entries, entry => entry.RequestId == good.RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Key_configuration_refreshes_new_channels_and_preserves_existing_channels(bool clearAll)
    {
        var original = ServiceClient.GetChannelDedicated<ITestService>();
        _settings.Settings["RestServiceApiKey:ITestService"] = "rotated";
        var cached = ServiceClient.GetChannelDedicated<ITestService>();
        ServiceClient.ClearSettingsCache(clearAll ? null : "RestServiceApiKey:ITestService");
        var refreshed = ServiceClient.GetChannelDedicated<ITestService>();
        _handler.Validate = (_, _, _, _) => Task.FromResult(true);
        await Task.Run(() =>
        {
            original.Calculate(Request());
            cached.Calculate(Request());
            refreshed.Calculate(Request());
            ServiceClient.Call<ITestService>(new ServiceCallOptions(), service => service.Calculate(Request()));
        }).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(new[] { "valid", "valid", "rotated", "rotated" }, _handler.Seen.Select(seen => seen.Key));
    }

    [Fact]
    public async Task Custom_header_and_empty_override_work_without_leaking_key_to_unconfigured_calls()
    {
        _host.TestConfiguration.ApiKeyHeaderName = "Customer-Key";
        _settings.Settings["RestServiceApiKeyHeaderName:ITestService"] = "Customer-Key";
        await Task.Run(() => ServiceClient.Call<ITestService>(service => service.Calculate(Request()))).WaitAsync(TimeSpan.FromSeconds(15));
        await Task.Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service => service.Calculate(Request()))).WaitAsync(TimeSpan.FromSeconds(15));
        _handler.Validate = (_, _, _, _) => Task.FromResult(true);
        await Task.Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions { ApiKey = "", ApiKeyHeaderName = "Customer-Key" }, service => service.Calculate(Request()))).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(new[] { "valid", "valid", null }, _handler.Seen.Select(seen => seen.Key));
        _host.AsyncConfiguration.ApiKeyHandler = null;
        var request = Request();
        _host.AsyncState.Invocations.GetOrAdd(request.RequestId, _ => new AsyncInvocation()).Release.TrySetResult();
        string leakedKey = "unset";
        _host.InterceptRequest = context =>
        {
            leakedKey = context.Request.Headers["Customer-Key"].FirstOrDefault() ?? context.Request.Headers["X-Api-Key"].FirstOrDefault();
            return Task.FromResult(false);
        };
        await ServiceClient.CallAsync<IAsyncTestService>(new ServiceCallOptions { ApiKey = "" }, service => service.Delayed(request)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Null(leakedKey);
    }

    [Fact]
    public async Task Explicit_header_override_and_disabled_cache_use_current_credentials()
    {
        _host.TestConfiguration.ApiKeyHeaderName = "Customer-Key";
        ServiceClient.CacheSettings = false;
        _handler.Validate = (_, _, _, _) => Task.FromResult(true);
        await Task.Run(() => ServiceClient.Call<ITestService>(new ServiceCallOptions { ApiKey = "override", ApiKeyHeaderName = "Customer-Key" }, service => service.Calculate(Request()))).WaitAsync(TimeSpan.FromSeconds(15));
        _settings.Settings["RestServiceApiKeyHeaderName:ITestService"] = "Customer-Key";
        _settings.Settings["RestServiceApiKey:ITestService"] = "rotated";
        await Task.Run(() => ServiceClient.Call<ITestService>(service => service.Calculate(Request()))).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(new[] { "override", "rotated" }, _handler.Seen.Select(seen => seen.Key));
    }

    [Fact]
    public async Task Accepted_key_does_not_bypass_authorization_denial()
    {
        var authorized = false;
        var before = false;
        _host.TestConfiguration.OnAuthorize = _ => { authorized = true; return Task.FromResult(false); };
        _host.TestConfiguration.OnBeforeMethodInvoke = _ => { before = true; return Task.CompletedTask; };
        var request = Request();
        using var message = new HttpRequestMessage(HttpMethod.Post, _host.ServiceUrl + "/Calculate/" + request.RequestId) { Content = CreateJsonContent(request) };
        message.Headers.Add("X-Api-Key", "valid");
        using var response = await _http.SendAsync(message);
        Assert.False(response.IsSuccessStatusCode);
        Assert.True(authorized);
        Assert.False(before);
        Assert.Single(_handler.Seen);
        Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Key_bearing_requests_do_not_follow_redirects(bool legacy)
    {
        var destinationCalls = 0;
        _host.InterceptRequest = context =>
        {
            if (context.Request.Path == "/redirect-target") Interlocked.Increment(ref destinationCalls);
            context.Response.StatusCode = StatusCodes.Status302Found;
            context.Response.Headers.Location = _host.ServiceUrl.Replace("/test", "/redirect-target");
            return Task.FromResult(true);
        };
        await Task.Run(() =>
        {
            if (legacy)
                Assert.Throws<CommunicationException>(() => ServiceClient.GetChannelDedicated<ITestService>().Calculate(Request()));
            else
            {
                var exception = Assert.Throws<ServiceTransportException>(() => ServiceClient.Call<ITestService>(new ServiceCallOptions(), service => service.Calculate(Request())));
                Assert.Equal(HttpStatusCode.Found, exception.StatusCode);
            }
        }).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(0, destinationCalls);
        Assert.Empty(_handler.Seen);
    }

    [Fact]
    public async Task Validator_is_awaited_and_request_abort_reaches_handler()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _handler.Validate = async (_, _, _, token) =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { if (token.IsCancellationRequested) canceled.TrySetResult(); }
            return true;
        };
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var request = Request();
        var call = ServiceClient.CallAsync<IAsyncTestService>(new ServiceCallOptions { CancellationToken = cancellation.Token, ExceptionBehavior = ServiceExceptionBehavior.Suppress }, service => service.Delayed(request));
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            Assert.False(call.IsCompleted);
            Assert.False(_host.AsyncState.Invocations.ContainsKey(request.RequestId));
        }
        finally { cancellation.Cancel(); }
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(TimeSpan.FromSeconds(10)));
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Preflight_and_unprotected_services_remain_available()
    {
        using var message = new HttpRequestMessage(HttpMethod.Options, _host.ServiceUrl + "/Calculate/" + Guid.NewGuid());
        message.Headers.Add("Origin", "https://example.com");
        message.Headers.Add("Access-Control-Request-Method", "POST");
        message.Headers.Add("Access-Control-Request-Headers", "Content-Type,X-Api-Key");
        using var preflight = await _http.SendAsync(message);
        Assert.Equal(HttpStatusCode.NoContent, preflight.StatusCode);
        Assert.Contains("X-Api-Key", string.Join(",", preflight.Headers.GetValues("Access-Control-Allow-Headers")), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_handler.Seen);
        _host.TestConfiguration.ApiKeyHandler = null;
        var request = Request();
        using var response = await _http.PostAsync(_host.ServiceUrl + "/Calculate/" + request.RequestId, CreateJsonContent(request));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_handler.Seen);
        Assert.Single(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    [Fact]
    public async Task OpenApi_declares_only_protected_operations_and_custom_header_schemes()
    {
        _host.AsyncConfiguration.ApiKeyHandler = null;
        var url = _host.ServiceUrl.Replace("/test", "/openapi.json");
        using var document = JsonDocument.Parse(await _http.GetStringAsync(url));
        var schemes = document.RootElement.GetProperty("components").GetProperty("securitySchemes");
        var scheme = Assert.Single(schemes.EnumerateObject());
        Assert.Equal("apiKey", scheme.Value.GetProperty("type").GetString());
        Assert.Equal("header", scheme.Value.GetProperty("in").GetString());
        Assert.Equal("X-Api-Key", scheme.Value.GetProperty("name").GetString());
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
            foreach (var operation in path.Value.EnumerateObject())
                if (path.Name.StartsWith("/test", StringComparison.Ordinal))
                {
                    Assert.True(operation.Value.GetProperty("security")[0].TryGetProperty(scheme.Name, out _));
                    Assert.True(operation.Value.GetProperty("responses").TryGetProperty("401", out _));
                }
                else
                    Assert.False(operation.Value.TryGetProperty("security", out _));
        _host.AsyncConfiguration.ApiKeyHandler = _handler;
        _host.AsyncConfiguration.ApiKeyHeaderName = "Async-Key";
        using var custom = JsonDocument.Parse(await _http.GetStringAsync(url));
        Assert.Equal(new[] { "Async-Key", "X-Api-Key" }, custom.RootElement.GetProperty("components").GetProperty("securitySchemes").EnumerateObject().Select(s => s.Value.GetProperty("name").GetString()).Order());
        _host.TestConfiguration.ApiKeyHandler = null;
        _host.AsyncConfiguration.ApiKeyHandler = null;
        using var unprotected = JsonDocument.Parse(await _http.GetStringAsync(url));
        Assert.False(unprotected.RootElement.GetProperty("components").TryGetProperty("securitySchemes", out _));
    }

    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("valid", HttpStatusCode.OK)]
    public async Task Mcp_tool_execution_enforces_the_same_handler(string key, HttpStatusCode expected)
    {
        var request = Request();
        using var message = new HttpRequestMessage(HttpMethod.Post, _host.ServiceUrl.Replace("/test", "/mcp"))
        {
            Content = JsonContent.Create(new { jsonrpc = "2.0", id = 1, method = "tools/call", @params = new { name = "calculate", arguments = request } })
        };
        if (key != null) message.Headers.Add("X-Api-Key", key);
        using var response = await _http.SendAsync(message);
        Assert.Equal(expected, response.StatusCode);
        Assert.Equal(typeof(ITestService), Assert.Single(_handler.Seen).Contract);
        if (expected == HttpStatusCode.OK)
        {
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.False(result.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            Assert.Single(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
        }
        else
            Assert.DoesNotContain(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
    }

    public void Dispose()
    {
        _host.TestConfiguration.ApiKeyHandler = null;
        _host.AsyncConfiguration.ApiKeyHandler = null;
        _host.TestConfiguration.ApiKeyHeaderName = "X-Api-Key";
        _host.AsyncConfiguration.ApiKeyHeaderName = "X-Api-Key";
        _host.TestConfiguration.OnAuthorize = null;
        _host.TestConfiguration.OnBeforeMethodInvoke = null;
        _host.InterceptRequest = null;
        ConfigurationSettings.Sources.Remove(_settings);
        ServiceClient.CacheSettings = _previousCacheSettings;
        ServiceClient.ClearSettingsCache();
        _http.Dispose();
    }

    private static CalculationRequest Request() => new() { RequestId = Guid.NewGuid(), Left = 17, Right = 25, Text = "key test" };

    private static StringContent CreateJsonContent(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");

    private class RecordingHandler : IApiKeyHandler
    {
        internal ConcurrentQueue<(Type Contract, string Key, ServiceHandlerRequestContext Context, string CorrelationId)> Seen { get; } = new();
        internal Func<Type, string, ServiceHandlerRequestContext, CancellationToken, Task<bool>> Validate { get; set; } = (_, key, _, _) => Task.FromResult(key == "valid");

        public Task<bool> CheckApiKeyAsync(Type contractType, string key, ServiceHandlerRequestContext context, CancellationToken cancellationToken)
        {
            Seen.Enqueue((contractType, key, context, context.HttpRequest.Headers["X-Correlation-Id"].FirstOrDefault()));
            return Validate(contractType, key, context, cancellationToken);
        }
    }
}
