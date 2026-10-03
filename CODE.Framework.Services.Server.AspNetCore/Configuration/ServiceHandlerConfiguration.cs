using CODE.Framework.Services.Server.AspNetCore.Properties;
using System.Threading.Tasks;

namespace CODE.Framework.Services.Server.AspNetCore.Configuration;

/// <summary>
/// Overall Service Handler configuration that contains individual 
/// service configuration for each service instance in the Servers
/// property.
/// </summary>
public class ServiceHandlerConfiguration
{
    /// <summary>
    /// Configuration Singleton you can use in lieu of Dependency Injection
    /// of IOptions injection
    /// </summary>
    public static ServiceHandlerConfiguration Current { get; set; }

    /// <summary>
    /// Holds a list of services that are configured to be handled.
    /// </summary>
    public List<ServiceHandlerConfigurationInstance> Services { get; set; } = new List<ServiceHandlerConfigurationInstance>();

    /// <summary>
    /// CORS specific configuration settings for the entire service
    /// </summary>
    public ServiceHandlerCorsConfiguration Cors { get; set; } = new ServiceHandlerCorsConfiguration();
}

/// <summary>
/// Configuration object for each individual configured Service.
/// </summary>
public class ServiceHandlerConfigurationInstance
{
    private string _displayName;
    private string _displayVersion;

    /// <summary>
    /// You can specify a specific type to bind rather than
    /// providing 
    /// </summary>
    public Type ServiceType { get; set; }

    /// <summary>
    /// Returns ServiceType or loads it from the ServiceTypeName and AssemblyName if ServiceType is null.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="ArgumentException"></exception>
    public Type GetInstantiatedServiceType()
    {
        if (ServiceType != null) return ServiceType;

        ServiceType = ObjectHelper.GetTypeFromName(ServiceTypeName);
        if (ServiceType == null)
        {
            var assemblyNameWithPath = AssemblyName;
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
                throw new ArgumentException(string.Format(Resources.InvalidServiceType, ServiceTypeName));
            ServiceType = ObjectHelper.GetTypeFromName(ServiceTypeName) ?? throw new ArgumentException(string.Format(Resources.InvalidServiceType, ServiceTypeName));
        }
        return ServiceType;
    }

    /// <summary>
    /// If you can't provide a type instance you can provide
    /// the type as string and get it dynamically loaded.
    /// Use fully qualified typename (namespace.typename)        
    /// You have to also specify the AssemblyName
    /// </summary>
    public string ServiceTypeName { get; set; }


    /// <summary>
    /// If specifying a type name you also have to specify the
    /// name of the assembly to load. Specify only the type.
    /// </summary>
    public string AssemblyName { get; set; }

    /// <summary>
    /// The base route to access this service instance
    /// Example: /api/users
    /// </summary>
    public string RouteBasePath { get; set; } = string.Empty;

    public ControllerHttpsMode HttpsMode { get; set; } = ControllerHttpsMode.Http;

    /// <summary>
    /// Determines how property names are rendered using CamelCase or ProperCase
    /// </summary>
    public JsonFormatModes JsonFormatMode { get; set; } = JsonFormatModes.CamelCase;
    
    /// <summary>
    /// Optional hook method fired before the service method
    /// is invoked. Method signature is async.
    /// </summary>
    public Func<ServiceHandlerRequestContext, Task> OnBeforeMethodInvoke { get; set; }

    /// <summary>
    /// Called to potentially check and handle authentication tasks.
    /// Return true to allow request through, otherwise return false
    /// </summary>
    /// <return>Return true or false to allow request through. When true request is further checked by attribute based authorization</return>
    public Func<ServiceHandlerRequestContext, Task<bool>> OnAuthorize { get; set; }
    
    /// <summary>
    /// Optional hook method fired after the service method is
    /// is invoked. Method signature is async.
    /// </summary>
    public Func<ServiceHandlerRequestContext, Task> OnAfterMethodInvoke { get; set; }

    /// <summary>
    /// If this service is hosted as an MCP server, this is the base path for the MCP route. Default is /mcp
    /// </summary>
    public string MCPRouteBasePath { get; set; } = "/mcp";

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrEmpty(_displayName)) return _displayName;
            
            var serviceType = GetInstantiatedServiceType();
            if (serviceType != null)
                _displayName = serviceType.Name;

            return _displayName;
        }

        set => _displayName = value;
    }

    /// <summary>
    /// Gets or sets the version display string for the service. 
    /// If not set, it will attempt to retrieve the version from the service type's assembly. 
    /// If that fails, it defaults to "1.0.0".
    /// </summary>
    public string DisplayVersion
    {
        get
        {
            if (!string.IsNullOrEmpty(_displayVersion)) return _displayVersion;

            if (ServiceType != null)
                _displayVersion = ServiceType.Assembly.GetName().Version?.ToString();

            if (string.IsNullOrEmpty(_displayVersion)) return "1.0.0";
            return _displayVersion;
        }

        set => _displayVersion = value;
    }
}

public enum JsonFormatModes
{
    CamelCase,
    ProperCase,
    [Obsolete("snake_case is not currently supported")]
    SnakeCase
}

public enum ControllerHttpsMode
{
    Undefined,
    Http,
    RequireHttps,
    RequireHttpsExceptLocalhost
}