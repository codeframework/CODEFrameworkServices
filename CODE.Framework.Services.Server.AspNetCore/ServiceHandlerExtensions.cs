using CODE.Framework.Services.Server.AspNetCore.Properties;
using Microsoft.AspNetCore.Http.Extensions;
using DescriptionAttribute = CODE.Framework.Services.Contracts.DescriptionAttribute;
using System.Security.Claims;
using System.Security.Principal;
using System.Threading.Tasks;

namespace CODE.Framework.Services.Server.AspNetCore;

public static class ServiceHandlerExtensions
{
    /// <summary>
    /// Configure the service and make it so you can inject IOptions
    /// </summary>
    /// <param name="services"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    [Obsolete("Use AddHostedServices() instead.")]
    public static IServiceCollection AddServiceHandler(this IServiceCollection services, Action<ServiceHandlerConfiguration> optionsAction = null) => AddHostedServices(services, optionsAction);

    /// <summary>
    /// Configures the system to enable service hosting (and all required features) and then sets up the hosted service objects
    /// </summary>
    /// <param name="services"></param>
    /// <param name="optionsAction"></param>
    /// <returns></returns>
    public static IServiceCollection AddHostedServices(this IServiceCollection services, Action<ServiceHandlerConfiguration> optionsAction = null)
    {
        // add strongly typed configuration
        services.AddOptions();
        services.AddRouting();

        var provider = services.BuildServiceProvider();
        var serviceConfiguration = provider.GetService<IConfiguration>();

        var config = new ServiceHandlerConfiguration();
        serviceConfiguration.Bind("ServiceHandler", config);
        ServiceHandlerConfiguration.Current = config;

        optionsAction?.Invoke(config);

        foreach (var svc in config.Services)
        {
            if (svc.ServiceType == null)
            {
                var type = ObjectHelper.GetTypeFromName(svc.ServiceTypeName);
                if (type == null)
                {
                    var assemblyNameWithPath = svc.AssemblyName;
                    if (assemblyNameWithPath.IndexOf("\\", StringComparison.Ordinal) < 0 && assemblyNameWithPath.IndexOf("/", StringComparison.Ordinal) < 0)
                    {
                        var entryAssembly = Assembly.GetEntryAssembly();
                        if (entryAssembly != null)
                        {
                            var directoryName = Path.GetDirectoryName(entryAssembly.Location);
                            if (directoryName != null) assemblyNameWithPath = Path.Combine(directoryName, assemblyNameWithPath);
                        }
                    }

                    var assemblyNameWithFullPath = Path.GetFullPath(assemblyNameWithPath);
                    if (ObjectHelper.LoadAssembly(assemblyNameWithFullPath) == null)
                        throw new ArgumentException(string.Format(Resources.InvalidServiceType, svc.ServiceTypeName));
                    type = ObjectHelper.GetTypeFromName(svc.ServiceTypeName) ?? throw new ArgumentException(string.Format(Resources.InvalidServiceType, svc.ServiceTypeName));
                }

                svc.ServiceType = type;
            }

            // Add to DI so we can compose the constructor
            services.AddTransient(svc.ServiceType);
        }

        // Add configured instance to DI
        services.AddSingleton(config);

        // Add service and create Policy with options
        services.AddCors(options =>
        {
            options.AddPolicy(config.Cors.CorsPolicyName,
                              builder =>
                              {
                                  if (config.Cors.AllowedOrigins == "*")
                                      builder = builder.SetIsOriginAllowed(s => true);
                                  else if (!string.IsNullOrEmpty(config.Cors.AllowedOrigins))
                                      builder.WithOrigins(config.Cors.AllowedOrigins.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

                                  if (!string.IsNullOrEmpty(config.Cors.AllowedMethods))
                                      builder.WithMethods(config.Cors.AllowedMethods.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

                                  if (!string.IsNullOrEmpty(config.Cors.AllowedHeaders))
                                      builder.WithHeaders(config.Cors.AllowedHeaders.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries));

                                  if (config.Cors.AllowCredentials)
                                      builder.AllowCredentials();
                              });
        });

        // Allow injection of the IPrincipal
        services.AddScoped<IPrincipal>(serviceProvider =>
        {
            try
            {
                // get User from Http Context
                return serviceProvider.GetRequiredService(typeof(IHttpContextAccessor)) is IHttpContextAccessor context ? context.HttpContext.User : null;
            }
            catch
            {
                // return an empty identity
                return new ClaimsPrincipal(new ClaimsIdentity());
            }
        });

        return services;
    }

    /// <summary>
    /// Enabled extended error display in exception return messages
    /// </summary>
    /// <param name="appBuilder"></param>
    public static void ShowExtendedFailureInformation(this IApplicationBuilder appBuilder) => ServiceHelper.ShowExtendedFailureInformation = true;

    /// <summary>
    /// Enabled CODE Framework service hosting
    /// </summary>
    /// <param name="appBuilder"></param>
    /// <returns></returns>
    public static IApplicationBuilder UseServiceHandler(this IApplicationBuilder appBuilder)
    {
        var serviceConfig = ServiceHandlerConfiguration.Current ?? throw new Exception("CODE Framework hosted services must be configured before UseOpenApiHandler() can be called. Use AddHostedServices() to configure which services are to be present in the hosting environment.");
        if (serviceConfig.Cors.UseCorsPolicy)
            appBuilder.UseCors(serviceConfig.Cors.CorsPolicyName);

        // Endpoints require routing, so we make sure it is there
        appBuilder.UseRouting();

        appBuilder.UseEndpoints(endpoints =>
        {
            foreach (var serviceInstanceConfig in serviceConfig.Services)
                // conditionally route to service handler based on RouteBasePath
                appBuilder.MapWhen(
                                   context =>
                                   {
                                       var requestPath = context.Request.Path.ToString().ToLower();
                                       if (SwaggerRoutes != null && SwaggerRoutes.Contains(requestPath)) return false; // We make sure we are not accidently eating up a configured swagger/openapi route
                                       var servicePath = serviceInstanceConfig.RouteBasePath.ToLower();
                                       var matched = requestPath == servicePath || requestPath.StartsWith(servicePath.Replace("//", "/") + "/");
                                       return matched;
                                   },
                                   builder =>
                                   {
                                       //if (serviceConfig.Cors.UseCorsPolicy)
                                       //    builder.UseCors(serviceConfig.Cors.CorsPolicyName);

                                       // Build up route mapping
                                       builder.UseRouter(routeBuilder =>
                                       {
                                           // Get Service interface = assuming first interface def is service interface
                                           var interfaces = serviceInstanceConfig.ServiceType.GetInterfaces();
                                           if (interfaces.Length < 1)
                                               throw new NotSupportedException(Resources.HostedServiceRequiresAnInterface);

                                           // Loop through service methods and cache the propertyInfo info, parameter info, and RestAttribute
                                           // in a MethodInvocationContext so we don't have to do this for each propertyInfo call
                                           foreach (var method in serviceInstanceConfig.ServiceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.DeclaredOnly))
                                           {
                                               // find service contract                                
                                               var interfaceMethod = interfaces[0].GetMethod(method.Name);
                                               if (interfaceMethod == null) continue; // Should never happen, but doesn't hurt to check

                                               var restAttribute = GetRestAttribute(interfaceMethod);
                                               if (restAttribute == null) continue; // This should never happen since GetRestAttribute() above returns a default attribute if none is attached

                                               var relativeRoute = restAttribute.Route;
                                               if (relativeRoute == null)
                                               {
                                                   // If no route is defined, we either build a route out of name and other attributes, or we use the propertyInfo name as the last resort.
                                                   // Note: string.Empty is a valid route (and also a valid name). Only null values indicate that the setting has not been set!

                                                   relativeRoute = restAttribute.Name ?? method.Name;

                                                   // We also have to take a look at the parameter(s) - there should be only one - to build the route
                                                   var parameters = method.GetParameters();
                                                   if (parameters.Length > 0)
                                                   {
                                                       var parameterType = parameters[0].ParameterType;
                                                       var parameterProperties = parameterType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                                                       var inlineParameters = GetSortedInlineParameterNames(parameterProperties);
                                                       foreach (var inlineParameter in inlineParameters)
                                                           relativeRoute += $"/{{{inlineParameter}}}";
                                                   }
                                               }

                                               if (relativeRoute.StartsWith("/")) relativeRoute = relativeRoute.Substring(1);

                                               // Figure out the full route we pass the ASP.NET Core Route Manager
                                               var fullRoute = (serviceInstanceConfig.RouteBasePath + "/" + relativeRoute).Replace("//", "/");
                                               if (fullRoute.StartsWith('/')) fullRoute = fullRoute[1..];

                                               // Cache reflection and context data
                                               var methodContext = new MethodInvocationContext(method, serviceConfig, serviceInstanceConfig);

                                               var roles = restAttribute.AuthorizationRoles;
                                               if (roles != null)
                                                   methodContext.AuthorizationRoles = roles.Split([','], StringSplitOptions.RemoveEmptyEntries).ToList();

                                               // This code is what triggers the SERVICE METHOD EXECUTION via a delegate that is called when the route is matched
                                               async Task exec(HttpRequest req, HttpResponse resp, RouteData routeData)
                                               {
                                                   // ReSharper disable once AccessToModifiedClosure
                                                   var handler = new ServiceHandler(req.HttpContext, routeData, methodContext);
                                                   await handler.ProcessRequest();
                                               }

                                               routeBuilder.MapVerb("OPTIONS", fullRoute, async (req, resp, route) =>
                                               {
                                                   resp.StatusCode = StatusCodes.Status204NoContent;
                                                   await Task.CompletedTask;
                                               });

                                               routeBuilder.MapVerb(restAttribute.Method.ToString(), fullRoute, exec);
                                           }
                                       });
                                   });
        });

        return appBuilder;
    }

    public static IApplicationBuilder UseMCPHandler(this IApplicationBuilder appBuilder, bool supportOpenApiJson = true, string generalMcpRoute = "")
    {
        //MCPRoutes ??= [generalMcpRoute];

        var serviceConfig = ServiceHandlerConfiguration.Current ?? throw new Exception("CODE Framework hosted services must be configured before UseMCPHandler() can be called. Use AddHostedServices() to configure which services are to be present in the hosting environment.");
        var configuration = appBuilder.ApplicationServices.GetService<IConfiguration>();
        var allowedHostsSetting = configuration?["MCP:AllowedHosts"] ?? configuration?["AllowedHosts"];
        var allowedHosts = string.IsNullOrWhiteSpace(allowedHostsSetting)
            ? ["*"]
            : allowedHostsSetting.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        // Endpoints require routing, so we make sure it is there
        appBuilder.UseRouting();

        appBuilder.UseEndpoints(endpoints =>
        {
            appBuilder.MapWhen(
                                context =>
                                {
                                    var requestPath = context.Request.Path.ToString().Trim().ToLowerInvariant();
                                    var toolsListFullRoute = !string.IsNullOrEmpty(generalMcpRoute) ? generalMcpRoute : "/mcp";
                                    if (!toolsListFullRoute.StartsWith('/'))
                                        toolsListFullRoute = $"/{toolsListFullRoute}";
                                    toolsListFullRoute = toolsListFullRoute.Trim().ToLowerInvariant();
                                    return requestPath == toolsListFullRoute;
                                },
                                builder =>
                                {
                                    builder.Use(async (context, next) =>
                                    {
                                        if (!IsMcpHostAllowed(context.Request.Host.Host, allowedHosts))
                                        {
                                            context.Response.StatusCode = StatusCodes.Status400BadRequest;
                                            return;
                                        }

                                        await next();
                                    });

                                    // Build up route mapping
                                    builder.UseRouter(routeBuilder =>
                                    {
                                        var toolsListFullRoute = !string.IsNullOrEmpty(generalMcpRoute) ? generalMcpRoute : "/mcp";
                                        if (!toolsListFullRoute.StartsWith('/'))
                                            toolsListFullRoute = $"/{toolsListFullRoute}";
                                        routeBuilder.MapVerb("GET", toolsListFullRoute, HandleMCPGet); // Indicates that GET is not allowed, only POST
                                        routeBuilder.MapVerb("POST", toolsListFullRoute, HandleMCPPost(serviceConfig.Services, allowedHosts));
                                        //MCPRoutes.Add(toolsListFullRoute);
                                    });
                                });
        });

        return appBuilder;
    }

    //private static List<string> MCPRoutes { get; set; }

    private static Task HandleMCPGet(HttpRequest req, HttpResponse resp, RouteData route)
    {
        resp.StatusCode = StatusCodes.Status405MethodNotAllowed;
        resp.Headers.Allow = "POST";
        return Task.CompletedTask;
    }

    private static Func<HttpRequest, HttpResponse, RouteData, Task> HandleMCPPost(List<ServiceHandlerConfigurationInstance> serviceInstanceConfigurations, string[] allowedHosts) => async (req, resp, route) =>
    {
        resp.ContentType = "application/json; charset=utf-8";

        if (!IsMcpHostAllowed(req.Host.Host, allowedHosts))
        {
            resp.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        JsonDocument requestDocument;
        try
        {
            requestDocument = await JsonDocument.ParseAsync(req.Body, cancellationToken: req.HttpContext.RequestAborted);
        }
        catch (JsonException)
        {
            await WriteMcpJsonRpcError(resp, null, -32700, "Parse error");
            return;
        }

        using (requestDocument)
        {
            var request = requestDocument.RootElement;
            if (request.ValueKind != JsonValueKind.Object ||
                !request.TryGetProperty("jsonrpc", out var jsonRpc) ||
                jsonRpc.ValueKind != JsonValueKind.String ||
                jsonRpc.GetString() != "2.0" ||
                !request.TryGetProperty("method", out var methodElement) ||
                methodElement.ValueKind != JsonValueKind.String)
            {
                JsonElement? invalidId = request.ValueKind == JsonValueKind.Object && request.TryGetProperty("id", out var idElement)
                    ? idElement
                    : null;
                await WriteMcpJsonRpcError(resp, invalidId, -32600, "Invalid Request");
                return;
            }

            var hasId = request.TryGetProperty("id", out var id);
            if (hasId && id.ValueKind is not (JsonValueKind.String or JsonValueKind.Number or JsonValueKind.Null))
            {
                await WriteMcpJsonRpcError(resp, id, -32600, "Invalid Request");
                return;
            }

            var method = methodElement.GetString();
            if (method.StartsWith("notifications/", StringComparison.OrdinalIgnoreCase))
            {
                // Notifications are one-way JSON-RPC messages with no response. They are used for
                // events like initialized/cancelled/progress and should not trigger a result or error reply.
                resp.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            if (!hasId)
            {
                resp.StatusCode = StatusCodes.Status202Accepted;
                return;
            }

            if (method == "tools/call")
            {
                await HandleMcpToolCall(req, resp, id, serviceInstanceConfigurations, request);
                return;
            }

            if (method == "initialize")
            {
                var requestedVersion = request.TryGetProperty("params", out var initializeParams) &&
                                       initializeParams.ValueKind == JsonValueKind.Object &&
                                       initializeParams.TryGetProperty("protocolVersion", out var versionElement) &&
                                       versionElement.ValueKind == JsonValueKind.String
                    ? versionElement.GetString()
                    : null;

                var supportedProtocolVersions = new[] { "2025-11-25", "2025-06-18", "2025-03-26", "2024-11-05" };
                var protocolVersion = requestedVersion is null
                    ? supportedProtocolVersions[0]
                    : supportedProtocolVersions.Contains(requestedVersion)
                        ? requestedVersion
                        : supportedProtocolVersions[0];

                resp.Headers["MCP-Protocol-Version"] = protocolVersion;
                await WriteMcpJsonRpcResult(resp, id, writer =>
                {
                    writer.WriteString("protocolVersion", protocolVersion);
                    writer.WriteStartObject("capabilities");
                    writer.WriteStartObject("tools");
                    writer.WriteBoolean("listChanged", false);
                    writer.WriteEndObject();
                    writer.WriteEndObject();
                    writer.WriteStartObject("serverInfo");
                    writer.WriteString("name", "CODE Framework Services");
                    writer.WriteString("version", typeof(ServiceHandlerExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0");
                    writer.WriteEndObject();
                });
                return;
            }

            if (method == "ping")
            {
                await WriteMcpJsonRpcResult(resp, id, _ => { });
                return;
            }

            if (method == "server/discover")
            {
                await WriteMcpJsonRpcResult(resp, id, writer =>
                {
                    writer.WriteStartObject("server");
                    writer.WriteString("name", "CODE Framework Services");
                    writer.WriteString("version", typeof(ServiceHandlerExtensions).Assembly.GetName().Version?.ToString() ?? "1.0.0");
                    writer.WriteEndObject();
                });
                return;
            }

            if (method == "tools/list")
            {
                await WriteMcpJsonRpcResult(resp, id, writer =>
                {
                    writer.WriteStartArray("tools");
                    WriteMcpTools(writer, serviceInstanceConfigurations);
                    writer.WriteEndArray();
                });
                return;
            }

            await WriteMcpJsonRpcError(resp, id, -32601, $"Method not found: {method}");
        }
    };

    private static bool IsMcpHostAllowed(string host, string[] allowedHosts)
    {
        if (string.IsNullOrWhiteSpace(host))
            return false;

        foreach (var allowedHost in allowedHosts)
        {
            if (allowedHost == "*")
                return true;

            if (allowedHost.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = allowedHost[1..];
                if (host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else if (string.Equals(host, allowedHost.Trim('[', ']'), StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static async Task HandleMcpToolCall(HttpRequest request, HttpResponse response, JsonElement id, List<ServiceHandlerConfigurationInstance> serviceInstanceConfigurations, JsonElement jsonRpcRequest)
    {
        if (!jsonRpcRequest.TryGetProperty("params", out var parameters) ||
            parameters.ValueKind != JsonValueKind.Object ||
            !parameters.TryGetProperty("name", out var nameElement) ||
            nameElement.ValueKind != JsonValueKind.String)
        {
            await WriteMcpJsonRpcError(response, id, -32602, "Invalid tools/call parameters.");
            return;
        }

        var requestedName = nameElement.GetString();
        var toolMatches = GetHostedServiceDescriptions(serviceInstanceConfigurations, discoverForMcpTools: true)
            .SelectMany(service => service.Operations.Select(operation => (Service: service, Operation: operation)))
            .Where(candidate => string.Equals(MCPHelper.GetToolName(candidate.Service, candidate.Operation), requestedName, StringComparison.Ordinal))
            .ToList();

        if (toolMatches.Count != 1)
        {
            await WriteMcpJsonRpcError(response, id, -32602,
                toolMatches.Count == 0 ? $"Unknown tool: {requestedName}" : $"Tool name is ambiguous: {requestedName}");
            return;
        }

        var match = toolMatches[0];
        var operation = match.Operation;
        var serviceConfiguration = match.Service.Configuration;
        var methodParameters = operation.Method.GetParameters();
        if (methodParameters.Length > 1)
        {
            await WriteMcpJsonRpcError(response, id, -32602, "MCP tools may have at most one input parameter.");
            return;
        }

        JsonElement arguments = default;
        if (parameters.TryGetProperty("arguments", out var argumentsElement))
        {
            if (argumentsElement.ValueKind != JsonValueKind.Object)
            {
                await WriteMcpJsonRpcError(response, id, -32602, "Tool arguments must be a JSON object.");
                return;
            }
            arguments = argumentsElement;
        }

        var serializerOptions = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        if (serviceConfiguration.JsonFormatMode == JsonFormatModes.CamelCase)
            serializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;

        object[] invocationArguments;
        try
        {
            if (methodParameters.Length == 0)
            {
                if (arguments.ValueKind == JsonValueKind.Object && arguments.EnumerateObject().Any())
                {
                    await WriteMcpJsonRpcError(response, id, -32602, "This tool does not accept arguments.");
                    return;
                }
                invocationArguments = [];
            }
            else
            {
                var argumentJson = arguments.ValueKind == JsonValueKind.Object ? arguments.GetRawText() : "{}";
                var argument = JsonSerializer.Deserialize(argumentJson, methodParameters[0].ParameterType, serializerOptions);
                if (argument == null)
                {
                    await WriteMcpJsonRpcError(response, id, -32602, "Tool arguments could not be deserialized.");
                    return;
                }
                invocationArguments = [argument];
            }
        }
        catch (JsonException)
        {
            await WriteMcpJsonRpcError(response, id, -32602, "Tool arguments do not match the expected input schema.");
            return;
        }

        object serviceInstance = null;
        string resultJson;
        string executionError = null;
        try
        {
            serviceInstance = request.HttpContext.RequestServices.GetService(serviceConfiguration.ServiceType)
                ?? throw new InvalidOperationException($"Unable to create service instance {serviceConfiguration.ServiceType}.");

            var principal = request.HttpContext.User;
            UserPrincipalHelper.AddPrincipal(serviceInstance, principal);

            var methodContext = new MethodInvocationContext(operation.Method, ServiceHandlerConfiguration.Current, serviceConfiguration);
            ValidateMcpToolRoles(methodContext.AuthorizationRoles, principal);
            var requestContext = new ServiceHandlerRequestContext
            {
                HttpContext = request.HttpContext,
                HttpRequest = request,
                HttpResponse = response,
                ServiceInstanceConfiguration = serviceConfiguration,
                MethodContext = methodContext,
                Url = new ServiceHandlerRequestContextUrl
                {
                    Url = request.GetDisplayUrl(),
                    UrlPath = request.Path.Value,
                    QueryString = request.QueryString,
                    HttpMethod = request.Method.ToUpperInvariant()
                }
            };

            if (serviceConfiguration.OnAuthorize != null && !await serviceConfiguration.OnAuthorize(requestContext))
                throw new UnauthorizedAccessException("Not authorized to call this tool.");
            if (serviceConfiguration.OnBeforeMethodInvoke != null)
                await serviceConfiguration.OnBeforeMethodInvoke(requestContext);

            var result = operation.Method.Invoke(serviceInstance, invocationArguments);
            requestContext.ResultValue = await AwaitMcpToolResult(result);
            if (serviceConfiguration.OnAfterMethodInvoke != null)
                await serviceConfiguration.OnAfterMethodInvoke(requestContext);

            resultJson = requestContext.ResultValue == null
                ? "null"
                : JsonSerializer.Serialize(requestContext.ResultValue, requestContext.ResultValue.GetType(), serializerOptions);
        }
        catch (Exception exception)
        {
            var error = exception is TargetInvocationException { InnerException: not null } invocationException
                ? invocationException.InnerException
                : exception;
            executionError = ServiceHelper.ShowExtendedFailureInformation ? error.Message : "Tool execution failed.";
            resultJson = null;
        }
        finally
        {
            if (serviceInstance != null)
                UserPrincipalHelper.RemovePrincipal(serviceInstance);
        }

        await WriteMcpJsonRpcResult(response, id, writer =>
        {
            writer.WriteStartArray("content");
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", executionError ?? resultJson);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteBoolean("isError", executionError != null);
        });
    }

    private static void ValidateMcpToolRoles(IReadOnlyCollection<string> authorizationRoles, IPrincipal user)
    {
        if (authorizationRoles.Count == 0)
            return;

        if (user?.Identity is not ClaimsIdentity identity || !identity.IsAuthenticated)
            throw new UnauthorizedAccessException("Access denied: User is not part of required Role.");

        var rolesClaim = identity.Claims.FirstOrDefault(claim => claim.Type == ClaimTypes.Role);
        if (rolesClaim == null || string.IsNullOrEmpty(rolesClaim.Value))
            return;

        var roles = rolesClaim.Value.Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (!roles.Any(role => authorizationRoles.Contains(role)))
            throw new UnauthorizedAccessException("Access denied: User is not part of required Role.");
    }

    private static async Task<object> AwaitMcpToolResult(object result)
    {
        if (result is Task task)
        {
            await task;
            return task.GetType().IsGenericType ? task.GetType().GetProperty("Result")?.GetValue(task) : null;
        }

        if (result is not null && result.GetType().IsGenericType &&
            result.GetType().GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var taskResult = (Task)result.GetType().GetMethod("AsTask")!.Invoke(result, null);
            await taskResult;
            return taskResult.GetType().GetProperty("Result")?.GetValue(taskResult);
        }

        if (result is ValueTask valueTask)
        {
            await valueTask;
            return null;
        }

        return result;
    }

    private static void WriteMcpTools(Utf8JsonWriter writer, List<ServiceHandlerConfigurationInstance> serviceInstanceConfigurations)
    {
        var serviceDescriptions = GetHostedServiceDescriptions(serviceInstanceConfigurations, discoverForMcpTools: true);
        foreach (var serviceDescription in serviceDescriptions)
            foreach (var operation in serviceDescription.Operations)
            {
                writer.WriteStartObject();
                writer.WriteString("name", MCPHelper.GetToolName(serviceDescription, operation));

                writer.WriteString("description", operation.Verbs[operation.Verbs.Keys.First()].Description);

                writer.WritePropertyName("inputSchema");
                WriteMcpSchema(writer, operation.InputSchema);
                writer.WriteEndObject();
            }
    }

    private static async Task WriteMcpJsonRpcResult(HttpResponse response, JsonElement id, Action<Utf8JsonWriter> writeResult)
    {
        await using var writer = new Utf8JsonWriter(response.Body);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");
        id.WriteTo(writer);
        writer.WritePropertyName("result");
        writer.WriteStartObject();
        writeResult(writer);
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    private static async Task WriteMcpJsonRpcError(HttpResponse response, JsonElement? id, int code, string message)
    {
        response.ContentType = "application/json; charset=utf-8";
        await using var writer = new Utf8JsonWriter(response.Body);
        writer.WriteStartObject();
        writer.WriteString("jsonrpc", "2.0");
        writer.WritePropertyName("id");
        if (id.HasValue)
            id.Value.WriteTo(writer);
        else
            writer.WriteNullValue();
        writer.WriteStartObject("error");
        writer.WriteNumber("code", code);
        writer.WriteString("message", message);
        writer.WriteEndObject();
        writer.WriteEndObject();
        await writer.FlushAsync();
    }

    private static List<HostedServiceDescription> GetHostedServiceDescriptions(List<ServiceHandlerConfigurationInstance> serviceInstanceConfigurations, bool discoverForMcpTools = false)
    {
        var xmlDocumentationFiles = new Dictionary<Assembly, XmlCodeDocumentationFile>();
        var serviceDescriptions = new List<HostedServiceDescription>();

        foreach (var serviceInstanceConfig in serviceInstanceConfigurations)
        {
            var interfaces = serviceInstanceConfig.ServiceType.GetInterfaces();
            if (interfaces.Length < 1)
                throw new NotSupportedException(Resources.HostedServiceRequiresAnInterface);

            var contractType = interfaces[0];
            if (!xmlDocumentationFiles.ContainsKey(contractType.Assembly))
                xmlDocumentationFiles.Add(contractType.Assembly, new XmlCodeDocumentationFile(contractType.Assembly));

            var serviceDescription = new HostedServiceDescription(serviceInstanceConfig, contractType)
            {
                Description = OpenApiHelper.GetDescription(serviceInstanceConfig.ServiceType, contractType, xmlDocumentationFiles, discoverForMcpTools),
                ExternalDocs = OpenApiHelper.GetExternalDocs(serviceInstanceConfig.ServiceType, contractType)
            };

            var methods = serviceInstanceConfig.ServiceType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.InvokeMethod | BindingFlags.DeclaredOnly);
            foreach (var method in methods)
            {
                var interfaceMethod = contractType.GetMethod(method.Name);
                if (interfaceMethod == null) continue;

                if (!discoverForMcpTools)
                {
                    var operation = GetOperationForOpenApi(xmlDocumentationFiles, serviceInstanceConfig, contractType, serviceDescription, method, interfaceMethod);
                    if (operation != null)
                        serviceDescription.Operations.Add(operation);
                }
                else
                {
                    var operation = GetOperationForMcpTool(xmlDocumentationFiles, serviceInstanceConfig, contractType, serviceDescription, method, interfaceMethod);
                    if (operation != null)
                        serviceDescription.Operations.Add(operation);
                }
            }

            serviceDescriptions.Add(serviceDescription);
        }

        return serviceDescriptions;
    }

    private static ServiceOperationDescription GetOperationForMcpTool(Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles, ServiceHandlerConfigurationInstance serviceInstanceConfig, Type contractType, HostedServiceDescription serviceDescription, MethodInfo method, MethodInfo interfaceMethod)
    {
        var exposedToolAttribute = MCPHelper.GetExposedToolAttribute(method);
        if (exposedToolAttribute == null) return null;
        if (!exposedToolAttribute.IsExposed) return null;

        var operation = new ServiceOperationDescription(method.Name, "POST", method.Name, method)
        {
            Name = method.Name,
            ReturnType = interfaceMethod.ReturnType,
            InputSchema = CreateMcpParametersSchema(interfaceMethod, xmlDocumentationFiles, serviceInstanceConfig.JsonFormatMode),
            IsExposedMcpTool = true
        };

        var description = MCPHelper.GetDescription(method, contractType, exposedToolAttribute, xmlDocumentationFiles);
        if (string.IsNullOrEmpty(description))
            description = MCPHelper.GetDescription(interfaceMethod, contractType, exposedToolAttribute, xmlDocumentationFiles);
        if (string.IsNullOrEmpty(description)) throw new Exception($"MCP tool description for method {interfaceMethod.Name} is required since AI needs it to know when to use the tool. Add an [ExposedTool(Description = ...] attribute, or a [Description] attribute, or a [Summary] attribute to the method or its parameters, or add XML documentation to the method.");
        operation.Verbs[operation.Verbs.Keys.First()].Description = description;

        var obsoleteAttribute = interfaceMethod.GetCustomAttribute<ObsoleteAttribute>();
        if (obsoleteAttribute != null)
        {
            operation.Obsolete = true;
            if (!string.IsNullOrEmpty(obsoleteAttribute.Message))
                operation.ObsoleteReason = obsoleteAttribute.Message.Trim();
        }

        MCPHelper.ExtractParameters(interfaceMethod, operation, xmlDocumentationFiles);

        return operation;
    }

    private static ServiceOperationDescription GetOperationForOpenApi(Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles, ServiceHandlerConfigurationInstance serviceInstanceConfig, Type contractType, HostedServiceDescription serviceDescription, MethodInfo method, MethodInfo interfaceMethod)
    {
        var restAttribute = GetRestAttribute(interfaceMethod);
        if (restAttribute == null) return null;

        var httpVerb = restAttribute.Method.ToString().ToLowerInvariant();
        var definedRoute = restAttribute.Route != null ? restAttribute.Route : restAttribute.Name == null ? $"{method.Name}" : $"{restAttribute.Name}";
        var fullRoute = string.IsNullOrEmpty(definedRoute) ? $"{serviceInstanceConfig.RouteBasePath}" : $"{serviceInstanceConfig.RouteBasePath}/{definedRoute}";

        var operation = new ServiceOperationDescription(method.Name, httpVerb, method.Name, method)
        {
            Name = method.Name,
            HttpVerb = httpVerb,
            Route = fullRoute,
            ReturnType = interfaceMethod.ReturnType
        };
        operation.Verbs[operation.Verbs.Keys.First()].Summary = OpenApiHelper.GetSummary(interfaceMethod, contractType, xmlDocumentationFiles);
        operation.Verbs[operation.Verbs.Keys.First()].Description = OpenApiHelper.GetDescription(interfaceMethod, contractType, xmlDocumentationFiles);

        var obsoleteAttribute = interfaceMethod.GetCustomAttribute<ObsoleteAttribute>();
        if (obsoleteAttribute != null)
        {
            operation.Obsolete = true;
            if (!string.IsNullOrEmpty(obsoleteAttribute.Message))
                operation.ObsoleteReason = obsoleteAttribute.Message.Trim();
        }

        var operationVerb = operation.Verbs[operation.Verbs.Keys.First()];
        if (string.IsNullOrEmpty(operationVerb.Summary) && string.IsNullOrEmpty(operationVerb.Description))
            operationVerb.Description = serviceDescription.Description;

        OpenApiHelper.ExtractOpenApiParameters(interfaceMethod, operation, xmlDocumentationFiles, httpVerb == "get");
        var methodParameters = interfaceMethod.GetParameters();
        if (httpVerb != "get" && methodParameters.Length > 0)
        {
            operation.Payload = new OpenApiPayload
            {
                Type = methodParameters[0].ParameterType,
                Description = OpenApiHelper.GetDescription(methodParameters[0].ParameterType, xmlDocumentationFiles)
            };
            if (string.IsNullOrEmpty(operation.Payload.Description))
                operation.Payload.Description = OpenApiHelper.GetSummary(methodParameters[0].ParameterType, xmlDocumentationFiles);
        }

        return operation;
    }

    private static MCPParametersSchemaDefinition CreateMcpParametersSchema(MethodInfo interfaceMethod, Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles, JsonFormatModes jsonFormatMode)
    {
        var schema = new MCPParametersSchemaDefinition
        {
            Type = "object"
        };

        var methodParameters = interfaceMethod.GetParameters();
        var parameterType = methodParameters.Length > 0 ? methodParameters[0].ParameterType : null;

        // CODE Framework patterns typically use a single complex parameter ("input message") for service calls.
        // For MCP tools, we flatten that parameter into top-level tool arguments instead of exposing the
        // parameter object itself as a nested property bag.
        if (methodParameters.Length == 1 && parameterType != null)
        {
            var parameterProperties = parameterType.GetProperties();
            var parameterObjectInstance = TryCreateObjectInstance(parameterType);

            foreach (var parameterProperty in parameterProperties)
            {
                var propertyName = GetPublicPropertyName(parameterProperty.Name, jsonFormatMode);
                schema.Properties.Add(propertyName, CreateSchemaDefinition(parameterProperty.PropertyType, parameterProperty, OpenApiHelper.GetDescription(parameterProperty, xmlDocumentationFiles), jsonFormatMode, parameterObjectInstance));

                if (parameterProperty.GetCustomAttributeEx<DataMemberAttribute>()?.IsRequired == true)
                    schema.RequiredProperties.Add(propertyName);
            }

            return schema;
        }

        return schema;
    }

    private static MCPParametersSchemaDefinition CreateSchemaDefinition(Type type, PropertyInfo propertyInfo, string description, JsonFormatModes jsonFormatMode, object parentObject = null)
    {
        var schema = new MCPParametersSchemaDefinition
        {
            Description = description ?? string.Empty,
            DefaultValue = TryGetDefaultValue(parentObject, propertyInfo)
        };

        var normalizedType = type;
        if (normalizedType.IsGenericType && normalizedType.GetGenericTypeDefinition() == typeof(Task<>))
            normalizedType = normalizedType.GetGenericArguments()[0];
        if (normalizedType.IsGenericType && normalizedType.GetGenericTypeDefinition() == typeof(Nullable<>))
        {
            schema.Nullable = true;
            normalizedType = normalizedType.GetGenericArguments()[0];
        }

        var openApiType = OpenApiHelper.GetOpenApiType(normalizedType);
        if (!string.IsNullOrEmpty(openApiType))
        {
            schema.Type = openApiType;
            schema.Format = OpenApiHelper.GetOpenApiTypeFormat(normalizedType, propertyInfo);

            if (normalizedType.IsEnum)
            {
                foreach (var enumValue in OpenApiHelper.GetOpenApiEnumValues(normalizedType))
                    schema.EnumValues.Add(enumValue);

                var enumDescription = OpenApiHelper.GetOpenApiEnumDescription(normalizedType);
                if (!string.IsNullOrEmpty(enumDescription))
                    schema.Description = string.IsNullOrEmpty(schema.Description) ? enumDescription : $"{schema.Description} Enum values: {enumDescription}";
            }

            if (openApiType == "array")
            {
                var elementType = normalizedType.IsArray ? normalizedType.GetElementType() : normalizedType.GenericTypeArguments[0];
                schema.ArrayItemSchema = CreateSchemaDefinition(elementType, propertyInfo, null, jsonFormatMode);
            }
        }
        else
        {
            schema.Type = "object";

            var nestedObjectInstance = TryCreateObjectInstance(normalizedType);
            foreach (var nestedProperty in normalizedType.GetProperties())
            {
                var nestedDescription = nestedProperty.GetCustomAttributeEx<DescriptionAttribute>()?.Description ?? nestedProperty.GetCustomAttributeEx<System.ComponentModel.DescriptionAttribute>()?.Description;
                var propertyName = GetPublicPropertyName(nestedProperty.Name, jsonFormatMode);
                schema.Properties.Add(propertyName, CreateSchemaDefinition(nestedProperty.PropertyType, nestedProperty, nestedDescription, jsonFormatMode, nestedObjectInstance));

                if (nestedProperty.GetCustomAttributeEx<DataMemberAttribute>()?.IsRequired == true)
                    schema.RequiredProperties.Add(propertyName);
            }
        }

        return schema;
    }

    private static void WriteMcpSchema(Utf8JsonWriter writer, MCPParametersSchemaDefinition schema)
    {
        writer.WriteStartObject();
        writer.WriteString("type", schema.Type);

        if (!string.IsNullOrEmpty(schema.Format))
            writer.WriteString("format", schema.Format);
        if (schema.Nullable)
            writer.WriteBoolean("nullable", true);
        if (!string.IsNullOrEmpty(schema.Description))
            writer.WriteString("description", schema.Description);

        if (schema.EnumValues.Count > 0)
        {
            writer.WriteStartArray("enum");
            foreach (var enumValue in schema.EnumValues)
                WriteJsonValue(writer, enumValue);
            writer.WriteEndArray();
        }

        if (schema.DefaultValue != null)
        {
            writer.WritePropertyName("default");
            WriteJsonValue(writer, schema.DefaultValue);
        }

        if (schema.Type == "array" && schema.ArrayItemSchema != null)
        {
            writer.WritePropertyName("items");
            WriteMcpSchema(writer, schema.ArrayItemSchema);
        }

        if (schema.Type == "object")
        {
            if (schema.Properties.Count > 0)
            {
                writer.WritePropertyName("properties");
                writer.WriteStartObject();
                foreach (var property in schema.Properties)
                {
                    writer.WritePropertyName(property.Key);
                    WriteMcpSchema(writer, property.Value);
                }
                writer.WriteEndObject();
            }

            if (schema.RequiredProperties.Count > 0)
            {
                writer.WriteStartArray("required");
                foreach (var requiredProperty in schema.RequiredProperties)
                    writer.WriteStringValue(requiredProperty);
                writer.WriteEndArray();
            }

            writer.WriteBoolean("additionalProperties", false);
        }

        writer.WriteEndObject();
    }

    private static object TryCreateObjectInstance(Type type)
    {
        try
        {
            return ObjectHelper.CreateInstanceFromType(type);
        }
        catch
        {
            return null;
        }
    }

    private static object TryGetDefaultValue(object parentObject, PropertyInfo propertyInfo)
    {
        if (parentObject == null || propertyInfo == null) return null;

        try
        {
            return propertyInfo.GetValue(parentObject);
        }
        catch
        {
            return null;
        }
    }

    private static string GetPublicPropertyName(string propertyName, JsonFormatModes jsonFormatMode)
    {
        if (jsonFormatMode == JsonFormatModes.CamelCase)
            return StringHelper.CamelCase(propertyName);
        if (jsonFormatMode == JsonFormatModes.SnakeCase)
            return StringHelper.SnakeCase(propertyName);
        return propertyName;
    }

    private static void WriteJsonValue(Utf8JsonWriter writer, object value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string stringValue:
                writer.WriteStringValue(stringValue);
                break;
            case char charValue:
                writer.WriteStringValue(charValue.ToString());
                break;
            case Guid guidValue:
                writer.WriteStringValue(guidValue.ToString());
                break;
            case DateTime dateTimeValue:
                writer.WriteStringValue(dateTimeValue);
                break;
            case bool boolValue:
                writer.WriteBooleanValue(boolValue);
                break;
            case byte byteValue:
                writer.WriteNumberValue(byteValue);
                break;
            case sbyte sbyteValue:
                writer.WriteNumberValue(sbyteValue);
                break;
            case short shortValue:
                writer.WriteNumberValue(shortValue);
                break;
            case ushort ushortValue:
                writer.WriteNumberValue(ushortValue);
                break;
            case int intValue:
                writer.WriteNumberValue(intValue);
                break;
            case uint uintValue:
                writer.WriteNumberValue(uintValue);
                break;
            case long longValue:
                writer.WriteNumberValue(longValue);
                break;
            case ulong ulongValue:
                writer.WriteNumberValue(ulongValue);
                break;
            case float floatValue:
                writer.WriteNumberValue(floatValue);
                break;
            case double doubleValue:
                writer.WriteNumberValue(doubleValue);
                break;
            case decimal decimalValue:
                writer.WriteNumberValue(decimalValue);
                break;
            default:
                if (value.GetType().IsEnum)
                    writer.WriteNumberValue(Convert.ToInt64(value));
                else
                    JsonSerializer.Serialize(writer, value, value.GetType(), _jsonSerializerOptions);
                break;
        }
    }

    /// <summary>
    /// Enabled CODE Framework Open API support.
    /// </summary>
    /// <param name="appBuilder">App builder</param>
    /// <param name="supportOpenApiJson">If true, the OpenAPI JSON will be generated and served at the specified route</param>
    /// <param name="openApiJsonRoute">The route at which the OpenAPI JSON will be served</param>
    /// <param name="info">Optional OpenAPI information</param>
    /// <returns></returns>
    public static IApplicationBuilder UseOpenApiHandler(this IApplicationBuilder appBuilder, bool supportOpenApiJson = true, string openApiJsonRoute = "openapi.json", OpenApiInfo info = null)
    {
        SwaggerRoutes ??=
            [
                "/swagger", // We assume swagger API is used when OpenAPI features are on. This makes sure that the general handler does not eat up other service routes
                "/swagger/index.html"
            ];

        var serviceConfig = ServiceHandlerConfiguration.Current ?? throw new Exception("CODE Framework hosted services must be configured before UseOpenApiHandler() can be called. Use AddHostedServices() to configure which services are to be present in the hosting environment.");

        // Endpoints require routing, so we make sure it is there
        appBuilder.UseRouting();

        appBuilder.UseEndpoints(endpoints =>
        {
            appBuilder.MapWhen(
                                context =>
                                {
                                    var requestPath = context.Request.Path.ToString().Trim().ToLowerInvariant();
                                    var openApiFullRoute = !string.IsNullOrEmpty(openApiJsonRoute) ? openApiJsonRoute : "/openapi.json";
                                    if (!openApiFullRoute.StartsWith('/'))
                                        openApiFullRoute = $"/{openApiFullRoute}";
                                    openApiFullRoute = openApiFullRoute.Trim().ToLowerInvariant();
                                    return requestPath == openApiFullRoute;
                                },
                                builder =>
                                {
                                    // Build up route mapping
                                    builder.UseRouter(routeBuilder =>
                                    {
                                        var openApiFullRoute = !string.IsNullOrEmpty(openApiJsonRoute) ? openApiJsonRoute : "/openapi.json";
                                        if (!openApiFullRoute.StartsWith('/'))
                                            openApiFullRoute = $"/{openApiFullRoute}";
                                        routeBuilder.MapVerb("GET", openApiFullRoute, GetOpenApiJson(serviceConfig.Services, info));
                                        SwaggerRoutes.Add(openApiFullRoute);

                                        // TODO: Add openapi.yaml support?
                                    });
                                });
        });

        return appBuilder;
    }

    private static List<string> SwaggerRoutes { get; set; }

    private static Func<HttpRequest, HttpResponse, RouteData, Task> GetOpenApiJson(List<ServiceHandlerConfigurationInstance> serviceInstanceConfigurations, OpenApiInfo info = null) => async (req, resp, route) =>
    {
        resp.ContentType = "application/json; charset=utf-8";

        info ??= new OpenApiInfo();
        var openApiInfo = new OpenApiInformation { Info = info };
        var serviceDescriptions = GetHostedServiceDescriptions(serviceInstanceConfigurations);
        var xmlDocumentationFiles = GetXmlDocumentationFiles(serviceDescriptions);

        foreach (var serviceDescription in serviceDescriptions)
        {
            if (string.IsNullOrEmpty(info.Version))
                info.Version = serviceDescription.ContractType.Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion;

            openApiInfo.Tags.Add(new OpenApiTag
            {
                Name = serviceDescription.Configuration.ServiceType.Name,
                Description = serviceDescription.Description,
                ExternalDocs = serviceDescription.ExternalDocs
            });

            foreach (var operation in serviceDescription.Operations)
            {
                operation.Tags.Add(new OpenApiTag { Name = serviceDescription.Configuration.ServiceType.Name });

                var returnTypeOpenApiString = OpenApiHelper.GetOpenApiType(operation.ReturnType);
                if (string.IsNullOrEmpty(returnTypeOpenApiString))
                    OpenApiHelper.AddTypeToComponents(openApiInfo, operation.ReturnType, operation.Obsolete, operation.ObsoleteReason, xmlDocumentationFiles, serviceDescription.Configuration.JsonFormatMode);

                if (operation.HttpVerb != "get" && operation.Payload != null)
                    OpenApiHelper.AddTypeToComponents(openApiInfo, operation.Payload.Type, false, string.Empty, xmlDocumentationFiles, serviceDescription.Configuration.JsonFormatMode);

                var fullRouteKey = $"{operation.HttpVerb}::{operation.Route}";
                if (!openApiInfo.Paths.ContainsKey(fullRouteKey))
                    openApiInfo.Paths.Add(fullRouteKey, operation);
                else
                    throw new Exception($"Duplicate path/route in service definition.{Environment.NewLine}Route: {operation.Route}");
            }
        }

        await JsonSerializer.SerializeAsync(resp.Body, openApiInfo, typeof(OpenApiInformation), _jsonSerializerOptions);
    };

    private static Dictionary<Assembly, XmlCodeDocumentationFile> GetXmlDocumentationFiles(IEnumerable<HostedServiceDescription> serviceDescriptions)
    {
        var xmlDocumentationFiles = new Dictionary<Assembly, XmlCodeDocumentationFile>();
        foreach (var serviceDescription in serviceDescriptions)
            if (!xmlDocumentationFiles.ContainsKey(serviceDescription.ContractType.Assembly))
                xmlDocumentationFiles.Add(serviceDescription.ContractType.Assembly, new XmlCodeDocumentationFile(serviceDescription.ContractType.Assembly));
        return xmlDocumentationFiles;
    }

    private static JsonSerializerOptions _jsonSerializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    /// <summary>
    /// Extracts the RestAttribute from a propertyInfo's attributes
    /// </summary>
    /// <param name="method">The method-info to be inspected</param>
    /// <returns>The applied RestAttribute or a default RestAttribute.</returns>
    public static RestAttribute GetRestAttribute(MethodInfo method)
    {
        var customAttributes = method.GetCustomAttributes(typeof(RestAttribute), true);
        if (customAttributes.Length <= 0) return new RestAttribute();
        var restAttribute = customAttributes[0] as RestAttribute;
        return restAttribute ?? new RestAttribute();
    }

    /// <summary>
    /// Extracts the RestUrlParameterAttribute from a propertyInfo's attributes
    /// </summary>
    /// <param name="propertyInfo">The property-info to be inspected</param>
    /// <returns>The applied RestUrlParameterAttribute or a default RestUrlParameterAttribute.</returns>
    public static RestUrlParameterAttribute GetRestUrlParameterAttribute(PropertyInfo propertyInfo)
    {
        var customAttributes = propertyInfo.GetCustomAttributes(typeof(RestUrlParameterAttribute), true);
        if (customAttributes.Length <= 0) return null;
        return customAttributes[0] as RestUrlParameterAttribute;
    }

    private static IEnumerable<string> GetSortedInlineParameterNames(IEnumerable<PropertyInfo> propertyInfos)
    {
        var list = new List<PropertyInfoHelper>();
        foreach (var propertyInfo in propertyInfos)
        {
            var attribute = GetRestUrlParameterAttribute(propertyInfo);
            if (attribute == null) continue;
            if (attribute.Mode == UrlParameterMode.Inline)
                list.Add(new PropertyInfoHelper { Name = propertyInfo.Name, Order = attribute.Sequence });
        }

        return list.OrderBy(a => a.Order).Select(a => a.Name);
    }

    private class PropertyInfoHelper
    {
        public string Name { get; set; }
        public int Order { get; set; }
    }
}