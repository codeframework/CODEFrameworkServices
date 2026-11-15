# Services regression tests

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet test CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj
```

All dependencies are public. If a machine-level private feed prevents restore, use `dotnet restore CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj --source https://api.nuget.org/v3/index.json`, then run the test command with `--no-restore`.

The HTTP fixture starts real Kestrel on an operating-system-assigned loopback port, using CODE Framework's service registration and routing. No external service, fixed port, credentials, or mock transport is required. Tests check a server-generated identity, calculated responses, and independently recorded server executions. The fixture shuts down the host after the class completes.

Coverage includes POST/GET/PUT/DELETE, inline and named URL parameters, Unicode and reserved characters, binary files and headers, standard server failure responses, concurrent request isolation, in-process configuration, and direct generated-proxy dispatch (reference/value/void/task returns and exceptions).

Assertions remain outside ServiceClient.Call callbacks because the current implementation swallows callback exceptions. Test parallelization is disabled because framework configuration and caches are static; one test explicitly exercises concurrent HTTP calls. Synchronous HTTP calls run on workers only to bound test waiting, not to claim async client support. Use `dotnet test --blame-hang --blame-hang-timeout 60s` as an additional process-level guard in CI.

This is a baseline for currently supported behavior, not a declaration that all IL is verifiable or all interface shapes work. The direct proxy pending-task test verifies forwarding; AsyncServiceTests separately exercises real async REST and in-process calls. Legacy exception swallowing is characterized for compatibility, not recommended for new calls. Inherited/generic interfaces remain separate work.

The in-process test uses a unique contract because ServiceGardenLocal has no public unregister operation. Its registration lives until the test process exits. Production code is unchanged by this test project.

## Proxy cache regression and compatibility

The endpoint regression starts two real servers exposing the same contract, then calls both without clearing caches between calls. Independent execution logs and server identities prove each call reaches the intended server. Direct proxy tests cover independent handlers, generated-type reuse, concurrent first use, cache bypass, explicit type-cache clearing, and preservation of generated types when service settings are cleared.

The active .NET Standard (`NETCORE`) implementation now caches generated types rather than proxy instances. Public signatures and default parameter values are unchanged, but object identity intentionally changes: every GetProxy call returns a fresh instance bound to the supplied handler. Consumers requiring a shared instance should retain that instance themselves. ServiceGardenLocal still retains its registered service proxies; this change does not introduce transient in-process services.

`useProxyCache: true` reuses the generated type. `false` generates a fresh type without reading or modifying the cache. `ClearProxyCache` removes cached type entries, leaves existing instances usable, and does not unload emitted types. Repeated bypassing or explicit clearing can therefore still accumulate types. `ServiceClient.ClearSettingsCache` no longer clears generated types because they contain no endpoint configuration. Derived protocol/settings invalidation is addressed separately below.

The inactive historical `NETFULL` implementation is unchanged and is not covered by this suite. No emitted method bodies, exception policy, retry behavior, or async transport support are changed in this fix.

## IL corrections

The subsequent IL fix in the active `NETCORE` implementation adds reference-return casts (including Task returns), an explicit MethodBase-to-MethodInfo cast, and correctly sized argument-index operands. Valid handler results retain their behavior; incompatible reference results now throw InvalidCastException at the proxy boundary instead of escaping as incorrectly typed references. Value returns continue to use unboxing and void methods continue to discard the handler result.

ProxyIlTests adds ten cases: invalid message/task/interface results, valid null/derived/interface-compatible results, invalid boxed value results, and complete argument preservation with 1, 255, 256, and 257 parameters. Large contracts are generated inside the tests because the proxy generator is a general utility, even though REST uses single-message operations. Five cases failed before the corrections: all three invalid reference results and both calls exceeding the short argument-index boundary. Existing tests also verify exact MethodInfo dispatch and pending-task identity/completion.

These are behavioral regression tests, not a formal IL-verifier pass. Generic methods, generic declaring interfaces, inherited interfaces, and by-reference signatures are separate work; this fix does not claim support for them or modify the inactive NETFULL branch.

## Configuration-cache invalidation

`ServiceClient.ClearSettingsCache()` (including a null or empty argument) clears raw settings, resolved protocols, service IDs, and message-size cache entries. Clearing `ServiceProtocol:ContractName` clears that raw setting and its resolved protocol. Clearing `ServiceProtocol` clears the global raw setting and all resolved protocols; cached per-contract settings still determine overrides. Other named settings clear only their own raw entry. Setting names retain their existing exact-case semantics.

Channel setup and invalidation are synchronized so an overlapping setup cannot repopulate a cleared cache with its old resolution after the clear completes. Network operations happen outside this lock. Existing channels retain their endpoint, generated proxy types remain cached, and one-time property initialization (such as LogCommunicationErrors) is not rerun. CacheSettings=false bypasses cached configuration but does not erase previously cached values; clear before re-enabling caching if those values must be discarded. Updating external configuration sources is the caller's responsibility.

Eight behavioral cases cover full clearing with null/empty, per-contract clearing, transport switches in both directions, global fallback across two contracts, explicit override precedence, unrelated-key and other-contract isolation, disabled caching, and endpoint refresh while retaining an old channel. HTTP calls verify actual server execution and results; local implementations return distinct identities. Six cases failed before the fix. Each test installs and removes its own configuration source and restores CacheSettings. Dedicated in-process registrations remain until process exit because the garden has no unregister API.

## Exception and retry migration

Existing `Call<T>(action, restServiceUrl)` and `GetChannelDedicated<T>` signatures and default exception/serialization behavior remain unchanged. Legacy retries still replay the entire callback once and match exact exception types. Two deliberate fixes apply: a null exception filter now behaves like an empty filter (retry any callback exception), and the configured delay applies to both. Enabling retries with an uninitialized filter will therefore perform a retry instead of throwing NullReferenceException. LegacyRetryTests documents these behaviors, including final exception suppression. Both bug regressions failed before the fixes.

The options-first overload is an opt-in path for synchronous contract methods:

```csharp
var options = new ServiceCallOptions
{
    ExceptionBehavior = ServiceExceptionBehavior.Propagate,
    MaxAttempts = 3,
    RetryDelay = TimeSpan.FromMilliseconds(200),
    CanRetryOperation = method => method.Name == nameof(ITestService.Lookup),
    CancellationToken = cancellationToken
};
ServiceClient.Call<ITestService>(options, service =>
{
    var response = service.Lookup(request);
    // Application processing happens once, outside the retry loop.
}, serviceUrl);
```

With default options, failures propagate and no retry occurs. Options are snapshotted before the callback; legacy global retry settings do not affect this path. `MaxAttempts` includes the initial attempt. Both an increased limit and explicit operation permission are required. Only the failed REST operation is retried, not the callback or preceding operations. Permission means the application knows replay is safe; it does not provide server deduplication or exactly-once execution.

Eligible HTTP statuses are 408, 429, 502, 503, and 504. Eligible connection errors are HttpRequestException chains containing a SocketException with connection reset/aborted/refused, timed-out, or try-again status. Other errors are not retried. The initial policy uses a fixed configured delay; it does not implement Retry-After, exponential backoff, or jitter. Application failure responses remain normal responses. Request serialization and response deserialization are strict and never retried; empty/null response messages are rejected. Shared JsonHelper and the legacy WebClient handler are unchanged.

The new REST handler uses a shared HttpClient with cookies disabled, buffered responses, and its default timeout. Cancellation interrupts HTTP I/O and retry waits. The public method remains synchronous and blocks its caller; this is not CallAsync support. It rejects Task-returning contract operations. Use synchronous callbacks, not async-void lambdas. In-process invocation preserves registered standard exception handling, unwraps reflection exceptions, and never retries local application exceptions. Its cancellation check occurs before invocation and cannot interrupt running synchronous code.

Final transport failures are ServiceTransportException (a CommunicationException subtype), with HTTP StatusCode when available and the original exception as InnerException; final transport errors honor LogCommunicationErrors. Payload failures remain Newtonsoft.Json exceptions. Explicit ExceptionBehavior.Suppress swallows final call failures without replaying the callback; cancellation and invalid options still propagate. New calls report missing registrations and unsupported protocols instead of silently skipping the callback.

Tests exercise actual HTTP attempt counts, earlier-operation and callback isolation, exhausted retries/status preservation, strict payload failures, application failure responses, cancellation of live I/O, cancellation before a delayed retry, options snapshots, file responses, and local exception compatibility. Global retry settings and fixture interceptors are restored after each test. For adoption, opt in at individual call sites and add exception handling there before migrating further consumers.

## CallAsync for task-returning contracts

`CallAsync<TService>(Func<TService, Task>, string restServiceUrl = null)` awaits the entire callback. `CallAsync<TService, TResult>(Func<TService, Task<TResult>>, string restServiceUrl = null)` returns its result. Both also have options-first overloads using ServiceCallOptions. The options are snapshotted, failures propagate by default, cancellation always propagates, and legacy global retries are ignored. Explicit suppression completes the nonresult overload normally or returns default(TResult) from the result overload.

```csharp
await ServiceClient.CallAsync<ICustomerService>(async service =>
{
    var response = await service.GetCustomersAsync(new GetCustomersRequest());
    Console.WriteLine(response.CustomerList.Count);
});

var customers = await ServiceClient.CallAsync<ICustomerService, GetCustomersResponse>(
    new ServiceCallOptions { CancellationToken = cancellationToken },
    service => service.GetCustomersAsync(new GetCustomersRequest()));
```

Operations invoked through CallAsync must return Task<TResponse>. Synchronous methods, bare Task operations, ValueTask operations, and arbitrary awaitables are outside this initial scope. Mixed contracts are allowed, but invoking a synchronous member through the async channel is rejected. Existing synchronous Call overloads and the legacy REST channel retain their behavior. Use CallAsync rather than passing async lambdas to the Action-based Call API.

The async REST proxy returns an actual Task<TResponse> and deserializes only TResponse. Task<FileResponse> uses the binary response path. It reuses cancellable HttpClient I/O and Task.Delay for per-operation retries, without Task.Run or blocking waits in the async execution path. The same strict serialization, transient classifications, and operation-repeatability requirements apply as for the options-based synchronous API.

The callback starts on the caller's context. Application awaits capture their context normally; library-internal awaits avoid capturing it. Await every service task used by the callback; unobserved fire-and-forget work is not tracked. The call's token cancels service I/O, retry waits, and waiting for in-process service tasks, not arbitrary application callback work. In-process cancellation does not stop the underlying service; a later task fault is observed internally. HTTP cancellation does not guarantee that server-side work stops either.

REST hosting now detects task return types even when implementations forward tasks without the async keyword. In-process proxies await Task<TResponse> methods so standard failure conversion works for both immediate invocation exceptions and faults after await; unhandled faults preserve the original exception, and cancellation remains cancellation. The synchronous in-process path is unchanged. This task handling also applies to tasks obtained directly from the registered in-process proxy.

Async tests use independently controlled received/release gates, real HTTP hosting, and local hosting of the same contract. They cover pending returns, forwarded tasks, callback completion and faults, service faults before/after task creation, failure responses, concurrent pending requests, binary responses, cancellation, bounded retries without callback replay, invalid JSON, suppression, argument validation, and caller synchronization-context preservation on a dedicated thread. Neither Task.FromResult-only tests nor arbitrary sleeps stand in for asynchronous execution. Both console samples now include a result-returning CallAsync example.

## API-key regression coverage

`ApiKeyTests` uses the real loopback host for missing/valid/invalid keys, nullable-key admission, duplicate HTTP fields, handler failures, request-context metadata, authorization ordering and denial, cancellation during validation, transparent legacy/options/async configuration, binary responses, overlapping credential isolation, configuration-cache refresh, custom headers, redirect refusal, CORS preflight, OpenAPI operation requirements, and MCP tool enforcement. Assertions run outside legacy callbacks. Request metadata needed after completion is copied while the request is alive.

See the repository README's [API-key usage](../README.md#api-keys) for server registration and client configuration. Validation failures remain HTTP transport failures; application failure-response conversion and existing retry/suppression policies retain their behavior.
