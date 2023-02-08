using CoreWCF.Dispatcher;


namespace CODE.Framework.Services.Wcf;

/// <summary>
/// Custom endpoint behavior object that gets applied automatically when script-cross-domain-calls are enabled on the ServiceGarden class.
/// </summary>
public class CrossDomainScriptBehavior : IEndpointBehavior
{
    /// <summary>
    /// Implements a modification or extension of the service across an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint that exposes the contract.</param>
    /// <param name="endpointDispatcher">The endpoint dispatcher to be modified or extended.</param>
    public void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
    {
        foreach (var channelEndpoint in endpointDispatcher.ChannelDispatcher.Endpoints)
            channelEndpoint.DispatchRuntime.MessageInspectors.Add(new CrossDomainScriptCallMessageInspector());
    }
    /// <summary>
    /// Implement to confirm that the endpoint meets some intended criteria.
    /// </summary>
    /// <param name="endpoint">The endpoint to validate.</param>
    public void Validate(ServiceEndpoint endpoint) { }
    /// <summary>
    /// Applies the dispatch behavior.
    /// </summary>
    /// <param name="operationDescription">The operation description.</param>
    /// <param name="dispatchOperation">The dispatch operation.</param>
    public void ApplyDispatchBehavior(OperationDescription operationDescription, DispatchOperation dispatchOperation) { }
    /// <summary>
    /// Implement to pass data at runtime to bindings to support custom behavior.
    /// </summary>
    /// <param name="endpoint">The endpoint to modify.</param>
    /// <param name="bindingParameters">The objects that binding elements require to support the behavior.</param>
    public void AddBindingParameters(ServiceEndpoint endpoint, BindingParameterCollection bindingParameters) { }
    /// <summary>
    /// Implements a modification or extension of the client across an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint that is to be customized.</param>
    /// <param name="clientRuntime">The client runtime to be customized.</param>
    public void ApplyClientBehavior(ServiceEndpoint endpoint, ClientRuntime clientRuntime) { }
}

/// <summary>
/// Inspector object used to add a cross-domain-call HTTP header
/// </summary>
public class CrossDomainScriptCallMessageInspector : IDispatchMessageInspector
{
    /// <summary>
    /// Called after an inbound message has been received but before the message is dispatched to the intended operation.
    /// </summary>
    /// <param name="request">The request message.</param>
    /// <param name="channel">The incoming channel.</param>
    /// <param name="instanceContext">The current service instance.</param>
    /// <returns>
    /// The object used to correlate state. This object is passed back in the <see cref="M:System.ServiceModel.Dispatcher.IDispatchMessageInspector.BeforeSendReply(System.ServiceModel.Channels.Message@,System.Object)"/> method.
    /// </returns>
    public object AfterReceiveRequest(ref Message request, IClientChannel channel, InstanceContext instanceContext) => null;

    /// <summary>
    /// Called after the operation has returned but before the reply message is sent.
    /// </summary>
    /// <param name="reply">The reply message. This value is null if the operation is one way.</param>
    /// <param name="correlationState">The correlation object returned from the <see cref="M:System.ServiceModel.Dispatcher.IDispatchMessageInspector.AfterReceiveRequest(System.ServiceModel.Channels.Message@,System.ServiceModel.IClientChannel,System.ServiceModel.InstanceContext)"/> method.</param>
    public void BeforeSendReply(ref Message reply, object correlationState)
    {
        HttpResponseMessageProperty httpResponseMessage;
        object httpResponseMessageObject;
        if (reply.Properties.TryGetValue(HttpResponseMessageProperty.Name, out httpResponseMessageObject))
        {
            httpResponseMessage = httpResponseMessageObject as HttpResponseMessageProperty;
            if (httpResponseMessage != null)
                if (string.IsNullOrEmpty(httpResponseMessage.Headers["Access-Control-Allow-Origin"]))
                    httpResponseMessage.Headers["Access-Control-Allow-Origin"] = ServiceGarden.HttpCrossDomainCallsAllowedFrom;
        }
        else
        {
            httpResponseMessage = new HttpResponseMessageProperty();
            httpResponseMessage.Headers.Add("Access-Control-Allow-Origin", ServiceGarden.HttpCrossDomainCallsAllowedFrom);
            reply.Properties.Add(HttpResponseMessageProperty.Name, httpResponseMessage);
        }
    }
}

/// <summary>
/// Endpoint behavior configuration specific to XML formatted REST calls
/// </summary>
public class RestXmlHttpBehavior : WebHttpBehavior
{
    public RestXmlHttpBehavior(IServiceProvider serviceProvider) : base(serviceProvider) { }

    /// <summary>Handles REST XML formatting behavior</summary>
    /// <param name="operationDescription"></param>
    /// <param name="endpoint"></param>
    /// <returns></returns>
    protected override IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint)
    {
        var webInvoke = GetBehavior<WebInvokeAttribute>(operationDescription);
        if (webInvoke == null)
        {
            webInvoke = new WebInvokeAttribute();
            operationDescription.OperationBehaviors.Add(webInvoke);
        }
        webInvoke.RequestFormat = WebMessageFormat.Xml;
        webInvoke.ResponseFormat = WebMessageFormat.Xml;
        webInvoke.Method = RestHelper.GetHttpMethodFromOperationDescription(operationDescription);

        var formatter = base.GetReplyDispatchFormatter(operationDescription, endpoint);
        return formatter;
    }

    /// <summary>
    /// Gets the request dispatch formatter.
    /// </summary>
    /// <param name="operationDescription">The operation description.</param>
    /// <param name="endpoint">The endpoint.</param>
    /// <returns>IDispatchMessageFormatter.</returns>
    protected override IDispatchMessageFormatter GetRequestDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint)
    {
        if (IsGetOperation(operationDescription))
            // no change for GET operations
            return base.GetRequestDispatchFormatter(operationDescription, endpoint);

        if (operationDescription.Messages[0].Body.Parts.Count == 0)
            // nothing in the body, still use the default
            return base.GetRequestDispatchFormatter(operationDescription, endpoint);

        return new NewtonsoftJsonDispatchFormatter(operationDescription, true);
    }

    /// <summary>
    /// Determines whether the operation is a GET operation.
    /// </summary>
    /// <param name="operation">The operation.</param>
    /// <returns><c>true</c> if [is get operation] [the specified operation]; otherwise, <c>false</c>.</returns>
    private static bool IsGetOperation(OperationDescription operation)
    {
        var wga = Find<WebGetAttribute>(operation.OperationBehaviors);
        if (wga != null) return true;

        var wia = Find<WebInvokeAttribute>(operation.OperationBehaviors);
        if (wia != null) return wia.Method == "HEAD";

        return false;
    }

    private static T Find<T>(KeyedCollection<Type, IOperationBehavior> collection)
    {
        for (var counter = 0; counter < collection.Count; counter++)
        {
            var val = collection[counter];
            if (val is T)
                return (T)(object)val;
        }

        return default;
    }


    /// <summary>
    /// Tries to find a behavior attribute of a certain type and returns it
    /// </summary>
    /// <typeparam name="T">Type of behavior we are looking for</typeparam>
    /// <param name="operationDescription">Operation description</param>
    /// <returns>Behavior or null</returns>
    private static T GetBehavior<T>(OperationDescription operationDescription) where T : class
    {
        foreach (var behavior in operationDescription.OperationBehaviors)
            if (behavior is T webGetAttribute)
                return webGetAttribute;
        return null;
    }
}

/// <summary>
/// Endpoint behavior configuration specific to XML formatted REST calls
/// </summary>
public class RestJsonHttpBehavior : WebHttpBehavior
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RestJsonHttpBehavior" /> class.
    /// </summary>
    /// <param name="rootUrl">The root URL.</param>
    /// <param name="contractType">Type of the contract.</param>
    public RestJsonHttpBehavior(IServiceProvider serviceProvider, string rootUrl, Type contractType) : base(serviceProvider)
    {
        _rootUrl = rootUrl;
        _contractType = contractType;
    }

    private readonly string _rootUrl;
    private readonly Type _contractType;

    /// <summary>Handles REST JSON formatting behavior</summary>
    /// <param name="operationDescription"></param>
    /// <param name="endpoint"></param>
    /// <returns></returns>
    protected override IDispatchMessageFormatter GetReplyDispatchFormatter(OperationDescription operationDescription, ServiceEndpoint endpoint)
    {
        var webInvoke = operationDescription.OperationBehaviors.OfType<WebInvokeAttribute>().FirstOrDefault();
        if (webInvoke == null)
        {
            webInvoke = new WebInvokeAttribute();
            operationDescription.OperationBehaviors.Add(webInvoke);
        }
        webInvoke.RequestFormat = WebMessageFormat.Json;
        webInvoke.ResponseFormat = WebMessageFormat.Json;
        webInvoke.Method = RestHelper.GetHttpMethodFromOperationDescription(operationDescription);

        if (operationDescription.Messages.Count == 1 || operationDescription.Messages[1].Body.ReturnValue.Type == typeof(void))
            return base.GetReplyDispatchFormatter(operationDescription, endpoint);
        return new NewtonsoftJsonDispatchFormatter(operationDescription, false);
    }

    /// <summary>
    /// Implements the <see cref="M:System.ServiceModel.Description.IEndpointBehavior.ApplyDispatchBehavior(System.ServiceModel.Description.ServiceEndpoint,System.ServiceModel.Dispatcher.EndpointDispatcher)" /> method to support modification or extension of the client across an endpoint.
    /// </summary>
    /// <param name="endpoint">The endpoint that exposes the contract.</param>
    /// <param name="endpointDispatcher">The endpoint dispatcher to which the behavior is applied.</param>
    public override void ApplyDispatchBehavior(ServiceEndpoint endpoint, EndpointDispatcher endpointDispatcher)
    {
        base.ApplyDispatchBehavior(endpoint, endpointDispatcher);

        endpointDispatcher.DispatchRuntime.MessageInspectors.Add(new RestDispatchMessageInspector(_rootUrl, _contractType));
        endpointDispatcher.DispatchRuntime.OperationSelector = new RestJsonOperationSelector(_rootUrl, _contractType);
    }
}