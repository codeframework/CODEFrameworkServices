﻿using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

namespace CODE.Framework.Fundamentals.Utilities
{
#if NETCORE
    /// <summary>
    /// Class TransparentProxyGenerator.
    /// </summary>
    public static class TransparentProxyGenerator
    {
        private static AssemblyBuilder _assemblyBuilder;
        private static ModuleBuilder _moduleBuilder;
        private static readonly object LockDummy = new object();
        private static readonly Dictionary<Type, object> ProxyCache = new Dictionary<Type, object>();

        /// <summary>
        /// Returns a proxy for the provided interface
        /// </summary>
        /// <typeparam name="TProxy">Type to be proxied</typeparam>
        /// <param name="handler">The actual handler object that handles all the calls to the proxy.</param>
        /// <param name="useProxyCache">If true, cached proxies can be reused</param>
        /// <returns>
        /// Proxy object
        /// </returns>
        /// <exception cref="System.ArgumentException">T needs to be an interface</exception>
        public static TProxy GetProxy<TProxy>(IProxyHandler handler, bool useProxyCache = true)
        {
            var proxyType = typeof(TProxy);
            return (TProxy)GetProxy(proxyType, handler, useProxyCache);
        }

        /// <summary>
        /// Returns a proxy for the provided interface
        /// </summary>
        /// <param name="proxyType">Type definition for the proxy</param>
        /// <param name="handler">The actual handler object that handles all the calls to the proxy.</param>
        /// <param name="useProxyCache">If true, cached proxies can be reused</param>
        /// <returns>
        /// Proxy object
        /// </returns>
        /// <exception cref="System.ArgumentException">T needs to be an interface</exception>
        public static object GetProxy(Type proxyType, IProxyHandler handler, bool useProxyCache = true)
        {
            if (!proxyType.IsInterface) throw new ArgumentException("T needs to be an interface");

            if (useProxyCache)
                lock (ProxyCache)
                    if (ProxyCache.ContainsKey(proxyType))
                        return ProxyCache[proxyType];

            lock (LockDummy)
            {
                if (_assemblyBuilder == null)
                {
                    _assemblyBuilder = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("CODE.Framework.Proxies"), AssemblyBuilderAccess.Run);
                    //var assemblyName = _assemblyBuilder.GetName().Name;
                    _moduleBuilder = _assemblyBuilder.DefineDynamicModule("MainModule");
                }
                var typeBuilder = _moduleBuilder.DefineType(proxyType.Name + "_CODE_Framework_Proxy_" + Guid.NewGuid().ToString().Replace("-", "_"), TypeAttributes.Class | TypeAttributes.Public, typeof(object), new[] { proxyType });
                var proxyFieldBuilder = typeBuilder.DefineField("handler", typeof(IProxyHandler), FieldAttributes.Public);
                var callRetMethod = typeof(IProxyHandler).GetMethod("OnMethod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(MethodInfo), typeof(object[]) }, null);

                var constructorType = typeof(object);
                var constructor = constructorType.GetConstructor(new Type[] { });
                var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new[] { typeof(IProxyHandler) });

                var cIl = constructorBuilder.GetILGenerator();
                cIl.Emit(OpCodes.Ldarg_0); // Load this to stack
                if (constructor != null) cIl.Emit(OpCodes.Call, constructor); // Call base (object) constructor
                cIl.Emit(OpCodes.Ldarg_0); // Load this to stack
                cIl.Emit(OpCodes.Ldarg_1); // Load the IProxyHandler to stack
                cIl.Emit(OpCodes.Stfld, proxyFieldBuilder); // Set proxy to the actual proxy
                cIl.Emit(OpCodes.Ret);

                // Creating all the methods on the interface
                foreach (var method in proxyType.GetMethods())
                {
                    var mb = typeBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual | MethodAttributes.Final, method.ReturnType, method.GetParameters().Select(pi => pi.ParameterType).ToArray());
                    var privateParameterCount = method.GetParameters().Length;
                    var il = mb.GetILGenerator();

                    var argArray = il.DeclareLocal(typeof(object[]));
                    il.Emit(OpCodes.Ldc_I4, privateParameterCount);
                    il.Emit(OpCodes.Newarr, typeof(object));
                    il.Emit(OpCodes.Stloc, argArray);

                    var methodInfo = il.DeclareLocal(typeof(MethodInfo));
                    il.Emit(OpCodes.Ldtoken, method);
                    il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", new[] { typeof(RuntimeMethodHandle) }));
                    il.Emit(OpCodes.Stloc, methodInfo);

                    for (var counter = 0; counter < privateParameterCount; counter++)
                    {
                        var info = method.GetParameters()[counter];

                        il.Emit(OpCodes.Ldloc, argArray);
                        il.Emit(OpCodes.Ldc_I4, counter);
                        il.Emit(OpCodes.Ldarg_S, counter + 1);
                        if (info.ParameterType.IsPrimitive || info.ParameterType.IsValueType)
                            il.Emit(OpCodes.Box, info.ParameterType);
                        il.Emit(OpCodes.Stelem_Ref);
                    }

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, proxyFieldBuilder);
                    il.Emit(OpCodes.Ldloc, methodInfo);
                    il.Emit(OpCodes.Ldloc, argArray);
                    il.Emit(OpCodes.Callvirt, callRetMethod);
                    if (method.ReturnType.IsValueType && method.ReturnType != typeof(void))
                        il.Emit(OpCodes.Unbox_Any, method.ReturnType);

                    if (method.ReturnType == typeof(void))
                        il.Emit(OpCodes.Pop);

                    il.Emit(OpCodes.Ret);
                }

                var proxyTypeInfo = typeBuilder.CreateTypeInfo();
                var proxyType2 = proxyTypeInfo.AsType();
                var proxy = Activator.CreateInstance(proxyType2, new object[] { handler }, null);
                if (useProxyCache)
                {
                    lock (ProxyCache)
                    {
                        if (!ProxyCache.ContainsKey(proxyType))
                            ProxyCache.Add(proxyType, proxy);
                        else
                            ProxyCache[proxyType] = proxy;
                    }
                }
                return proxy;
            }
        }

        /// <summary>
        /// Clears either an individual proxy type (if a proxy type is provided as the parameter),
        /// or all proxies.
        /// </summary>
        /// <param name="proxyType">Proxy Type to clear (if not provided, all proxies are cleared).</param>
        public static void ClearProxyCache(Type proxyType = null)
        {
            lock (ProxyCache)
            {
                if (proxyType == null)
                    ProxyCache.Clear();
                else if (ProxyCache.ContainsKey(proxyType))
                    ProxyCache.Remove(proxyType);
            }
        }
    }

#endif

#if NETFULL
    /// <summary>
    /// Class TransparentProxyGenerator.
    /// </summary>
    public static class TransparentProxyGenerator
    {
        private static AssemblyBuilder _assemblyBuilder;
        private static ModuleBuilder _moduleBuilder;
        private static readonly object LockDummy = new object();
        private static readonly Dictionary<Type, object> ProxyCache = new Dictionary<Type, object>();

        /// <summary>
        /// Returns a proxy for the provided interface
        /// </summary>
        /// <typeparam name="TProxy">Type to be proxied</typeparam>
        /// <param name="handler">The actual handler object that handles all the calls to the proxy.</param>
        /// <param name="useProxyCache">If true, cached proxies can be reused</param>
        /// <returns>
        /// Proxy object
        /// </returns>
        /// <exception cref="System.ArgumentException">T needs to be an interface</exception>
        public static TProxy GetProxy<TProxy>(IProxyHandler handler, bool useProxyCache = true)
        {
            var t = typeof(TProxy);

            if (!t.IsInterface) throw new ArgumentException("T needs to be an interface");

            if (useProxyCache)
                lock (ProxyCache)
                    if (ProxyCache.ContainsKey(t))
                        return ProxyCache[t];

            lock (LockDummy)
            {
                if (_assemblyBuilder == null)
                {
                    _assemblyBuilder = AppDomain.CurrentDomain.DefineDynamicAssembly(new AssemblyName("CODE.Framework.Proxies"), AssemblyBuilderAccess.Run);
                    var assemblyName = _assemblyBuilder.GetName().Name;
                    _moduleBuilder = _assemblyBuilder.DefineDynamicModule(assemblyName);
                }
                var typeBuilder = _moduleBuilder.DefineType(t.Name + "_CODE_Framework_Proxy_" + Guid.NewGuid().ToString().Replace("-", "_"), TypeAttributes.Class | TypeAttributes.Public, typeof(Object), new[] { t });
                var proxyFieldBuilder = typeBuilder.DefineField("handler", typeof(IProxyHandler), FieldAttributes.Private);
                var callRetMethod = typeof(IProxyHandler).GetMethod("OnMethod", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(MethodInfo), typeof(object[]) }, null);

                var constructorType = Type.GetType("System.Object");
                if (constructorType != null)
                {
                    var constructor = constructorType.GetConstructor(new Type[] { });
                    var constructorBuilder = typeBuilder.DefineConstructor(MethodAttributes.Public, CallingConventions.HasThis, new[] { typeof(IProxyHandler) });

                    var cIl = constructorBuilder.GetILGenerator();
                    cIl.Emit(OpCodes.Ldarg_0); // Load this to stack
                    if (constructor != null) cIl.Emit(OpCodes.Call, constructor); // Call base (object) constructor
                    cIl.Emit(OpCodes.Ldarg_0); // Load this to stack
                    cIl.Emit(OpCodes.Ldarg_1); // Load the IProxyHandler to stack
                    cIl.Emit(OpCodes.Stfld, proxyFieldBuilder); // Set proxy to the actual proxy
                    cIl.Emit(OpCodes.Ret);
                }

                // Creating all the methods on the interface
                foreach (var method in t.GetMethods())
                {
                    var mb = typeBuilder.DefineMethod(method.Name, MethodAttributes.Public | MethodAttributes.HideBySig | MethodAttributes.NewSlot | MethodAttributes.Virtual, method.ReturnType, method.GetParameters().Select(pi => pi.ParameterType).ToArray());
                    var privateParameterCount = method.GetParameters().Length;
                    var il = mb.GetILGenerator();

                    var argArray = il.DeclareLocal(typeof(object[]));
                    il.Emit(OpCodes.Ldc_I4, privateParameterCount);
                    il.Emit(OpCodes.Newarr, typeof(object));
                    il.Emit(OpCodes.Stloc, argArray);

                    var methodInfo = il.DeclareLocal(typeof(MethodInfo));
                    il.Emit(OpCodes.Ldtoken, method);
                    il.Emit(OpCodes.Call, typeof(MethodBase).GetMethod("GetMethodFromHandle", new[] { typeof(RuntimeMethodHandle) }));
                    il.Emit(OpCodes.Stloc, methodInfo);

                    for (var counter = 0; counter < privateParameterCount; counter++)
                    {
                        var info = method.GetParameters()[counter];

                        il.Emit(OpCodes.Ldloc, argArray);
                        il.Emit(OpCodes.Ldc_I4, counter);
                        il.Emit(OpCodes.Ldarg_S, counter + 1);
                        if (info.ParameterType.IsPrimitive || info.ParameterType.IsValueType)
                            il.Emit(OpCodes.Box, info.ParameterType);
                        il.Emit(OpCodes.Stelem_Ref);
                    }

                    il.Emit(OpCodes.Ldarg_0);
                    il.Emit(OpCodes.Ldfld, proxyFieldBuilder);
                    il.Emit(OpCodes.Ldloc, methodInfo);
                    il.Emit(OpCodes.Ldloc, argArray);
                    il.Emit(OpCodes.Callvirt, callRetMethod);
                    if (method.ReturnType.IsValueType && method.ReturnType != typeof(void))
                        il.Emit(OpCodes.Unbox_Any, method.ReturnType);

                    if (method.ReturnType == typeof(void))
                        il.Emit(OpCodes.Pop);

                    il.Emit(OpCodes.Ret);
                }
                var proxyType = typeBuilder.CreateType();
                var proxy = Activator.CreateInstance(proxyType, new object[] { handler }, null);
                if (useProxyCache)
                {
                    lock (ProxyCache)
                    {
                        if (!ProxyCache.ContainsKey(proxyType))
                            ProxyCache.Add(proxyType, proxy);
                        else
                            ProxyCache[proxyType] = proxy;
                    }
                }
                return (TProxy)proxy;
            }
        }
    }
#endif

    /// <summary>
    /// Interface for handler objects that can be used to provide transparent proxy functionality
    /// </summary>
    public interface IProxyHandler
    {
        /// <summary>
        /// This method is called when any method on a proxied object is invoked.
        /// </summary>
        /// <param name="method">Information about the method being called.</param>
        /// <param name="args">The arguments passed to the method.</param>
        /// <returns>Result value from the proxy call</returns>
        object OnMethod(MethodInfo method, object[] args);
    }
}