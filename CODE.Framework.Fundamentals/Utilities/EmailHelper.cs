using System.Linq;
using System.Net;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace CODE.Framework.Fundamentals.Utilities
{
    /// <summary>
    /// This class provides basic functionality for email features.
    /// </summary>
    public static class EmailHelper
    {
        /// <summary>
        /// Sends an email to the specified recipient
        /// </summary>
        /// <param name="senderName">Sender Name</param>
        /// <param name="senderEmail">Sender Email Address</param>
        /// <param name="recipientName">Recipient Name</param>
        /// <param name="recipientEmail">Recipient Email Address</param>
        /// <param name="subject">Subject</param>
        /// <param name="textBody">Email Body (text-only version)</param>
        /// <param name="htmlBody">Email Body (HTML version)</param>
        /// <param name="mailServer">Mail server used to route the email. If null or not supplied, uses DefaultMailServer appSetting from config file</param>
        /// <param name="portNumber">If null or not supplied, uses default of 25</param>
        /// <param name="userName">Only required if SMTP server requires authentication to send</param>
        /// <param name="password">Only required if SMTP server requires authentication to send</param>
        /// <param name="attachments">(Optional) Attachments to send with the email</param>
        /// <returns>True if sent successfully</returns>
        public static bool SendEmail(string senderName, string senderEmail, string recipientName, string recipientEmail, string subject, string textBody, string htmlBody, string mailServer = null, int portNumber = 25, string userName = null, string password = null, List<Attachment> attachments = null)
        {
            if (string.IsNullOrEmpty(mailServer))
            {
                if (Configuration.ConfigurationSettings.Settings.IsSettingSupported("DefaultMailServer"))
                    mailServer = Configuration.ConfigurationSettings.Settings["DefaultMailServer"];
                else
                    throw new Exception("DefaultMailServer config setting is missing.");
            }

            var processor = _emailProcessors.FirstOrDefault(p => p.MailServer.ToLowerInvariant() == mailServer.ToLowerInvariant());
            if (processor == null)
                processor = _emailProcessors.FirstOrDefault(p => p.MailServer == "*");
            if (processor == null)
            {
                LoggingMediator.Log($"No email processor is registered with the EmailHelper class to process messages through mail server {mailServer}.", LogEventType.Error);
                return false;
            }

            return processor.Processor.SendEmail(senderName, senderEmail, recipientName, recipientEmail, subject, textBody, htmlBody, mailServer, portNumber, userName, password, attachments);
        }

        /// <summary>
        /// Removes all currently registered email processors.
        /// </summary>
        /// <remarks>Note that at least 1 processor needs to be registered in order to process any messages.</remarks>
        /// <returns>True if successful</returns>
        public static bool ClearEmailProcessors()
        {
            _emailProcessors.Clear();
            return true;
        }

        /// <summary>
        /// Adds or replaces an email processor for the specified mail server
        /// </summary>
        /// <param name="processor">The 'processor' object that actually sends the email</param>
        /// <param name="forMailServer">
        /// If a certain mail server is specified, then the processor will only be used,
        /// when the specified mail server for the individual messages is a match.
        /// * (default) applies to all messages/servers if there is no more specific match
        /// </param>
        /// <returns></returns>
        public static bool AddEmailProcessor(IEmailProcessor processor, string forMailServer = "*")
        {
            var currentProcessor = _emailProcessors.FirstOrDefault(p => p.MailServer.ToLowerInvariant() == forMailServer.ToLowerInvariant());
            if (currentProcessor != null)
                currentProcessor.Processor = processor;
            else
                _emailProcessors.Add(new EmailProcessorRegistration { MailServer = forMailServer, Processor = processor });
            return true;
        }

        private static readonly List<EmailProcessorRegistration> _emailProcessors = new() { new EmailProcessorRegistration { Processor = new SmtpEmailProcessor() } };

        /// <summary>
        /// This method returns true if the email address is well formed and thus COULD be valid.
        /// </summary>
        /// <param name="email">Email address, such as billg@microsoft.com</param>
        /// <returns>True if the address appears to be valid.</returns>
        /// <remarks>
        /// This method does NOT check whether the address in fact does exist as a valid address on a mail server.
        /// </remarks>
        public static bool IsEmailAddressWellFormed(string email)
        {
            var reg = new Regex("\\w+([-+.]\\w+)*@\\w+([-.]\\w+)*\\.\\w+([-.]\\w+)*");
            return reg.IsMatch(email.Trim());
        }

        private class EmailProcessorRegistration
        {
            public IEmailProcessor Processor { get; set; }
            public string MailServer { get; set; } = "*";
        }
    }

    /// <summary>
    /// Base interface for email processors (these are the objects that are used to process actual email messages)
    /// </summary>
    public interface IEmailProcessor
    {
        /// <summary>
        /// Sends an email to the specified recipient
        /// </summary>
        /// <param name="senderName">Sender Name</param>
        /// <param name="senderEmail">Sender Email Address</param>
        /// <param name="recipientName">Recipient Name</param>
        /// <param name="recipientEmail">Recipient Email Address</param>
        /// <param name="subject">Subject</param>
        /// <param name="textBody">Email Body (text-only version)</param>
        /// <param name="htmlBody">Email Body (HTML version)</param>
        /// <param name="mailServer">Mail server used to route the email. If null or not supplied, uses DefaultMailServer appSetting from config file</param>
        /// <param name="portNumber">If null or not supplied, uses default of 25</param>
        /// <param name="userName">Only required if SMTP server requires authentication to send</param>
        /// <param name="password">Only required if SMTP server requires authentication to send</param>
        /// <param name="attachments">(Optional) Attachments to send with the email</param>
        /// <returns>True if sent successfully</returns>
        bool SendEmail(string senderName, string senderEmail, string recipientName, string recipientEmail, string subject, string textBody, string htmlBody, string mailServer, int portNumber, string userName, string password, List<Attachment> attachments);
    }

    public class SmtpEmailProcessor : IEmailProcessor
    {
        public bool SendEmail(string senderName, string senderEmail, string recipientName, string recipientEmail, string subject, string textBody, string htmlBody, string mailServer, int portNumber, string userName, string password, List<Attachment> attachments)
        {
            var smtp = new SmtpClient(mailServer, portNumber);
            if (!string.IsNullOrWhiteSpace(userName) && !string.IsNullOrWhiteSpace(password)) 
                smtp.Credentials = new NetworkCredential(userName, password);

            using var message = new MailMessage(new MailAddress(senderEmail, senderName), new MailAddress(recipientEmail, recipientName))
            {
                Subject = subject,
                IsBodyHtml = !string.IsNullOrEmpty(htmlBody)
            };
            htmlBody ??= string.Empty;
            textBody ??= string.Empty;
            message.Body = message.IsBodyHtml ? htmlBody : textBody;
            if (attachments != null)
                foreach (var attachment in attachments)
                    message.Attachments.Add(attachment);

            smtp.Send(message);
            return true;
        }
    }
}