using CODE.Framework.Fundamentals.Properties;
using System.Diagnostics;
using System.Net;

namespace CODE.Framework.Fundamentals.Utilities;

/// <summary>
/// Various helper methods related to exceptions
/// </summary>
public static class ExceptionHelper
{
    /// <summary>
    /// Analyzes exception information and returns HTML with details about the exception.
    /// </summary>
    /// <param name="exception">Exception object</param>
    /// <returns>Exception HTML</returns>
    public static string GetExceptionHtml(Exception exception)
    {
        var sb = new StringBuilder();
        sb.Append($"""<table><tr><td><font face="Verdana" size="-1">{WebUtility.HtmlEncode(Resources.ExceptionStack)}<br/>""");
        var errorCount = -1;
        while (exception != null)
        {
            errorCount++;
            if (errorCount > 0) sb.Append("<br>");
            // + "icon"
            sb.Append($"""
                <strong><span onclick="javascript:ShowError('error{StringHelper.ToString(errorCount)}','errorTable{StringHelper.ToString(errorCount)}');" 
                         name="error{StringHelper.ToString(errorCount)}" 
                         id="error{StringHelper.ToString(errorCount)}">+</span>&nbsp;{WebUtility.HtmlEncode(exception.Message)}</strong>
                <table width = "100%" id="errorTable{StringHelper.ToString(errorCount)}" style="display:none">
                  <tr>
                    <td width="25"> </td>
                    <td valign="top" bgcolor="#ffffcc"><font face="Tahoma" size="-1" color="maroon"><strong>{WebUtility.HtmlEncode(Resources.ExceptionAttributes)}</strong><br/>
                      <table>
                        <tr>
                          <td> </td>
                          <td style="BORDER-BOTTOM: black 1px solid"><font face="Tahoma" size="-1"><font color="Navy">{WebUtility.HtmlEncode(Resources.ExceptionInformation)}</font></font></td>
                          <td style="BORDER-BOTTOM: black 1px solid"><font face="Tahoma" size="-1"><font color="Navy">{WebUtility.HtmlEncode(Resources.ExceptionDetail)}</font></font></td>
                        </tr>
                        <tr>
                          <td> </td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(Resources.Message)}</font></td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(exception.Message)}</font></td>
                        </tr>
                        <tr>
                          <td> </td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(Resources.ExceptionType)}</font></td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(StringHelper.ToString(exception.GetType()))}</font></td>
                        </tr>
                        <tr>
                          <td> </td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(Resources.Source)}</font></td>
                          <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(exception.Source)}</font></td>
                        </tr>
                """);

            if (exception.TargetSite != null)
            {
                sb.Append($"""
                            <tr>
                              <td> </td>
                              <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(Resources.ThrownByMethod)}</font></td>
                              <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(exception.TargetSite.Name)}</font></td>
                            </tr>
                            <tr>
                              <td> </td>
                              <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(Resources.ThrownByClass)}</font></td>
                              <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(exception.TargetSite.DeclaringType?.Name)}</font></td>
                            </tr>
                    """);
            }
            sb.AppendLine("</table>");

            // Stack Trace
            sb.Append($"""
                {WebUtility.HtmlEncode(Resources.StackTrace)}<br/>
                <table>
                  <tr>
                    <td> </td>
                    <td style="BORDER-BOTTOM: black 1px solid"><font face="Tahoma" size="-1"><font color="Navy">{WebUtility.HtmlEncode(Resources.LineNumber)}</font></font></td>
                    <td style="BORDER-BOTTOM: black 1px solid"><font face="Tahoma" size="-1"><font color="Navy">{WebUtility.HtmlEncode(Resources.Method)}</font></font></td>
                    <td style="BORDER-BOTTOM: black 1px solid"><font face="Tahoma" size="-1"><font color="Navy">{WebUtility.HtmlEncode(Resources.SourceFile)}</font></font></td>
                  </tr>
                """);
            foreach (var frame in new StackTrace(exception, true).GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                var detail = method == null ? Resources.NotApplicable : $"{method.DeclaringType?.FullName}.{method}";
                var file = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                sb.Append($"""
                      <tr>
                        <td> </td>
                        <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(lineNumber > 0 ? lineNumber.ToString() : Resources.NotApplicable)}</font></td>
                        <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(detail)}</font></td>
                        <td><font face="Tahoma" size="-1">{WebUtility.HtmlEncode(file)}</font></td>
                      </tr>
                    """);
            }
            sb.Append("""
                      </table>
                    </font></td>
                  </tr>
                </table>
                """);

            // Next exception
            exception = exception.InnerException;
        }
        sb.Append("""
                </font>
              </td>
            </tr>
            </table>
            """);
        // Script needed to expand and collapse
        sb.AppendLine();
        sb.AppendLine("""
            <script language="JavaScript">
            function ShowError(id,idTable) {
               obj = document.getElementById(idTable);
               obj2 = document.getElementById(id);
               if (obj.style.display == "none") {
                   obj2.innerHTML = "-";
                   obj.style.display = "";
               }
               else {
                   obj2.innerHTML = "+";
                   obj.style.display = "none";
               }
            }
            </script>
            """.Replace("\r\n", "\n").Replace("\n", Environment.NewLine));
        return sb.ToString();
    }

    /// <summary>
    /// Analyzes exception information and returns it as a plain text string
    /// </summary>
    /// <param name="exception">Exception object</param>
    /// <returns>string</returns>
    public static string GetExceptionText(Exception exception)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Resources.ExceptionStack);
        sb.AppendLine();
        var errorCount = -1;
        while (exception != null)
        {
            errorCount++;
            if (errorCount > 0) sb.AppendLine();
            sb.AppendLine(exception.Message);

            sb.AppendLine($"""
                  {Resources.ExceptionAttributes}
                    {Resources.Message} {StringHelper.ToStringSafe(exception.Message)}
                    {Resources.ExceptionType} {StringHelper.ToStringSafe(exception.GetType())}
                    {Resources.Source} {StringHelper.ToStringSafe(exception.Source)}
                """);
            if (exception.TargetSite != null)
            {
                sb.AppendLine($"{Resources.ThrownByMethod} {StringHelper.ToStringSafe(exception.TargetSite.Name)}");
                if (exception.TargetSite.DeclaringType != null)
                    sb.AppendLine($"{Resources.ThrownByClass} {StringHelper.ToStringSafe(exception.TargetSite.DeclaringType.Name)}");
            }

            sb.AppendLine(Resources.StackTrace);
            foreach (var frame in new StackTrace(exception, true).GetFrames() ?? Array.Empty<StackFrame>())
            {
                var method = frame.GetMethod();
                var detail = method == null ? Resources.NotApplicable : $"{method.DeclaringType?.FullName}.{method}";
                var file = frame.GetFileName();
                var lineNumber = frame.GetFileLineNumber();
                if (!string.IsNullOrEmpty(file))
                    sb.AppendLine($"{Resources.LineNumber}: {(lineNumber > 0 ? lineNumber.ToString() : Resources.NotApplicable)} -- {Resources.Method}: {detail} -- {Resources.SourceFile}: {file}");
                else
                    sb.AppendLine($"{Resources.Method}: {detail}");
            }

            exception = exception.InnerException;
        }
        return sb.ToString().Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", Environment.NewLine);
    }
}