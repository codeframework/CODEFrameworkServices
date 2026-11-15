using CODE.Framework.Services.Client;
using CODE.Framework.Services.Contracts;
using Xunit;

namespace CODE.Framework.Services.Tests;

public class HttpServiceTests(HttpServiceFixture host) : IClassFixture<HttpServiceFixture>
{
    [Theory]
    [InlineData("Calculate")]
    [InlineData("Lookup")]
    [InlineData("Replace")]
    [InlineData("Remove")]
    public async Task ServiceClient_round_trip_executes_requested_operation(string operation)
    {
        var request = NewRequest();
        CalculationResponse response = null;
        var callbackCompleted = false;
        await Task.Run(() => ServiceClient.Call<ITestService>(service =>
        {
            response = operation switch
            {
                "Calculate" => service.Calculate(request),
                "Lookup" => service.Lookup(request),
                "Replace" => service.Replace(request),
                "Remove" => service.Remove(request),
                _ => throw new ArgumentOutOfRangeException(nameof(operation))
            };
            callbackCompleted = true;
        }, host.ServiceUrl)).WaitAsync(TimeSpan.FromSeconds(15));

        // Assertions must be OUTSIDE Call: the current client swallows callback exceptions.
        Assert.True(callbackCompleted);
        AssertResponse(request, response);
        AssertExecution(request, operation);
    }

    [Fact]
    public async Task File_response_preserves_binary_bytes_and_headers()
    {
        var request = NewRequest();
        FileResponse response = null;
        await Task.Run(() => ServiceClient.Call<ITestService>(service => response = service.Download(request), host.ServiceUrl)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(response);
        Assert.Equal(request.RequestId.ToByteArray(), response.FileBytes);
        Assert.Equal("receipt.bin", response.FileName);
        Assert.Equal("application/octet-stream", response.ContentType);
        AssertExecution(request, "Download");
    }

    [Fact]
    public async Task Standard_exception_handling_returns_failure_message()
    {
        var request = NewRequest();
        CalculationResponse response = null;
        await Task.Run(() => ServiceClient.Call<ITestService>(service => response = service.Fail(request), host.ServiceUrl)).WaitAsync(TimeSpan.FromSeconds(15));
        Assert.NotNull(response);
        Assert.False(response.Success);
        Assert.False(string.IsNullOrWhiteSpace(response.FailureInformation));
        AssertExecution(request, "Fail");
    }

    [Fact]
    public async Task Concurrent_requests_keep_messages_separate()
    {
        var requests = Enumerable.Range(0, 12).Select(index => NewRequest(index)).ToArray();
        var responses = await Task.WhenAll(requests.Select(request => Task.Run(() =>
            ServiceClient.GetChannelDedicated<ITestService>(host.ServiceUrl).Calculate(request)))).WaitAsync(TimeSpan.FromSeconds(15));
        for (var index = 0; index < requests.Length; index++)
        {
            AssertResponse(requests[index], responses[index]);
            AssertExecution(requests[index], "Calculate");
        }
    }

    private static CalculationRequest NewRequest(int left = 17) => new() { RequestId = Guid.NewGuid(), Left = left, Right = 25, Text = "Maui café & tea + / ?" };

    private void AssertResponse(CalculationRequest request, CalculationResponse response)
    {
        Assert.NotNull(response);
        Assert.True(response.Success);
        Assert.Equal(request.RequestId, response.RequestId);
        Assert.Equal(host.Log.ServerId, response.ServerId);
        Assert.Equal(request.Left + request.Right, response.Total);
        Assert.Equal(request.Text, response.Text);
    }

    private void AssertExecution(CalculationRequest request, string operation)
    {
        var execution = Assert.Single(host.Log.Entries, entry => entry.RequestId == request.RequestId);
        Assert.Equal(new Execution(request.RequestId, request.Left, request.Right, request.Text, operation), execution);
    }
}
