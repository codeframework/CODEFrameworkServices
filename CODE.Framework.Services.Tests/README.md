# Services regression tests

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet test CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj
```

All dependencies are public. If a machine-level private feed prevents restore, use `dotnet restore CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj --source https://api.nuget.org/v3/index.json`, then run the test command with `--no-restore`.

The HTTP fixture starts real Kestrel on an operating-system-assigned loopback port, using CODE Framework's service registration and routing. No external service, fixed port, credentials, or mock transport is required. Tests check a server-generated identity, calculated responses, and independently recorded server executions. The fixture shuts down the host after the class completes.

Coverage includes POST/GET/PUT/DELETE, inline and named URL parameters, Unicode and reserved characters, binary files and headers, standard server failure responses, concurrent request isolation, in-process configuration, and direct generated-proxy dispatch (reference/value/void/task returns and exceptions).

Assertions remain outside ServiceClient.Call callbacks because the current implementation swallows callback exceptions. Test parallelization is disabled because framework configuration and caches are static; one test explicitly exercises concurrent HTTP calls. Synchronous HTTP calls run on workers only to bound test waiting, not to claim async client support. Use `dotnet test --blame-hang --blame-hang-timeout 60s` as an additional process-level guard in CI.

This is a baseline for currently supported behavior, not a declaration that all IL is verifiable or all interface shapes work. The pending-task test verifies proxy forwarding only, not REST async support. There are no tests endorsing swallowed exceptions. Add reproductions with the corresponding fixes for strict serialization failures, retry policy, inherited/generic interfaces, and async REST/in-process fault handling.

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
