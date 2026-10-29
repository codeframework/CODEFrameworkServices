# Services regression tests

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet test CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj
```

All dependencies are public. If a machine-level private feed prevents restore, use `dotnet restore CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj --source https://api.nuget.org/v3/index.json`, then run the test command with `--no-restore`.

The HTTP fixture starts real Kestrel on an operating-system-assigned loopback port, using CODE Framework's service registration and routing. No external service, fixed port, credentials, or mock transport is required. Tests check a server-generated identity, calculated responses, and independently recorded server executions. The fixture shuts down the host after the class completes.

Coverage includes POST/GET/PUT/DELETE, inline and named URL parameters, Unicode and reserved characters, binary files and headers, standard server failure responses, concurrent request isolation, in-process configuration, and direct generated-proxy dispatch (reference/value/void/task returns and exceptions).

Assertions remain outside ServiceClient.Call callbacks because the current implementation swallows callback exceptions. Test parallelization is disabled because framework configuration and caches are static; one test explicitly exercises concurrent HTTP calls. Synchronous HTTP calls run on workers only to bound test waiting, not to claim async client support. Use `dotnet test --blame-hang --blame-hang-timeout 60s` as an additional process-level guard in CI.

This is a baseline for currently supported behavior, not a declaration that all IL is verifiable or all interface shapes work. The pending-task test verifies proxy forwarding only, not REST async support. There are no tests endorsing swallowed exceptions. Add reproductions with the corresponding fixes for configuration invalidation, strict serialization failures, retry policy, inherited/generic interfaces, and async REST/in-process fault handling.

The in-process test uses a unique contract because ServiceGardenLocal has no public unregister operation. Its registration lives until the test process exits. Production code is unchanged by this test project.

## Proxy cache regression and compatibility

The endpoint regression starts two real servers exposing the same contract, then calls both without clearing caches between calls. Independent execution logs and server identities prove each call reaches the intended server. Direct proxy tests cover independent handlers, generated-type reuse, concurrent first use, cache bypass, explicit type-cache clearing, and preservation of generated types when service settings are cleared.

The active .NET Standard (`NETCORE`) implementation now caches generated types rather than proxy instances. Public signatures and default parameter values are unchanged, but object identity intentionally changes: every GetProxy call returns a fresh instance bound to the supplied handler. Consumers requiring a shared instance should retain that instance themselves. ServiceGardenLocal still retains its registered service proxies; this change does not introduce transient in-process services.

`useProxyCache: true` reuses the generated type. `false` generates a fresh type without reading or modifying the cache. `ClearProxyCache` removes cached type entries, leaves existing instances usable, and does not unload emitted types. Repeated bypassing or explicit clearing can therefore still accumulate types. `ServiceClient.ClearSettingsCache` no longer clears generated types because they contain no endpoint configuration. This change does not fix the separate derived protocol/settings invalidation issue.

The inactive historical `NETFULL` implementation is unchanged and is not covered by this suite. No emitted method bodies, exception policy, retry behavior, or async transport support are changed in this fix.
