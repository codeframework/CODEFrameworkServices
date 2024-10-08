﻿using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace CODE.Framework.Services.Contracts;

public static class ServiceHelper
{
    public static PingResponse GetPopulatedPingResponse(this object referenceObject)
    {
        try
        {
            return new PingResponse
            {
                ServerDateTime = DateTime.Now, 
                Version = referenceObject?.GetType().Assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion,
                OperatingSystemDescription = RuntimeInformation.OSDescription,
                FrameworkDescription = RuntimeInformation.FrameworkDescription,
                Success = true,
                CodeFrameworkVersion = typeof(ServiceHelper).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>().InformationalVersion
            };
        }
        catch
        {
            return new PingResponse
            {
                Success = false,
                FailureInformation = "PingService::GetPopulatedPingResponse() - generic error."
            };
        }
    }

    public static bool ShowExtendedFailureInformation { get; set; } = false;

    public static TResponse GetPopulatedFailureResponse<TResponse>(Exception ex) where TResponse: new()
    {
        var response = new TResponse();
        return GetPopulatedFailureResponse(ex, response, 2);
    }

    public static object GetPopulatedFailureResponse(Type responseType, Exception ex, string typeName = "", string methodName = "")
    {
        var returnIsAsyncTask = false;
        if (responseType.IsGenericType && responseType.Name == "Task`1")
            returnIsAsyncTask = true;

        if (!returnIsAsyncTask)
        {
            var response = Activator.CreateInstance(responseType);
            return GetPopulatedFailureResponse(ex, response, 2, typeName, methodName);
        }

        // We are dealing with an async operation, so things are a bit more complicated
        var genericTypes = responseType.GetGenericArguments();
        if (genericTypes.Length != 1)
            throw new Exception($"ServiceHelper.GetPopulatedFailureResponse() can only handle single-generic-parameter return values of generic type.");

        var response2 = Activator.CreateInstance(genericTypes[0]);
        var response3 = GetPopulatedFailureResponse(ex, response2, 2, typeName, methodName);
        return response3; 
        //return Task.FromResult(response3); //Wrapping this in a Task results in a Task<T> being serialized and the caller receiving an invalid response
    }

    private static TResponse GetPopulatedFailureResponse<TResponse>(Exception ex, TResponse response, int stackDepth, string typeName = "", string methodName = "") where TResponse : new()
    {
        if (string.IsNullOrEmpty(typeName) || string.IsNullOrEmpty(methodName))
        {
            var frame = new StackFrame(stackDepth);
            if (string.IsNullOrEmpty(typeName))
                typeName = frame.GetMethod().DeclaringType.Name;
            if (string.IsNullOrEmpty(methodName))
                methodName = frame.GetMethod().Name;
        }
        var message = ShowExtendedFailureInformation ? GetExceptionText(ex) : $"Generic error in {typeName}::{methodName}";

        if (response is BaseServiceResponse baseResponse)
        {
            baseResponse.Success = false;
            baseResponse.FailureInformation = message;
        }
        else
        {
            var responseType = response.GetType();
            responseType?.GetProperty("Success", BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty)?.SetValue(response, false);
            responseType?.GetProperty("FailureInformation", BindingFlags.Public | BindingFlags.Instance | BindingFlags.SetProperty)?.SetValue(response, message);
        }

        var loggingMediatorType = Type.GetType("CODE.Framework.Fundamentals.Utilities.LoggingMediator, CODE.Framework.Fundamentals");
        if (loggingMediatorType != null)
        {
            var logMethods = loggingMediatorType.GetMethods(BindingFlags.Public | BindingFlags.Static);
            foreach (var logMethod in logMethods)
                if (logMethod.Name == "Log")
                {
                    var parameters = logMethod.GetParameters();
                    if (parameters.Length == 3 && parameters[0].ParameterType == typeof(string) && parameters[1].ParameterType == typeof(Exception) && parameters[2].ParameterType.Name == "LogEventType")
                    {
                        var callParameters = new object[3];
                        callParameters[0] = $"Generic error in {typeName}::{methodName} - {ex.GetType().Name}";
                        callParameters[1] = ex;
                        callParameters[2] = 4; // Exception
                        logMethod.Invoke(null, callParameters);
                        break;
                    }
                }
        }

        return response;
    }

    public static string GetExceptionText(Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Exception Stack:");
        sb.AppendLine();
        var errorCount = -1;
        while (exception != null)
        {
            errorCount++;
            if (errorCount > 0) sb.AppendLine();
            sb.Append(exception.Message);

            sb.AppendLine("  Exception Attributes:");
            sb.AppendLine($"    Message {exception.Message}");
            sb.AppendLine($"    Exception Type: {exception.GetType().Name}");
            sb.AppendLine($"    Source: {exception.Source}");

            if (exception.TargetSite != null)
            {
                sb.AppendLine($"    Thrown by Method: {exception.TargetSite.Name}");
                if (exception.TargetSite.DeclaringType != null)
                    sb.AppendLine($"    Thrown by Class: {exception.TargetSite.DeclaringType.Name}");
            }

            // Stack Trace
            if (!string.IsNullOrEmpty(exception.StackTrace))
            {
                sb.AppendLine("  Stack Trace:");
                var stackLines = exception.StackTrace.Split('\r');
                foreach (var stackLine in stackLines)
                    if (!string.IsNullOrEmpty(stackLine))
                        if (stackLine.IndexOf(" in ", StringComparison.Ordinal) > -1)
                        {
                            var detail = stackLine.Trim().Trim();
                            detail = detail.Replace("at ", string.Empty);
                            var at = detail.IndexOf(" in ", StringComparison.Ordinal);
                            var file = detail.Substring(at + 4);
                            detail = detail.Substring(0, at);
                            at = file.IndexOf(":line", StringComparison.Ordinal);
                            var lineNumber = file.Substring(at + 6);
                            file = file.Substring(0, at);
                            sb.Append($"    Line Number: {lineNumber} -- ");
                            sb.Append($"Method: {detail} -- ");
                            sb.Append($"Source File: {file}{Environment.NewLine}");
                        }
                        else
                        {
                            // We only have generic info
                            var detail = stackLine.Trim().Trim();
                            detail = detail.Replace("at ", string.Empty);
                            sb.Append($"    Method: {detail}");
                        }
            }

            // Next exception
            exception = exception.InnerException;
        }
        return sb.ToString();
    }
}