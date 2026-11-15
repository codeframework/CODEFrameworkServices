using CODE.Framework.Fundamentals.Configuration;
using CODE.Framework.Services.Client;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class ConfigurationCacheTests : IClassFixture<HttpServiceFixture>, IDisposable
{
    private const string FirstProtocol = "ServiceProtocol:IConfigService";
    private const string SecondProtocol = "ServiceProtocol:IOtherConfigService";
    private const string FirstUrl = "RestServiceUrl:IConfigService";
    private readonly HttpServiceFixture _host;
    private readonly DictionarySettings _source;
    private readonly bool _previousCacheSettings;

    public ConfigurationCacheTests(HttpServiceFixture host)
    {
        _host = host;
        _previousCacheSettings = ServiceClient.CacheSettings;
        _source = new DictionarySettings(new Dictionary<string, string>
        {
            [FirstProtocol] = "Rest",
            [SecondProtocol] = "Rest",
            [FirstUrl] = host.ServiceUrl,
            ["RestServiceUrl:IOtherConfigService"] = host.ServiceUrl,
            ["ServiceProtocol"] = "Rest"
        });
        ConfigurationSettings.Sources.Insert(0, _source);
        ServiceClient.CacheSettings = true;
        ServiceClient.ClearSettingsCache();
        // Dedicated contracts avoid other tests; registrations remain until process exit.
        if (ServiceGardenLocal.GetService<IConfigService>() == null)
            ServiceGardenLocal.AddServiceHost(typeof(LocalConfigService));
        if (ServiceGardenLocal.GetService<IOtherConfigService>() == null)
            ServiceGardenLocal.AddServiceHost(typeof(OtherLocalConfigService));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(FirstProtocol)]
    public async Task Clearing_cache_switches_transport_in_both_directions(string setting)
    {
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        _source.Settings[FirstProtocol] = "InProcess";
        Assert.Equal(_host.Log.ServerId, await CallFirst()); // Cached until explicitly cleared.
        ServiceClient.ClearSettingsCache(setting);
        Assert.Equal(LocalConfigService.Id, await CallFirst());
        _source.Settings[FirstProtocol] = "Rest";
        ServiceClient.ClearSettingsCache(setting);
        Assert.Equal(_host.Log.ServerId, await CallFirst());
    }

    [Fact]
    public async Task Clearing_global_protocol_refreshes_all_fallback_contracts()
    {
        _source.Settings[FirstProtocol] = "";
        _source.Settings[SecondProtocol] = "";
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        Assert.Equal(_host.Log.ServerId, await CallSecond());
        _source.Settings["ServiceProtocol"] = "InProcess";
        ServiceClient.ClearSettingsCache("ServiceProtocol");
        Assert.Equal(LocalConfigService.Id, await CallFirst());
        Assert.Equal(OtherLocalConfigService.Id, await CallSecond());
    }

    [Fact]
    public async Task Clearing_global_protocol_preserves_explicit_contract_override()
    {
        _source.Settings[FirstProtocol] = "";
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        Assert.Equal(_host.Log.ServerId, await CallSecond());
        _source.Settings["ServiceProtocol"] = "InProcess";
        ServiceClient.ClearSettingsCache("ServiceProtocol");
        Assert.Equal(LocalConfigService.Id, await CallFirst());
        Assert.Equal(_host.Log.ServerId, await CallSecond());
    }

    [Fact]
    public async Task Targeted_clear_leaves_other_contract_cached()
    {
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        Assert.Equal(_host.Log.ServerId, await CallSecond());
        _source.Settings[FirstProtocol] = "InProcess";
        _source.Settings[SecondProtocol] = "InProcess";
        ServiceClient.ClearSettingsCache("UnrelatedSetting");
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        ServiceClient.ClearSettingsCache(FirstProtocol);
        Assert.Equal(LocalConfigService.Id, await CallFirst());
        Assert.Equal(_host.Log.ServerId, await CallSecond());
        ServiceClient.ClearSettingsCache(SecondProtocol);
        Assert.Equal(OtherLocalConfigService.Id, await CallSecond());
    }

    [Fact]
    public async Task Disabled_caching_reads_changed_protocol_without_clearing()
    {
        Assert.Equal(_host.Log.ServerId, await CallFirst());
        ServiceClient.CacheSettings = false;
        _source.Settings[FirstProtocol] = "InProcess";
        Assert.Equal(LocalConfigService.Id, await CallFirst());
        _source.Settings[FirstProtocol] = "Rest";
        Assert.Equal(_host.Log.ServerId, await CallFirst());
    }

    [Fact]
    public async Task Clearing_endpoint_setting_refreshes_new_channels_but_preserves_existing_channel()
    {
        var second = new HttpServiceFixture();
        try
        {
            await second.InitializeAsync();
            var original = ServiceClient.GetChannelDedicated<IConfigService>();
            Assert.Equal(_host.Log.ServerId, await CallFirst());
            _source.Settings[FirstUrl] = second.ServiceUrl;
            Assert.Equal(_host.Log.ServerId, await CallFirst());
            ServiceClient.ClearSettingsCache(FirstUrl);
            var refreshed = ServiceClient.GetChannelDedicated<IConfigService>();
            Assert.Same(original.GetType(), refreshed.GetType());
            var request = NewRequest();
            var response = await Task.Run(() => refreshed.Calculate(request)).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.Equal(second.Log.ServerId, response.ServerId);
            Assert.Equal(request.RequestId, Assert.Single(second.Log.Entries).RequestId);
            Assert.Equal(_host.Log.ServerId, await Invoke(original.Calculate));
        }
        finally
        {
            await second.DisposeAsync();
        }
    }

    private Task<Guid> CallFirst() => Invoke(ServiceClient.GetChannelDedicated<IConfigService>().Calculate);

    private Task<Guid> CallSecond() => Invoke(ServiceClient.GetChannelDedicated<IOtherConfigService>().Calculate);

    private async Task<Guid> Invoke(Func<CalculationRequest, CalculationResponse> operation)
    {
        var request = NewRequest();
        var response = await Task.Run(() => operation(request)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(42, response.Total);
        if (response.ServerId == _host.Log.ServerId)
            Assert.Single(_host.Log.Entries, entry => entry.RequestId == request.RequestId);
        return response.ServerId;
    }

    private static CalculationRequest NewRequest() => new() { RequestId = Guid.NewGuid(), Left = 17, Right = 25, Text = "configuration" };

    public void Dispose()
    {
        ConfigurationSettings.Sources.Remove(_source);
        ServiceClient.CacheSettings = _previousCacheSettings;
        ServiceClient.ClearSettingsCache();
    }

    public interface IConfigService
    {
        CalculationResponse Calculate(CalculationRequest request);
    }

    public interface IOtherConfigService
    {
        CalculationResponse Calculate(CalculationRequest request);
    }

    public class LocalConfigService : IConfigService
    {
        public static Guid Id { get; } = Guid.NewGuid();

        public CalculationResponse Calculate(CalculationRequest request) => new() { RequestId = request.RequestId, ServerId = Id, Total = request.Left + request.Right };
    }

    public class OtherLocalConfigService : IOtherConfigService
    {
        public static Guid Id { get; } = Guid.NewGuid();

        public CalculationResponse Calculate(CalculationRequest request) => new() { RequestId = request.RequestId, ServerId = Id, Total = request.Left + request.Right };
    }
}
