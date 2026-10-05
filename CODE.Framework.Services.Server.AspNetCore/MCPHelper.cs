using DescriptionAttribute = CODE.Framework.Services.Contracts.DescriptionAttribute;

namespace CODE.Framework.Services.Server.AspNetCore;

public static class McpHelper
{
    public static string GetDescription(MethodInfo interfaceMethod, Type methodInterface, AiToolAttribute exposedToolAttribute, Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles)
    {
        if (exposedToolAttribute != null && !string.IsNullOrEmpty(exposedToolAttribute.Description))
            return exposedToolAttribute.Description.Trim();

        var descriptionAttribute = interfaceMethod.GetCustomAttributeEx<DescriptionAttribute>();
        if (descriptionAttribute != null && !string.IsNullOrEmpty(descriptionAttribute.Description))
            return descriptionAttribute.Description.Trim();

        var componentModelDescriptionAttribute = interfaceMethod.GetCustomAttributeEx<System.ComponentModel.DescriptionAttribute>();
        if (componentModelDescriptionAttribute != null && !string.IsNullOrEmpty(componentModelDescriptionAttribute.Description))
            return componentModelDescriptionAttribute.Description.Trim();

        if (xmlDocumentationFiles.ContainsKey(methodInterface.Assembly))
        {
            var xmlDescription = xmlDocumentationFiles[methodInterface.Assembly].GetDescriptionFromXmlDocs(interfaceMethod);
            if (!string.IsNullOrEmpty(xmlDescription))
                return xmlDescription;
        }

        var summaryAttribute = interfaceMethod.GetCustomAttributeEx<SummaryAttribute>();
        if (summaryAttribute != null && !string.IsNullOrEmpty(summaryAttribute.Summary))
            return summaryAttribute.Summary.Trim();

        return string.Empty;
    }

    public static string GetDescription(Type type, Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles)
    {
        var descriptionAttribute = type.GetCustomAttributeEx<DescriptionAttribute>();
        if (descriptionAttribute != null && !string.IsNullOrEmpty(descriptionAttribute.Description))
            return descriptionAttribute.Description.Trim();

        var componentModelDescriptionAttribute = type.GetCustomAttributeEx<System.ComponentModel.DescriptionAttribute>();
        if (componentModelDescriptionAttribute != null && !string.IsNullOrEmpty(componentModelDescriptionAttribute.Description))
            return componentModelDescriptionAttribute.Description.Trim();

        if (xmlDocumentationFiles.ContainsKey(type.Assembly))
        {
            var xmlDescription = xmlDocumentationFiles[type.Assembly].GetDescriptionFromXmlDocs(type);
            if (!string.IsNullOrEmpty(xmlDescription))
                return xmlDescription;
        }

        return string.Empty;
    }

    /// <summary>
    /// Extracts the AiToolAttribute from a method's attributes. 
    /// Checks both the implementation method and the interface method (if provided),
    /// since attributes applied to interface methods don't automatically inherit to implementation methods.
    /// </summary>
    /// <param name="method">The implementation method to inspect</param>
    /// <param name="interfaceMethod">Optional interface method to check for inherited attributes</param>
    /// <returns>The applied AiToolAttribute or null if not found</returns>
    public static AiToolAttribute GetExposedAiToolAttribute(MethodInfo method, MethodInfo interfaceMethod = null)
    {
        // First check the implementation method itself
        var customAttributes = method.GetCustomAttributes(typeof(AiToolAttribute), true);
        if (customAttributes.Length > 0)
            return customAttributes[0] as AiToolAttribute;

        // If not found on implementation and interface method is provided, check the interface method
        if (interfaceMethod != null)
        {
            var interfaceAttributes = interfaceMethod.GetCustomAttributes(typeof(AiToolAttribute), true);
            if (interfaceAttributes.Length > 0)
                return interfaceAttributes[0] as AiToolAttribute;
        }

        return null;
    }

    public static void ExtractParameters(MethodInfo methodInfo, OpenApiPathInfo pathInfo, Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles)
    {
        var methodParameters = methodInfo.GetParameters();
        if (methodParameters.Length > 0)
        {
            var parameter = methodParameters[0]; // Note that in CODE Framework, there always is just a single in-parameter
            var parameterProperties = parameter.ParameterType.GetProperties();
            if (parameterProperties == null || parameterProperties.Length == 0) return; // If there are no properties on the parameter object, then there are no parameters as far as external callers are concerned

            var parameterObjectInstance = ObjectHelper.CreateInstanceFromType(parameter.ParameterType);
            foreach (var parameterProperty in parameterProperties)
            {
                var description = GetDescription(parameterProperty, xmlDocumentationFiles);

                var isRequired = false;
                var dataMemberAttribute = parameterProperty.GetCustomAttributeEx<DataMemberAttribute>();
                if (dataMemberAttribute != null)
                    isRequired = dataMemberAttribute.IsRequired;

                pathInfo.NamedParameters.Add(new OpenApiNamedOperationParameter
                {
                    Name = parameterProperty.Name,
                    Type = parameterProperty.PropertyType,
                    Required = isRequired,
                    Description = description,
                    ParentObject = parameterObjectInstance,
                    PropertyInfo = parameterProperty
                });
            }
        }
    }

    public static string GetDescription(PropertyInfo property, Dictionary<Assembly, XmlCodeDocumentationFile> xmlDocumentationFiles)
    {
        var descriptionAttribute = property.GetCustomAttributeEx<DescriptionAttribute>();
        if (descriptionAttribute != null && !string.IsNullOrEmpty(descriptionAttribute.Description))
            return descriptionAttribute.Description.Trim();

        var componentModelDescriptionAttribute = property.GetCustomAttributeEx<System.ComponentModel.DescriptionAttribute>();
        if (componentModelDescriptionAttribute != null && !string.IsNullOrEmpty(componentModelDescriptionAttribute.Description))
            return componentModelDescriptionAttribute.Description.Trim();

        if (xmlDocumentationFiles.ContainsKey(property.DeclaringType.Assembly))
        {
            var xmlDescription = xmlDocumentationFiles[property.DeclaringType.Assembly].GetDescriptionFromXmlDocs(property);
            if (!string.IsNullOrEmpty(xmlDescription))
                return xmlDescription;
        }

        return string.Empty;
    }

    public static string GetToolName(HostedServiceDescription serviceDescription, ServiceOperationDescription operation)
    {
        var toolName = $"{serviceDescription.ContractType.Name}_{operation.Name}";

        var interfaceMethod = serviceDescription.ContractType?.GetMethod(operation.Method.Name);
        var exposedToolAttribute = GetExposedAiToolAttribute(operation.Method, interfaceMethod);
        if (exposedToolAttribute != null && !string.IsNullOrEmpty(exposedToolAttribute.Name))
            toolName = exposedToolAttribute.Name;

        toolName = new string(toolName.Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        while (toolName.Contains("__"))
            toolName = toolName.Replace("__", "_");
        toolName = toolName.Trim('_');

        return toolName;
    }

    public static string GetToolTitle(HostedServiceDescription serviceDescription, ServiceOperationDescription operation)
    {
        if (!string.IsNullOrEmpty(operation.Title)) return operation.Title; 

        var title = string.Empty;

        var interfaceMethod = serviceDescription.ContractType?.GetMethod(operation.Method.Name);
        var exposedToolAttribute = GetExposedAiToolAttribute(operation.Method, interfaceMethod);
        if (exposedToolAttribute != null && !string.IsNullOrEmpty(exposedToolAttribute.Title))
            title = exposedToolAttribute.Title;

        if (string.IsNullOrEmpty(title))
        {
            var toolName = GetToolName(serviceDescription, operation);
            if (!string.IsNullOrEmpty(toolName))
            {
                if (toolName.Length > 1 && toolName.Substring(0, 1) == "I" && toolName.Substring(1, 1) == toolName.Substring(1, 1).ToUpper())
                    toolName = toolName.Substring(1);
                toolName = toolName.Replace("_", " ");
                toolName = toolName.Replace("  ", " ");
                toolName = toolName.Replace("  ", " ");
                title = StringHelper.SpaceCamelCase(toolName);
            }
        }

        title = title.Replace("  ", " ");
        title = title.Replace("  ", " ");

        if (string.IsNullOrEmpty(operation.Title))
            operation.Title = title;

        return title;
    }

    public static bool ToolNameMatchesPath(string toolName, string path, HostedServiceDescription serviceDescription, ServiceOperationDescription operation)
    {
        var toolExposedName = GetToolName(serviceDescription, operation);
        if (string.IsNullOrEmpty(toolExposedName)) return false;

        // If the tool name isn't a match, we can forget about it
        if (!toolName.Equals(toolExposedName, StringComparison.OrdinalIgnoreCase)) return false;

        // The tool name is a match, but are we on the right route?
        var interfaceMethod = serviceDescription.ContractType?.GetMethod(operation.Method.Name);
        var exposedToolAttribute = GetExposedAiToolAttribute(operation.Method, interfaceMethod);
        var fullRoute = serviceDescription.Configuration.McpRouteBasePath;
        if (!string.IsNullOrEmpty(exposedToolAttribute.SubRoute))
            fullRoute = $"{fullRoute}/{exposedToolAttribute.SubRoute}".Replace("//", "/");
        return fullRoute.Equals(path, StringComparison.OrdinalIgnoreCase);
    }
}