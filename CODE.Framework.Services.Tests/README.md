# Services regression tests

Run from the repository root with the .NET 10 SDK:

```powershell
dotnet test CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj
```

All dependencies are public. If a machine-level private feed prevents restore, use `dotnet restore CODE.Framework.Services.Tests/CODE.Framework.Services.Tests.csproj --source https://api.nuget.org/v3/index.json`, then run the test command with `--no-restore`.

The HTTP fixture starts real Kestrel on an operating-system-assigned loopback port, using CODE Framework's service registration and routing. No external service, fixed port, credentials, or mock transport is required. Tests check a server-generated identity, calculated responses, and independently recorded server executions. The fixture shuts down the host after the class completes.

Coverage includes POST/GET/PUT/DELETE, inline and named URL parameters, Unicode and reserved characters, binary files and headers, standard server failure responses, concurrent request isolation, in-process configuration, and direct generated-proxy dispatch (reference/value/void/task returns and exceptions).

Assertions remain outside ServiceClient.Call callbacks because the current implementation swallows callback exceptions. Test parallelization is disabled because framework configuration and caches are static; one test explicitly exercises concurrent HTTP calls. Synchronous HTTP calls run on workers only to bound test waiting, not to claim async client support. Use `dotnet test --blame-hang --blame-hang-timeout 60s` as an additional process-level guard in CI.

This is a baseline for currently supported behavior, not a declaration that all IL is verifiable or all interface shapes work. The pending-task test verifies proxy forwarding only, not REST async support. There are no tests endorsing swallowed exceptions or the known endpoint-cache bug. Add reproductions with the corresponding fixes: two endpoints for one contract without cache clearing, configuration invalidation, strict serialization failures, retry policy, inherited/generic interfaces, and async REST/in-process fault handling. The uncached-handler test does not cover cached endpoint isolation.

The in-process test uses a unique contract because ServiceGardenLocal has no public unregister operation. Its registration lives until the test process exits. Production code is unchanged by this test project.
