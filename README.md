# CODEFrameworkServices
All components related to CODE Framework Service/API features.

Fore more information about CODE Framework services see also: https://docs.codeframework.io/Understanding-Services-Core

## API keys

REST services can opt into API-key validation through an `IApiKeyHandler` from `CODE.Framework.Services.Server.AspNetCore`:

```csharp
public class MyApiKeyHandler : IApiKeyHandler
{
    public Task<bool> CheckApiKeyAsync(Type contractType, string? key,
        ServiceHandlerRequestContext context, CancellationToken cancellationToken)
    {
        // MyKeyStore represents your application's key-validation implementation.
        return MyKeyStore.CheckAsync(contractType, key, cancellationToken);
    }
}

builder.Services.AddHostedServices(config =>
{
    config.Services.Add(new ServiceHandlerConfigurationInstance
    {
        ServiceType = typeof(CustomerService),
        RouteBasePath = "/customers",
        ApiKeyHandler = new MyApiKeyHandler()
    });
});
```

The default header is `X-Api-Key`. Set `ApiKeyHeaderName` on the service configuration to use another header. The validator receives the hosted contract interface type and request context, including the operation, HTTP headers, caller address, and request-abort cancellation token. Instances are shared across requests and must be thread-safe. Do not retain the HTTP context after the request finishes. The application owns key storage and validation; the framework does not cache validation results.

Validation runs before authorization hooks, service creation, and service execution. Returning true continues through existing authorization and role checks; it does not establish a user principal. Returning false sends HTTP 401 with `WWW-Authenticate: ApiKey realm="services"`. Validator exceptions produce a generic HTTP 500 rather than an application failure response. Multiple header fields produce HTTP 400 without invoking the validator. Keys are opaque strings; empty headers remain empty when the HTTP stack preserves them. Missing headers are null, and the validator may explicitly allow them. Without a validator, existing behavior continues.

Configure .NET clients alongside their endpoints, using the same contract service ID:

```json
{
  "RestServiceUrl:ICustomerService": "https://example.com/customers",
  "RestServiceApiKey:ICustomerService": "your-key",
  "RestServiceApiKeyHeaderName:ICustomerService": "X-Api-Key"
}
```

The header-name setting is optional. Existing `ServiceClient.Call`, dedicated channels, options-based synchronous calls, and `CallAsync` add the configured key transparently, including binary responses. `ServiceCallOptions.ApiKey` and `ApiKeyHeaderName` override configuration for a call. A null override uses configuration; an empty key omits the header. In-process calls do not use HTTP API keys. Directly constructed `RestProxyHandler` instances also expose these two properties.

Keys and header names are resolved when a channel is created. After rotating a configured key, call `ServiceClient.ClearSettingsCache("RestServiceApiKey:ICustomerService")` (or clear all settings) before creating new channels. Existing channels keep their resolved credentials. Calls with explicit endpoint URLs still use the contract's configured key unless overridden. Options-based calls preserve existing failure/suppression behavior and do not retry HTTP 401; legacy callback suppression and configured callback retries remain unchanged.

Use HTTPS for key-bearing requests. Such requests do not automatically follow HTTP redirects, preventing forwarding custom key headers to a different destination. Browser clients must include the key header in the host's allowed CORS headers (alongside `Content-Type` for JSON); preflight requests remain unauthenticated. OpenAPI documents declare header security schemes and requirements only on protected REST operations, without including key values.

HTTP-hosted MCP tool calls enforce the same service validator before argument deserialization and execution. MCP initialization, discovery, and tool listings remain public; API-key support here is a tool-execution gate, not an implementation of MCP OAuth authorization. A shared MCP route can contain services with different validators.

### Customer API-key sample

`Sample.Web` registers `CustomerApiKeyHandler` for the entire customer service at its existing `/api/customers` route. The demonstration key is `sample-customer-api-key`, configured as `CustomerApiKey` in `Sample.Web/appsettings.json`. All customer REST operations and MCP customer tool calls require it. The user service keeps its existing behavior. The console and web HTTP clients configure the matching `RestServiceApiKey:ICustomerService` value, so their customer calls send the header transparently. The in-process sample calls services locally and does not use HTTP authentication.

Start the server on the port used by the sample clients:

```powershell
dotnet run --project Sample.Web --no-launch-profile --urls http://localhost:5008
```

From another terminal, compare a missing key (HTTP 401) with an accepted key (HTTP 200):

```powershell
curl.exe -i http://localhost:5008/api/customers
curl.exe -i -H "X-Api-Key: sample-customer-api-key" http://localhost:5008/api/customers
dotnet run --project Sample.Client.Console
```

In Swagger UI at `/swagger`, click **Authorize** and enter `sample-customer-api-key` for the API-key scheme before trying customer operations. This checked-in key is only for demonstrating the feature; real applications should supply their own keys and validation policy. If you change the sample server key, update the HTTP clients' configured key to match.
