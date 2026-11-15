namespace CODE.Framework.Services.Client;

/// <summary>A transport failure from the options-based REST client. Payload conversion failures remain serialization exceptions.</summary>
public class ServiceTransportException : CommunicationException
{
    /// <summary>HTTP status when a response was received, otherwise null.</summary>
    public HttpStatusCode? StatusCode { get; }

    public ServiceTransportException(string message, HttpStatusCode? statusCode, Exception innerException) : base(message, innerException)
    {
        StatusCode = statusCode;
    }
}
