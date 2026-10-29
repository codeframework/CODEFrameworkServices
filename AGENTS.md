# CODE Framework Services

## Purpose and architecture

- This is a public .NET framework used by existing applications. Favor focused changes, preserve supported behavior, and evaluate source, binary, configuration, and wire compatibility when changing public APIs or runtime behavior.
- Services are message-based: an operation accepts one request object and returns one response object. Request properties carry the operation's parameters. Preserve this model and the ability to host the same service in different environments.
- REST/JSON is the primary current transport. This solution also supports in-process execution and MCP server hosting. Historical WCF and custom transports inform the design, but do not assume they are active or supported by every current target.
- Client code uses a contract interface and configuration to select the service implementation or endpoint. Keep protocol and endpoint details out of ordinary calling code.
- A task is a .NET execution mechanism, not a wire message. Async transport should serialize request/response payloads, not Task objects. An async HTTP client does not require an async server implementation.

## Project map

- `CODE.Framework.Services.Contracts`: shared contracts, REST/documentation attributes, file responses, and service response helpers.
- `CODE.Framework.Services.Client`: `ServiceClient`, REST proxy handling, configuration-based channel selection, and `ServiceGardenLocal` for in-process hosting.
- `CODE.Framework.Fundamentals/Utilities/TransparentProxyGenerator.cs`: Reflection.Emit proxy generation and the `IProxyHandler` dispatch interface. This is a general utility, not exclusively a REST implementation.
- `CODE.Framework.Services.Server.AspNetCore`: ASP.NET Core REST hosting, invocation, OpenAPI, and MCP hosting.
- `Sample.Contracts`, `Sample.Services.Implementation`, and `Sample.Web`: examples of contracts, implementations, and hosting.
- `Sample.Client.Console` and `Sample.Client.ConsoleInProcess`: examples of transparent client calls over REST and in-process execution.
- `CODE.Framework.Services.Tests`: regression tests and their execution instructions in its README.
- Shared libraries currently target .NET Standard 2.0; the ASP.NET Core host and test project target .NET 10. Check project files before introducing APIs that may be unavailable on a target.

## Compatibility and implementation guidance

- Separate bug fixes, public behavior changes, and refactoring so reviewers can assess each. Avoid unrelated cleanup during transport or proxy changes.
- Treat exception propagation, failure-response conversion, retries, routing, JSON formatting, binary responses, and cache invalidation as observable behavior. Changing them can affect consumers even if method signatures stay the same.
- Distinguish application failure messages from HTTP/transport failures and serialization errors. Do not silently change their semantics while adding async support.
- Retrying an entire client callback can repeat successful operations and application side effects. Account for this before modifying retry behavior.
- Review static caches and ownership carefully. Generated proxy types, proxy instances, handlers, and hosted service instances have different lifetimes. Endpoint-specific or call-specific state must not accidentally be shared across callers.
- Clearing a proxy-instance cache does not unload types emitted into a non-collectible dynamic assembly. Avoid solving instance-sharing problems by repeatedly generating new types.
- For Reflection.Emit changes, verify stack types, operand widths, return conversions, and supported interface shapes. Runtime execution tests do not by themselves establish IL verifiability. Support additional signature shapes deliberately or reject them clearly.
- Async work should use actual asynchronous I/O. Task.Run around blocking HTTP is not equivalent to async transport. The generated proxy can return a handler-provided Task without generating its own async state machine.
- Preserve existing coding style and target compatibility; do not migrate serializers, target frameworks, or transports incidentally.

## Testing

- Add meaningful regression tests for changed behavior. Test observable results and boundaries rather than reproducing implementation logic in assertions.
- Run `dotnet test CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj` from the solution root with a compatible SDK.
- If a machine-level private NuGet feed blocks restore, the test dependencies are public: run `dotnet restore CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj --source https://api.nuget.org/v3/index.json`, then test with `--no-restore`. Do not alter a user's global NuGet configuration as a workaround.
- The HTTP integration fixture uses real Kestrel on an OS-assigned loopback port and the actual CODE Framework hosting/client paths. Preserve tests that independently verify server execution and returned responses; mocks alone cannot establish a complete round trip.
- Keep assertions outside `ServiceClient.Call` callbacks while that API swallows callback exceptions, otherwise a failed assertion can disappear.
- Framework configuration and caches are process-wide. The test assembly disables parallel test execution; exercise concurrency explicitly inside individual tests and isolate or restore modified global state.
- `ServiceGardenLocal` currently has no public unregister API. In-process tests use a dedicated contract to avoid contaminating HTTP tests.
- Use bounded waits and explicit gates for asynchronous tests, not arbitrary sleeps or assumptions about scheduling. A completed Task alone does not demonstrate nonblocking behavior.
- The suite covers successful REST/in-process calls, direct proxy dispatch, cached endpoint/handler isolation, generated-type reuse, and full/targeted configuration-cache invalidation. It does not certify every interface shape, async REST support, or IL verification. Add focused reproductions with the corresponding fixes rather than locking known bugs into the intended contract.
- Keep samples and test documentation consistent with supported behavior. Report what was actually run and any remaining validation limitations.

## Documentation

- Conceptual overview: https://docs.codeframework.io/Understanding-Services-Core
- Proxy design: https://docs.codeframework.io/TransparentProxyGenerator
- Client behavior: https://docs.codeframework.io/Calling-REST-Services-through-ServiceClient
- Published documentation may describe older implementations and targets. Use it for architectural intent, and verify current behavior against this solution's source and tests.

## Coding style

- C# preference: Avoid hand-written partial classes. Keep each class in its main code file unless explicitly requested otherwise. Leave tool-generated partial classes alone when required by their generator.
- Use file-scoped C# namespaces (`namespace Example;`) and do not indent the file contents beneath the namespace.
- Do not leave blank lines after the final closing `}` in a code file. A single terminating newline is fine.
- Use at most one blank line between code lines; never use consecutive blank lines.
