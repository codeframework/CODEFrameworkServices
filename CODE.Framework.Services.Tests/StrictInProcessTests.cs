using CODE.Framework.Fundamentals.Configuration;
using CODE.Framework.Services.Client;
using CODE.Framework.Services.Contracts;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class StrictInProcessTests : IDisposable
{
    private readonly DictionarySettings _source = new(new Dictionary<string, string> { ["ServiceProtocol:IStrictLocalService"] = "InProcess" });

    public StrictInProcessTests()
    {
        ConfigurationSettings.Sources.Insert(0, _source);
        ServiceClient.ClearSettingsCache();
        if (ServiceGardenLocal.GetService<IStrictLocalService>() == null)
            ServiceGardenLocal.AddServiceHost(typeof(StrictLocalService));
        StrictLocalService.Attempts = 0;
    }

    [Fact]
    public void Local_exception_is_unwrapped_and_not_retried()
    {
        var options = new ServiceCallOptions { MaxAttempts = 3, CanRetryOperation = _ => true };
        var error = Assert.Throws<InvalidOperationException>(() => ServiceClient.Call<IStrictLocalService>(options, service => service.Throw(new CalculationRequest())));
        Assert.Equal("Local failure", error.Message);
        Assert.Equal(1, StrictLocalService.Attempts);
    }

    [Fact]
    public void Standard_failure_conversion_is_preserved()
    {
        CalculationResponse response = null;
        ServiceClient.Call<IStrictLocalService>(new ServiceCallOptions(), service => response = service.Handled(new CalculationRequest()));
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.Equal(1, StrictLocalService.Attempts);
    }

    [Fact]
    public void Missing_registration_reports_configuration_failure()
    {
        _source.Settings["ServiceProtocol:IMissingLocalService"] = "InProcess";
        Assert.Throws<InvalidOperationException>(() => ServiceClient.Call<IMissingLocalService>(new ServiceCallOptions(), _ => throw new Exception("Must not run")));
    }

    public void Dispose()
    {
        ConfigurationSettings.Sources.Remove(_source);
        ServiceClient.ClearSettingsCache();
    }

    public interface IStrictLocalService
    {
        CalculationResponse Throw(CalculationRequest request);
        [StandardExceptionHandling]
        CalculationResponse Handled(CalculationRequest request);
    }

    public interface IMissingLocalService
    {
        CalculationResponse Calculate(CalculationRequest request);
    }

    public class StrictLocalService : IStrictLocalService
    {
        public static int Attempts { get; set; }

        public CalculationResponse Throw(CalculationRequest request)
        {
            Attempts++;
            throw new InvalidOperationException("Local failure");
        }

        public CalculationResponse Handled(CalculationRequest request) => Throw(request);
    }
}
