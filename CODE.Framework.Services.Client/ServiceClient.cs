namespace CODE.Framework.Services.Client;

using System.Threading.Tasks;

public static class ServiceClient
{
    static ServiceClient()
    {
        if (GetSetting("ServiceClient:" + nameof(LogCommunicationErrors)).ToLowerInvariant() == "true")
            LogCommunicationErrors = true;
    }

    /// <summary>Internal message size cache</summary>
    private static readonly Dictionary<string, MessageSize> CachedMessageSizes = [];

    /// <summary>Cached protocols</summary>
    private static readonly Dictionary<string, Protocol> CachedProtocols = [];

    /// <summary>The cached service ids</summary>
    private static readonly Dictionary<Type, string> CachedServiceIds = [];

    /// <summary>Internal settings cache</summary>
    private static readonly Dictionary<string, string> SettingsCache = [];

    // Keep channel configuration resolution atomic with cache invalidation. Network calls
    // occur on the returned channel, outside this lock.
    private static readonly object ConfigurationCacheLock = new();

    /// <summary>
    /// Indicates whether configuration settings should be cached (default = yes)
    /// </summary>
    /// <value><c>true</c> if [cache settings]; otherwise, <c>false</c>.</value>
    /// <remarks>
    /// When settings get cached, they are only retrieved from the configuration system
    /// the first time they are needed. Subsequent uses will be based on the cache, which improves performance.
    /// However, if settings are meant to change dynamically, caching needs to be turned off, otherwise
    /// new configuration settings will be ignored by the service client.
    /// </remarks>
    public static bool CacheSettings { get; set; } = true;

    /// <summary>
    /// Opens a channel to the service by means of a transparently generated proxy and then performs the provided action on that call.
    /// </summary>
    /// <typeparam name="TServiceType">Service contract (interface) to call.</typeparam>
    /// <param name="action">Code to run once the connection is established</param>
    /// <param name="restServiceUrl">Optional URL for configuration-less calling of REST services. (Note: The standard approach is to not use this parameter and configure the system for the call)</param>
    /// <example>
    /// ServiceClient.Call<ISearchService>(s => {
    ///     var response = s.Search(new SearchRequest { SearchText = "Hello World" });
    ///     if (response.Success)
    ///         Console.WriteLine(response.Results);
    /// });
    /// </ISearchService>
    /// </example>
    public static void Call<TServiceType>(Action<TServiceType> action, string restServiceUrl = null) where TServiceType : class
    {
        var channel = GetChannelDedicated<TServiceType>(restServiceUrl);
        if (channel == null) return; // Exception event has fired by now, so callers can see what happened internally

        try
        {
            action(channel);
        }
        catch (Exception ex)
        {
            if (MustRetryCall(ex))
            {
                channel = GetChannelDedicated<TServiceType>(restServiceUrl);
                try
                {
                    action(channel);
                }
                catch
                {
                    // TODO: Do we need this for REST? -- AbortChannel(channel, ex2);
                }
            }
        }
    }

    /// <summary>Calls a service with explicit failure handling and per-operation retry options.</summary>
    /// <remarks>Unlike the legacy overload, defaults to propagating exceptions and ignores the global legacy retry settings. Cancellation always propagates. The callback runs once; only explicitly repeatable REST operations can be retried.</remarks>
    public static void Call<TServiceType>(ServiceCallOptions options, Action<TServiceType> action, string restServiceUrl = null) where TServiceType : class
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (action == null) throw new ArgumentNullException(nameof(action));
        var snapshot = options.Snapshot();
        snapshot.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            var channel = CreateOptionsChannel<TServiceType>(snapshot, restServiceUrl, false);
            action(channel);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch when (snapshot.ExceptionBehavior == ServiceExceptionBehavior.Suppress)
        {
            // Explicit opt-in to suppression; the callback is never replayed.
        }
    }

    /// <summary>Awaits a callback using Task&lt;TResponse&gt; contract operations. Failures propagate by default; no retries are enabled.</summary>
    public static Task CallAsync<TServiceType>(Func<TServiceType, Task> action, string restServiceUrl = null) where TServiceType : class
        => CallAsync(new ServiceCallOptions(), action, restServiceUrl);

    /// <summary>Awaits a callback with explicit cancellation, exception, and per-operation REST retry options.</summary>
    /// <remarks>The callback starts on the calling context and runs once. Await its service operations; do not start unobserved work. Cancellation always propagates.</remarks>
    public static async Task CallAsync<TServiceType>(ServiceCallOptions options, Func<TServiceType, Task> action, string restServiceUrl = null) where TServiceType : class
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (action == null) throw new ArgumentNullException(nameof(action));
        var snapshot = options.Snapshot();
        snapshot.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            var channel = CreateOptionsChannel<TServiceType>(snapshot, restServiceUrl, true);
            await action(channel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch when (snapshot.ExceptionBehavior == ServiceExceptionBehavior.Suppress) { }
    }

    /// <summary>Awaits a callback and returns its result. Contract operations must return Task&lt;TResponse&gt;.</summary>
    public static Task<TResult> CallAsync<TServiceType, TResult>(Func<TServiceType, Task<TResult>> action, string restServiceUrl = null) where TServiceType : class
        => CallAsync(new ServiceCallOptions(), action, restServiceUrl);

    /// <summary>Awaits a result-producing callback with explicit service-call options.</summary>
    /// <remarks>Explicit suppression returns default(TResult) on failure. Cancellation always propagates. Legacy retry settings do not apply.</remarks>
    public static async Task<TResult> CallAsync<TServiceType, TResult>(ServiceCallOptions options, Func<TServiceType, Task<TResult>> action, string restServiceUrl = null) where TServiceType : class
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (action == null) throw new ArgumentNullException(nameof(action));
        var snapshot = options.Snapshot();
        snapshot.CancellationToken.ThrowIfCancellationRequested();
        try
        {
            var channel = CreateOptionsChannel<TServiceType>(snapshot, restServiceUrl, true);
            return await action(channel).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch when (snapshot.ExceptionBehavior == ServiceExceptionBehavior.Suppress) { return default; }
    }

    private static TServiceType CreateOptionsChannel<TServiceType>(ServiceCallOptions snapshot, string restServiceUrl, bool asyncOperations) where TServiceType : class
    {
        lock (ConfigurationCacheLock)
        {
            switch (GetProtocol<TServiceType>())
            {
                case Protocol.RestHttpJson:
                    var url = string.IsNullOrEmpty(restServiceUrl) ? GetSetting($"RestServiceUrl:{GetServiceId<TServiceType>()}") : restServiceUrl;
                    snapshot.ApiKey ??= GetSetting($"RestServiceApiKey:{GetServiceId<TServiceType>()}");
                    snapshot.ApiKeyHeaderName ??= GetSetting($"RestServiceApiKeyHeaderName:{GetServiceId<TServiceType>()}", defaultValue: "X-Api-Key");
                    return TransparentProxyGenerator.GetProxy<TServiceType>(new StrictRestProxyHandler(new Uri(url), snapshot, asyncOperations));
                case Protocol.InProcess:
                    var service = ServiceGardenLocal.GetService<TServiceType>() ?? throw new InvalidOperationException($"No in-process service is registered for {typeof(TServiceType).FullName}.");
                    return TransparentProxyGenerator.GetProxy<TServiceType>(new StrictInProcessProxyHandler(service, snapshot.CancellationToken, asyncOperations));
                default:
                    throw new NotSupportedException($"The configured protocol for {typeof(TServiceType).FullName} is not supported by the options-based client.");
            }
        }
    }

    public static bool AutoRetryFailedCalls { get; set; } = false;

    /// <summary>Defines the delay (milliseconds) between auto-retry calls</summary>
    /// <remarks>The delay is defined in milliseconds (-1 = no delay). Note that the delay puts the thread to sleep, so this should not be done on foreground threads.</remarks>
    public static int AutoRetryDelay { get; set; } = -1;

    /// <summary>Defines exact exception types for legacy callback retries. Null or empty retries any callback exception once.</summary>
    public static List<Type> AutoRetryFailedCallsForExceptions { get; set; } // TODO: = new List<Type> { typeof (EndpointNotFoundException), typeof (ServerTooBusyException) };

    /// <summary>When set to true (non-default), service communication errors are forwarded to the logging mediator.</summary>
    public static bool LogCommunicationErrors { get; set; } = false;

    /// <summary>Determines whether a call needs to be auto-retried</summary>
    /// <param name="exception">The exception that caused the original failure.</param>
    /// <returns>True if the call should be retried</returns>
    private static bool MustRetryCall(Exception exception)
    {
        if (!AutoRetryFailedCalls) return false;
        var exceptionTypes = AutoRetryFailedCallsForExceptions;
        if (exceptionTypes != null && exceptionTypes.Count > 0 && !exceptionTypes.Any(exceptionType => exception.GetType() == exceptionType)) return false;
        if (AutoRetryDelay > 0) Thread.Sleep(AutoRetryDelay);
        return true;
    }

    /// <summary>
    /// Gets a dedicated channel to a data contract.
    /// </summary>
    /// <typeparam name="TServiceType">The type of the service type.</typeparam>
    /// <param name="restUrl">Optional URL for a rest service call. (If not passed, will be retrieved from settings, which is the usual scenario)</param>
    /// <returns>Operations service</returns>
    /// <example>
    /// var service = ServiceClient.GetChannelDedicated&lt;IUserService&gt;();
    /// var result = service.GetUsers();
    /// ServiceClient.CloseChannel(service);
    /// </example>
    /// <remarks>
    /// Relies on service configurations to figure out which protocol (and so forth) to use for the desired service.
    /// Creates a channel exclusive to this caller. It is up to the caller to close the channel after use!
    /// </remarks>
    public static TServiceType GetChannelDedicated<TServiceType>(string restUrl = null) where TServiceType : class
    {
        lock (ConfigurationCacheLock)
            return CreateChannel<TServiceType>(restUrl);
    }

    private static TServiceType CreateChannel<TServiceType>(string restUrl) where TServiceType : class
    {
        switch (GetProtocol<TServiceType>())
        {
#if FULLFRAMEWORK
            case Protocol.NetTcp:
                var port = GetServicePort<TServiceType>();
                var serviceId = GetServiceId<TServiceType>();
                return GetChannelDedicated<TServiceType>(port, serviceId, protocol, GetMessageSize<TServiceType>());

            case Protocol.BasicHttp:
            case Protocol.WsHttp:
                var serviceId2 = GetServiceId<TServiceType>();
                return GetChannelDedicated<TServiceType>(serviceId2, protocol, GetMessageSize(serviceId2));
#endif

            case Protocol.InProcess:
                return ServiceGardenLocal.GetService<TServiceType>();
            case Protocol.RestHttpJson:
                var serviceId3 = GetServiceId<TServiceType>();
                var restUri = string.IsNullOrEmpty(restUrl) ? new Uri(GetSetting($"RestServiceUrl:{serviceId3}")) : new Uri(restUrl);
                var restHandler = new RestProxyHandler(restUri)
                {
                    ApiKey = GetSetting($"RestServiceApiKey:{serviceId3}"),
                    ApiKeyHeaderName = GetSetting($"RestServiceApiKeyHeaderName:{serviceId3}", defaultValue: "X-Api-Key")
                };
                var proxy = TransparentProxyGenerator.GetProxy<TServiceType>(restHandler);
                return proxy;
            default:
                return default;
        }
    }

    /// <summary>
    /// Clears a cached setting and its derived protocol selection, or all client configuration caches.
    /// </summary>
    /// <param name="setting">Setting to clear (if not provided, all settings are cleared).</param>
    /// <remarks>
    /// Clearing ServiceProtocol invalidates all resolved protocols; contract overrides remain in effect.
    /// Clearing ServiceProtocol:ContractName invalidates only that contract's resolved protocol.
    /// Other named settings leave unrelated cached settings intact. Null or empty clears all caches.
    /// Existing channels and generated proxy types are unaffected; new channels use refreshed settings.
    /// This does not reload properties initialized once from configuration, such as LogCommunicationErrors.
    /// </remarks>
    public static void ClearSettingsCache(string setting = null)
    {
        lock (ConfigurationCacheLock)
        {
            if (string.IsNullOrEmpty(setting))
            {
                lock (SettingsCache)
                    SettingsCache.Clear();
                lock (CachedProtocols)
                    CachedProtocols.Clear();
                lock (CachedServiceIds)
                    CachedServiceIds.Clear();
                lock (CachedMessageSizes)
                    CachedMessageSizes.Clear();
            }
            else
            {
                lock (SettingsCache)
                    SettingsCache.Remove(setting);
                lock (CachedProtocols)
                {
                    if (setting == "ServiceProtocol")
                        CachedProtocols.Clear();
                    else if (setting.StartsWith("ServiceProtocol:", StringComparison.Ordinal))
                        CachedProtocols.Remove(setting);
                }
            }
        }
    }

    /// <summary>
    /// Gets the protocol for the specified service.
    /// </summary>
    /// <typeparam name="TServiceType">Type of the service (contract).</typeparam>
    /// <returns>Protocol.</returns>
    private static Protocol GetProtocol<TServiceType>()
    {
        var serviceType = typeof(TServiceType);
        var interfaceName = serviceType.Name;
        var key = "ServiceProtocol:" + interfaceName;

        lock (CachedProtocols)
            if (CacheSettings && CachedProtocols.ContainsKey(key))
                return CachedProtocols[key];

        var protocolName = GetSetting(key).ToLower(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(protocolName)) protocolName = GetSetting("ServiceProtocol").ToLower(CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(protocolName)) return Protocol.RestHttpJson; // This default has changed since the full framework implementation, where it was TCP/IP

        var protocol = protocolName switch
        {
            "inprocess" => Protocol.InProcess,
            "wshttp" => Protocol.WsHttp,
            "basichttp" => Protocol.BasicHttp,
            "rest" or "restjson" or "resthttpjson" => Protocol.RestHttpJson,
            "restxml" or "resthttpxml" => Protocol.RestHttpXml,
            _ => Protocol.NetTcp,
        };
        if (CacheSettings)
            lock (CachedProtocols)
                if (CachedProtocols.ContainsKey(key))
                    CachedProtocols[key] = protocol;
                else
                    CachedProtocols.Add(key, protocol);

        return protocol;
    }

    /// <summary>
    /// Gets the service id.
    /// </summary>
    /// <typeparam name="TServiceType">The type of the service type.</typeparam>
    /// <returns></returns>
    private static string GetServiceId<TServiceType>()
    {
        lock (CachedServiceIds)
            if (CacheSettings && CachedServiceIds.ContainsKey(typeof(TServiceType)))
                return CachedServiceIds[typeof(TServiceType)];

        string serviceId;
        var contractType = typeof(TServiceType);
        if (contractType.IsInterface)
            serviceId = contractType.Name;
        else
        {
            var interfaces = contractType.GetInterfaces();
            if (interfaces.Length == 1)
                serviceId = interfaces[0].Name;
            else if (interfaces.Length == 2)
            {
                if (interfaces[0].FullName == "CODE.Framework.Services.Contracts.IServiceEvents")
                    serviceId = interfaces[1].Name;
                else if (interfaces[1].FullName == "CODE.Framework.Services.Contracts.IServiceEvents")
                    serviceId= interfaces[0].Name;
                else
                    throw new IndexOutOfBoundsException("Service information must be the service contract, not the service implementation type.");
            }
            else
                throw new IndexOutOfBoundsException("Service information must be the service contract, not the service implementation type.");
        }

        if (CacheSettings)
            lock (CachedServiceIds)
                if (CachedServiceIds.ContainsKey(typeof(TServiceType)))
                    CachedServiceIds[typeof(TServiceType)] = serviceId;
                else
                    CachedServiceIds.Add(typeof(TServiceType), serviceId);

        return serviceId;
    }

    /// <summary>
    /// Retrieves a setting from the configuration system
    /// </summary>
    /// <param name="setting">Name of the setting.</param>
    /// <param name="ignoreCache">If set to <c>true</c> setting caching is ignored.</param>
    /// <param name="defaultValue">Default value in case the setting is not found</param>
    /// <returns>Setting value</returns>
    private static string GetSetting(string setting, bool ignoreCache = false, string defaultValue = "")
    {
        var settingValue = defaultValue;

        if (CacheSettings && !ignoreCache)
            lock (SettingsCache)
                if (SettingsCache.ContainsKey(setting))
                    return SettingsCache[setting];

        if (ConfigurationSettings.Settings.IsSettingSupported(setting))
            settingValue = ConfigurationSettings.Settings[setting];

        if (CacheSettings && !ignoreCache)
            lock (SettingsCache)
                if (SettingsCache.ContainsKey(setting))
                    SettingsCache[setting] = settingValue;
                else
                    SettingsCache.Add(setting, settingValue);

        return settingValue;
    }
}

/// <summary>
/// Communication Protocol
/// </summary>
public enum Protocol
{
    /// <summary>
    /// Net TCP
    /// </summary>
    NetTcp,
    /// <summary>
    /// Local in process service
    /// </summary>
    InProcess,
    /// <summary>
    /// Basic HTTP
    /// </summary>
    BasicHttp,
    /// <summary>
    /// WS HTTP
    /// </summary>
    WsHttp,
    /// <summary>
    /// XML Formatted REST over HTTP
    /// </summary>
    RestHttpXml,
    /// <summary>
    /// JSON Formatted REST over HTTP
    /// </summary>
    RestHttpJson
}

/// <summary>
/// Message size
/// </summary>
public enum MessageSize
{
    /// <summary>
    /// Normal (default message size as defined by WCF)
    /// </summary>
    Normal,
    /// <summary>
    /// Large (up to 100MB)
    /// </summary>
    Large,
    /// <summary>
    /// Medium (up to 10MB) - this is the default
    /// </summary>
    Medium,
    /// <summary>
    /// For internal use only
    /// </summary>
    Undefined,
    /// <summary>
    /// Very large (up to 1GB)
    /// </summary>
    VeryLarge,
    /// <summary>
    /// Maximum size (equal to int.MaxValue, about 2GB)
    /// </summary>
    Max
}

/// <summary>
/// Exception class used for enumeration errors.
/// The error is raised when an enumeration finds its enumeration source in disarray
/// and thus overshoots the sources bounds
/// </summary>
[Serializable]
public class IndexOutOfBoundsException : Exception
{
    /// <summary>
    /// Default Constructor.
    /// </summary>
    public IndexOutOfBoundsException() : base("Index out of bounds") { }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="message">Exception message</param>
    public IndexOutOfBoundsException(string message) : base(message) { }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">Inner exception.</param>
    public IndexOutOfBoundsException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>
    /// Constructor.
    /// </summary>
    /// <param name="info">Serialization information.</param>
    /// <param name="context">Streaming context.</param>
    protected IndexOutOfBoundsException(SerializationInfo info, StreamingContext context) : base(info, context) { }
}
