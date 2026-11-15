using CODE.Framework.Services.Client;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class EndpointIsolationTests
{
    [Fact]
    public async Task Same_contract_calls_two_servers_without_reusing_the_first_endpoint()
    {
        var first = new HttpServiceFixture();
        var second = new HttpServiceFixture();
        try
        {
            await first.InitializeAsync();
            await second.InitializeAsync();
            var firstRequest = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 17, Right = 25, Text = "first" };
            var secondRequest = new CalculationRequest { RequestId = Guid.NewGuid(), Left = 3, Right = 4, Text = "second" };
            CalculationResponse firstResponse = null;
            CalculationResponse secondResponse = null;
            // Both hosts are started before the calls: no cache clearing between endpoints.
            await Task.Run(() =>
            {
                ServiceClient.Call<ITestService>(service => firstResponse = service.Calculate(firstRequest), first.ServiceUrl);
                ServiceClient.Call<ITestService>(service => secondResponse = service.Calculate(secondRequest), second.ServiceUrl);
            }).WaitAsync(TimeSpan.FromSeconds(15));
            Assert.NotNull(firstResponse);
            Assert.NotNull(secondResponse);
            Assert.Equal(first.Log.ServerId, firstResponse.ServerId);
            Assert.Equal(second.Log.ServerId, secondResponse.ServerId);
            Assert.Equal(42, firstResponse.Total);
            Assert.Equal(7, secondResponse.Total);
            Assert.Equal(firstRequest.RequestId, firstResponse.RequestId);
            Assert.Equal(secondRequest.RequestId, secondResponse.RequestId);
            Assert.Equal(firstRequest.RequestId, Assert.Single(first.Log.Entries).RequestId);
            Assert.Equal(secondRequest.RequestId, Assert.Single(second.Log.Entries).RequestId);
        }
        finally
        {
            // Restore the static host configuration in reverse initialization order.
            await second.DisposeAsync();
            await first.DisposeAsync();
        }
    }
}
