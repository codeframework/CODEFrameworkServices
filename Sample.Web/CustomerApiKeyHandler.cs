using Sample.Contracts;

namespace Sample.Web;

/// <summary>Simple sample validator. Real applications can look up keys in their own key store.</summary>
public class CustomerApiKeyHandler : IApiKeyHandler
{
    private readonly string _expectedKey;

    public CustomerApiKeyHandler(string expectedKey)
    {
        if (string.IsNullOrEmpty(expectedKey)) throw new ArgumentException("Configure CustomerApiKey before starting the sample.", nameof(expectedKey));
        _expectedKey = expectedKey;
    }

    public Task<bool> CheckApiKeyAsync(Type contractType, string key, ServiceHandlerRequestContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Missing keys arrive as null. Every customer operation requires the configured sample key.
        return Task.FromResult(contractType == typeof(ICustomerService) && string.Equals(key, _expectedKey, StringComparison.Ordinal));
    }
}
