using System.Collections.Concurrent;
using System.Runtime.Serialization;
using System.ServiceModel;
using CODE.Framework.Services.Contracts;

namespace CODE.Framework.Services.Tests;

[ServiceContract]
public interface ITestService
{
    [OperationContract, AiTool(Name = "calculate", Description = "Calculate the sum of two numbers.")]
    CalculationResponse Calculate(CalculationRequest request);

    [OperationContract, Rest(Method = RestMethods.Get, Name = "lookup")]
    CalculationResponse Lookup(CalculationRequest request);

    [OperationContract, Rest(Method = RestMethods.Put, Name = "replace")]
    CalculationResponse Replace(CalculationRequest request);

    [OperationContract, Rest(Method = RestMethods.Delete, Name = "remove")]
    CalculationResponse Remove(CalculationRequest request);

    [OperationContract, Rest(Method = RestMethods.Get)]
    FileResponse Download(CalculationRequest request);

    [OperationContract, StandardExceptionHandling]
    CalculationResponse Fail(CalculationRequest request);
}

[DataContract]
public class CalculationRequest
{
    [DataMember, RestUrlParameter(Mode = UrlParameterMode.Inline)]
    public Guid RequestId { get; set; }

    [DataMember, RestUrlParameter]
    public int Left { get; set; }

    [DataMember, RestUrlParameter]
    public int Right { get; set; }

    [DataMember, RestUrlParameter]
    public string Text { get; set; }
}

[DataContract]
public class CalculationResponse : BaseServiceResponse
{
    [DataMember]
    public Guid RequestId { get; set; }

    [DataMember]
    public Guid ServerId { get; set; }

    [DataMember]
    public int Total { get; set; }

    [DataMember]
    public string Text { get; set; }
}

public record Execution(Guid RequestId, int Left, int Right, string Text, string Operation);

public class ExecutionLog
{
    public Guid ServerId { get; } = Guid.NewGuid();
    public ConcurrentQueue<Execution> Entries { get; } = new();
}

public class TestService(ExecutionLog log) : ITestService
{
    public CalculationResponse Calculate(CalculationRequest request) => Execute(request, nameof(Calculate));

    public CalculationResponse Lookup(CalculationRequest request) => Execute(request, nameof(Lookup));

    public CalculationResponse Replace(CalculationRequest request) => Execute(request, nameof(Replace));

    public CalculationResponse Remove(CalculationRequest request) => Execute(request, nameof(Remove));

    public FileResponse Download(CalculationRequest request)
    {
        Execute(request, nameof(Download));
        return new FileResponse { FileBytes = request.RequestId.ToByteArray(), FileName = "receipt.bin", ContentType = "application/octet-stream" };
    }

    public CalculationResponse Fail(CalculationRequest request)
    {
        Execute(request, nameof(Fail));
        throw new InvalidOperationException("Deliberate test failure");
    }

    private CalculationResponse Execute(CalculationRequest request, string operation)
    {
        log.Entries.Enqueue(new Execution(request.RequestId, request.Left, request.Right, request.Text, operation));
        return new CalculationResponse { RequestId = request.RequestId, ServerId = log.ServerId, Total = request.Left + request.Right, Text = request.Text, Success = true };
    }
}
