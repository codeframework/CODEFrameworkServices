using System.Threading;

#nullable enable

namespace CODE.Framework.Services.Server.AspNetCore;

/// <summary>Validates API keys for a hosted service. Registered instances are shared and must be thread-safe.</summary>
public interface IApiKeyHandler
{
    /// <summary>Returns true to admit the request. An absent header is passed as null; exceptions fail the request.</summary>
    Task<bool> CheckApiKeyAsync(Type contractType, string? key, ServiceHandlerRequestContext context, CancellationToken cancellationToken);
}
