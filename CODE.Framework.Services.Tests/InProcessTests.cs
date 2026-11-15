using CODE.Framework.Services.Client;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class InProcessTests
{
    [Fact]
    public void Registered_service_is_selected_by_configuration_and_executed()
    {
        ServiceClient.ClearSettingsCache();
        try
        {
            Assert.True(ServiceGardenLocal.AddServiceHost(typeof(LocalService)));
            var request = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 17, Right = 25 };
            CalculationResponse response = null;
            ServiceClient.Call<ILocalService>(service => response = service.Calculate(request));
            Assert.NotNull(response);
            Assert.Equal(request.RequestId, response.RequestId);
            Assert.Equal(42, response.Total);
            Assert.Equal(LocalService.InstanceId, response.ServerId);
            Assert.Same(request, LocalService.LastRequest);
        }
        finally
        {
            ServiceClient.ClearSettingsCache();
        }
    }

    // A unique contract avoids polluting HTTP tests. The garden has no unregister API;
    // this registration intentionally lives for the lifetime of the test process.
    public interface ILocalService
    {
        CalculationResponse Calculate(CalculationRequest request);
    }

    public class LocalService : ILocalService
    {
        public static Guid InstanceId { get; } = Guid.NewGuid();
        public static CalculationRequest LastRequest { get; private set; }

        public CalculationResponse Calculate(CalculationRequest request)
        {
            LastRequest = request;
            return new CalculationResponse { RequestId = request.RequestId, Total = request.Left + request.Right, ServerId = InstanceId };
        }
    }
}
