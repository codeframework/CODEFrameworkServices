namespace CODE.Framework.Services.Contracts;

/// <summary>
/// Generic description attribute usable in all service elements
/// </summary>
/// <remarks>
/// Constructor
/// </remarks>
/// <param name="description">Description text</param>
[AttributeUsage(AttributeTargets.All, Inherited = true)]
public class DescriptionAttribute(string description) : Attribute
{

    /// <summary>
    /// Description text (depending on usage, this may or may not support markdown)
    /// </summary>
    public string Description { get; set; } = description;
}

/// <summary>
/// Tools Description attribute used to describe something for use with AI tools.
/// </summary>
/// <remarks>Constructor</remarks>
/// <param name="description">Description text</param>
[AttributeUsage(AttributeTargets.All, Inherited = true)]
public class ToolDescriptionAttribute(string description) : Attribute
{
    /// <summary>
    /// Description text (depending on usage, this may or may not support markdown)
    /// </summary>
    public string Description { get; set; } = description;
}

/// <summary>
/// Indicates whether a tool is exposed in MCP
/// </summary>
[AttributeUsage(AttributeTargets.Method, Inherited = true)]
public class ToolAttribute() : Attribute
{
    /// <summary>
    /// Indicates whether the tool is exposed in MCP
    /// </summary>
    public bool IsExposed { get; set; } = true;

    /// <summary>
    /// Exposed name of the tool. 
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Exposed human readable title of the tool
    /// </summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Description of the tool (used by AI)</summary>
    /// <remarks>If empty, the system tries to use standard descriptions applied to the method. However, it is recommended to create an AI-specific description.</remarks>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Sub-route for the MCP route. 
    /// This is appended to the base route of the service.
    /// </summary>
    /// <remarks>
    /// MCP tools are usually exposed to a client on a certain route (typically /mcp). 
    /// This attribute allows to specify a sub-route for the tool, which is appended to the base route of the service. 
    /// For example, if the base route is /mcp and the sub-route is /update, the full route for the tool would be /mcp/update.
    /// This is useful when different parts of a tools package are to be exposed as if they were separate server.
    /// For instance, some clients may not support tools that perform updates, so you may want to make those appear like a secondary MCP server.
    /// </remarks>
    public string SubRoute { get; set; }
}

/// <summary>
/// Generic summary attribute usable in all service elements
/// </summary>
/// <remarks>
/// Constructor
/// </remarks>
/// <param name="summary">Summary text</param>
[AttributeUsage(AttributeTargets.All, Inherited = true)]
public class SummaryAttribute(string summary) : Attribute
{
    /// <summary>
    /// Summary text (depending on usage, this may or may not support markdown)
    /// </summary>
    public string Summary { get; set; } = summary;
}

/// <summary>
/// Generic external documentation attribute usable in all service elements
/// </summary>
/// <remarks>
/// Constructor
/// </remarks>
/// <param name="description">Description text</param>
/// <param name="url">Full URL for the external description</param>
[AttributeUsage(AttributeTargets.All, Inherited = true)]
public class ExternalDocumentationAttribute(string description, string url) : Attribute
{
    /// <summary>
    /// Description text (depending on usage, this may or may not support markdown)
    /// </summary>
    public string Description { get; set; } = description;

    /// <summary>
    /// Full URL for the external description
    /// </summary>
    public string Url { get; set; } = url;
}

/// <summary>
/// Can be used to specify a custom content type for REST calls. (Used for things such as FileResponse contracts)
/// </summary>
[AttributeUsage(AttributeTargets.All, Inherited = true)]
public class RestContentTypeAttribute(string contentType) : Attribute
{
    /// <summary>
    /// Content type
    /// </summary>
    public string ContentType { get; set; } = contentType;
}

/// <summary>
/// Marker attribute used to indicate that a byte[] property contains a file
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = true)]
public class FileContentAttribute : Attribute { }