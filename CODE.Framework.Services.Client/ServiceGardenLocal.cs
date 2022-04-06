namespace CODE.Framework.Services.Client;

/// <summary>
/// Provides an in-process service garden
/// </summary>
public static class ServiceGardenLocal
{
    /// <summary>
    /// Collection of known hosts
    /// </summary>
    /// <value>Hosts</value>
    private static Dictionary<Type, object> Hosts { get; } = new Dictionary<Type, object>();

    private static bool _useDependencyInjection = false;
    private static readonly Dictionary<Type, Type> _transientDependencies = new Dictionary<Type, Type>();
    private static readonly Dictionary<Type, SingletonDependencyWrapper> _singletonDependencies = new Dictionary<Type, SingletonDependencyWrapper>();

    private class SingletonDependencyWrapper
    {
        public Type Type { get; set; }
        public object Instance { get; set; }
    }

    /// <summary>
    /// Injects a transient dependency to tell the service garden to use a certain concrete class whenever a interface is injected as a constructor parameter
    /// </summary>
    /// <remarks>
    /// This method is identical to InjectTransientDependency();
    /// 
    /// Transient dependencies will be created, and re-created whenever needed.
    /// Note that in an in-process system, transient dependencies behave very similar to singletons, except that different classes using the
    /// same injected dependencies will get their own instance of the concrete type.
    /// </remarks>
    /// <typeparam name="TInterface">Injected interface</typeparam>
    /// <typeparam name="TConcrete">Concrete type to be used whenever the interface is desired</typeparam>
    /// <example>
    /// // Use FakeUserProvider whenever a class wants to use IUserProvider
    /// ServiceGardenLocal.InjectDependency<IUserProvider, FakeUserProvider>();
    /// 
    /// // Could be used by a class like this (details truncated for clarity)
    /// public class Example
    /// {
    ///     public Example(IUserProvider _userProvider) { } // Constructor dependency injection
    /// }
    /// </example>
    public static void InjectDependency<TInterface, TConcrete>() where TConcrete : new() => InjectTransientDependency<TInterface, TConcrete>();

    /// <summary>
    /// Injects a transient dependency to tell the service garden to use a certain concrete class whenever a interface is injected as a constructor parameter
    /// </summary>
    /// <remarks>
    /// Transient dependencies will be created, and re-created whenever needed.
    /// Note that in an in-process system, transient dependencies behave very similar to singletons, except that different classes using the
    /// same injected dependencies will get their own instance of the concrete type.
    /// </remarks>
    /// <typeparam name="TInterface">Injected interface</typeparam>
    /// <typeparam name="TConcrete">Concrete type to be used whenever the interface is desired</typeparam>
    /// <example>
    /// // Use FakeUserProvider whenever a class wants to use IUserProvider
    /// ServiceGardenLocal.InjectTransientDependency<IUserProvider, FakeUserProvider>();
    /// 
    /// // Could be used by a class like this (details truncated for clarity)
    /// public class Example
    /// {
    ///     public Example(IUserProvider _userProvider) { } // Constructor dependency injection
    /// }
    /// </example>
    public static void InjectTransientDependency<TInterface, TConcrete>() where TConcrete : new()
    {
        if (!typeof(TConcrete).GetInterfaces().Contains(typeof(TInterface))) return; // TODO: Throw an exception!
        _transientDependencies.Add(typeof(TInterface), typeof(TConcrete));
        _useDependencyInjection = true;
    }

    /// <summary>
    /// Injects a singleton dependency to tell the service garden to use a certain concrete class whenever a interface is injected as a constructor parameter
    /// </summary>
    /// <remarks>
    /// Singleton dependency will be created the first time they are needed. Then, that instance is reused across the entire app.
    /// </remarks>
    /// <typeparam name="TInterface">Injected interface</typeparam>
    /// <typeparam name="TConcrete">Concrete type to be used whenever the interface is desired</typeparam>
    /// <example>
    /// // Use FakeUserProvider whenever a class wants to use IUserProvider
    /// ServiceGardenLocal.InjectSingletonDependency<IUserProvider, FakeUserProvider>();
    /// 
    /// // Could be used by a class like this (details truncated for clarity)
    /// public class Example
    /// {
    ///     // Constructor dependency injection
    ///     public Example(IUserProvider _userProvider) { }
    /// }
    /// 
    /// // Another class coudl also use it
    /// public class Example2
    /// {
    ///     // Constructor dependency injection gets the same object instance as the Example class above
    ///     public Example2(IUserProvider _userProvider) { } 
    /// }
    /// </example>
    public static void InjectSingletonDependency<TInterface, TConcrete>() where TConcrete : new()
    {
        if (!typeof(TConcrete).GetInterfaces().Contains(typeof(TInterface))) return; // TODO: Throw an exception!
        _singletonDependencies.Add(typeof(TInterface), new SingletonDependencyWrapper { Type = typeof(TConcrete) });
        _useDependencyInjection = true;
    }

    /// <summary>
    /// Adds a local service based on the services type
    /// </summary>
    /// <param name="serviceType">Type of the service.</param>
    /// <returns>True if successful</returns>
    /// <remarks>The interface used by the service is automatically determined.</remarks>
    /// <example>
    /// ServiceGardenLocal.AddServiceHost(typeof(MyNamespace.CustomerService));
    /// </example>
    public static bool AddServiceHost(Type serviceType)
    {
        Type contractType;
        var interfaces = serviceType.GetInterfaces();
        if (interfaces.Length == 1)
            contractType = interfaces[0];
        else if (interfaces.Length == 2)
        {
            if (interfaces[0].FullName == "CODE.Framework.Services.Contracts.IServiceEvents")
                contractType = interfaces[1];
            else if (interfaces[1].FullName == "CODE.Framework.Services.Contracts.IServiceEvents")
                contractType = interfaces[0];
            else
                throw new IndexOutOfBoundsException("Service contract cannot be automatically determined for the specified service type.");
        }
        else
            throw new IndexOutOfBoundsException("Service contract cannot be automatically determined for the specified service type.");
        return AddServiceHost(serviceType, contractType);
    }

    /// <summary>
    /// Adds a local service
    /// </summary>
    /// <param name="serviceType">Type of the service.</param>
    /// <param name="contractType">Type of the operation contract (interface).</param>
    /// <returns>True if successful</returns>
    /// <example>
    /// ServiceGardenLocal.AddServiceHost(typeof(MyNamespace.CustomerService), typeof(MyContracts.ICustomerServicce));
    /// </example>
    public static bool AddServiceHost(Type serviceType, Type contractType)
    {
        object serviceInstance = null;
        if (!_useDependencyInjection)
            serviceInstance = Activator.CreateInstance(serviceType);
        else
        {
            serviceInstance = CreateInstanceWithDependencies(serviceType);
            if (serviceInstance == null)
                serviceInstance = Activator.CreateInstance(serviceType);
        }

        if (serviceInstance == null) return false;

        var proxy = TransparentProxyGenerator.GetProxy(contractType, new InProcessProxyHandler(serviceInstance));

        Hosts.Add(contractType, proxy);

        var settingName = $"ServiceProtocol:{contractType.Name}";
        if (!ConfigurationSettings.Settings.IsSettingSupported(settingName))
            if (ConfigurationSettings.Sources.Any(s => s.FriendlyName == "Memory"))
                ConfigurationSettings.Sources["Memory"].Settings[settingName] = "InProcess";

        if (serviceInstance is IServiceEvents serviceEvents) 
            serviceEvents.OnInProcessHostLaunched();

        return true;
    }

    private static object CreateInstanceWithDependencies(Type serviceType)
    {
        var constructors = serviceType.GetConstructors();
        if (constructors.Length == 0) return null; // No constructors at all? Nothing we can do here.

        // As a general rule, we look for the constructor with the most parameters and use it.
        var constructorsByParameterCount = constructors.OrderByDescending(c => c.GetParameters().Length).ToList();
        var longestConstructor = constructorsByParameterCount[0];

        // If we only have a constructor without parameters, we just let it rip!
        var constructorParameters = longestConstructor.GetParameters();
        if (constructorParameters.Length == 0)
            return Activator.CreateInstance(serviceType);

        // We need to come up with the parameters, so we go ahead and create all the required parameter instances
        var parameterValues = new List<object>();
        foreach (var parameter in constructorParameters)
        {
            if (!parameter.ParameterType.IsInterface) return null; // TODO: We should probably throw an exception!
            if (_transientDependencies.ContainsKey(parameter.ParameterType))
            {
                var concreteType = _transientDependencies[parameter.ParameterType];
                var concreteInstance = CreateInstanceWithDependencies(concreteType);
                parameterValues.Add(concreteInstance);
            }
            else if (_singletonDependencies.ContainsKey(parameter.ParameterType))
            {
                var dependency = _singletonDependencies[parameter.ParameterType];
                if (dependency.Instance != null)
                    parameterValues.Add(dependency.Instance);
                else
                {
                    dependency.Instance = CreateInstanceWithDependencies(dependency.Type);
                    parameterValues.Add(dependency.Instance);
                }
            }
            else
                return null; // TODO: We should probably throw an exception!
        }

        var objectInstance = Activator.CreateInstance(serviceType, parameterValues.ToArray());
        return objectInstance;
    }

    /// <summary>
    /// Gets the service.
    /// </summary>
    /// <typeparam name="TContractType">The type of the operations contract (interface).</typeparam>
    /// <returns></returns>
    public static TContractType GetService<TContractType>() => Hosts.ContainsKey(typeof(TContractType)) ? (TContractType) Hosts[typeof(TContractType)] : default;

    /// <summary>
    /// Gets the service.
    /// </summary>
    /// <param name="contractType">Contract Type</param>
    /// <returns></returns>
    public static object GetService(Type contractType) => Hosts.ContainsKey(contractType) ? Hosts[contractType] : null;
}

public class InProcessProxyHandler : IProxyHandler
{
    private readonly object _instance;
    private readonly Type _type;

    public InProcessProxyHandler(object instance)
    {
        _instance = instance;
        _type = instance.GetType();
    }

    public object OnMethod(MethodInfo method, object[] args)
    {
        var realMethod = _type.GetMethod(method.Name);
        if (realMethod != null)
        {
            try
            {
                return realMethod.Invoke(_instance, args);
            }
            catch (Exception ex)
            {
                // If there was an exception, we may handle it automatically, IF either the operation or the entire type is flagged to auto-handle exceptions
                var handlerAttribute = realMethod.GetCustomAttributeEx<StandardExceptionHandlingAttribute>();
                if (handlerAttribute is null)
                    handlerAttribute = realMethod.DeclaringType.GetCustomAttributeEx<StandardExceptionHandlingAttribute>();

                if (handlerAttribute is not null)
                {
                    var ex2 = ex;
                    if (ex2 is InvalidOperationException && ex2.InnerException != null)
                        ex2 = ex2.InnerException;
                    return ServiceHelper.GetPopulatedFailureResponse(realMethod.ReturnType, ex, realMethod.DeclaringType.Name, realMethod.Name);
                }
                else
                    return null; // TODO: Throw an exception
            }
        }
        else
            return null; // TODO: Throw an exception
    }
}