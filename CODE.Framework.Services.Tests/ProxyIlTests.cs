using System.Reflection;
using System.Reflection.Emit;
using CODE.Framework.Fundamentals.Utilities;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class ProxyIlTests
{
    [Theory]
    [InlineData(nameof(IReturnContract.Message))]
    [InlineData(nameof(IReturnContract.Deferred))]
    [InlineData(nameof(IReturnContract.Sequence))]
    public void Incompatible_handler_result_is_rejected_at_the_proxy_boundary(string methodName)
    {
        var proxy = TransparentProxyGenerator.GetProxy<IReturnContract>(new ResultHandler(new object()));
        // Invoke reflectively so an incorrectly typed reference cannot escape into typed test code.
        var error = Assert.Throws<TargetInvocationException>(() => proxy.GetType().GetMethod(methodName).Invoke(proxy, null));
        Assert.IsType<InvalidCastException>(error.InnerException);
    }

    [Fact]
    public void Null_reference_returns_remain_valid()
    {
        var proxy = TransparentProxyGenerator.GetProxy<IReturnContract>(new ResultHandler(null));
        Assert.Null(proxy.Message());
        Assert.Null(proxy.Deferred());
        Assert.Null(proxy.Sequence());
    }

    [Fact]
    public void Derived_and_interface_compatible_returns_are_preserved()
    {
        var response = new DerivedResponse { Total = 42 };
        var messageProxy = TransparentProxyGenerator.GetProxy<IReturnContract>(new ResultHandler(response));
        Assert.Same(response, messageProxy.Message());
        var sequence = new List<int> { 17, 25 };
        var sequenceProxy = TransparentProxyGenerator.GetProxy<IReturnContract>(new ResultHandler(sequence));
        Assert.Same(sequence, sequenceProxy.Sequence());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(255)]
    [InlineData(256)]
    [InlineData(257)]
    public void All_arguments_survive_dispatch_across_the_short_operand_boundary(int count)
    {
        // The proxy generator is a general utility, even though REST operations use one message.
        // Generate a large contract here instead of maintaining hundreds of handwritten parameters.
        var assembly = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("ArgumentBoundary_" + Guid.NewGuid().ToString("N")), AssemblyBuilderAccess.Run);
        var module = assembly.DefineDynamicModule("Main");
        var contractBuilder = module.DefineType("IArguments", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
        contractBuilder.DefineMethod("Capture", MethodAttributes.Public | MethodAttributes.Abstract | MethodAttributes.Virtual | MethodAttributes.NewSlot, typeof(object[]), Enumerable.Repeat(typeof(int), count).ToArray());
        var contract = contractBuilder.CreateTypeInfo().AsType();
        var handler = new ArgumentHandler();
        var proxy = TransparentProxyGenerator.GetProxy(contract, handler);
        var arguments = Enumerable.Range(0, count).Select(index => (object)(1000 + index)).ToArray();
        var result = Assert.IsType<object[]>(contract.GetMethod("Capture").Invoke(proxy, arguments));
        Assert.Equal(arguments, result);
        Assert.Equal(contract.GetMethod("Capture"), handler.Method);
    }

    [Fact]
    public void Value_return_rejects_a_different_boxed_type()
    {
        var proxy = TransparentProxyGenerator.GetProxy<IReturnContract>(new ResultHandler(42L));
        Assert.Throws<InvalidCastException>(() => proxy.Number());
    }

    public interface IReturnContract
    {
        CalculationResponse Message();
        Task<CalculationResponse> Deferred();
        IEnumerable<int> Sequence();
        int Number();
    }

    public class DerivedResponse : CalculationResponse { }

    private class ResultHandler(object result) : IProxyHandler
    {
        public object OnMethod(MethodInfo method, object[] args) => result;
    }

    private class ArgumentHandler : IProxyHandler
    {
        public MethodInfo Method { get; private set; }

        public object OnMethod(MethodInfo method, object[] args)
        {
            Method = method;
            return args;
        }
    }
}
