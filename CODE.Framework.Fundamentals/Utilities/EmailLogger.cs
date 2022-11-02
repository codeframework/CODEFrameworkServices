using System.Threading;

namespace CODE.Framework.Fundamentals.Utilities
{
    public class EmailLogger : Logger
    {
        private readonly string _senderName;
        private readonly string _senderEmail;
        private readonly List<EmailRecipient> _recipients;
        private readonly string _appName;
        private readonly string _mailServer;
        private readonly int _portNumber;
        private readonly string _userName;
        private readonly string _password;

        public EmailLogger(
          string senderName,
          string senderEmail,
          List<EmailRecipient> recipients,
          string appName,
          string mailServer = null,
          int portNumber = 25,
          string userName = null,
          string password = null)
        {
            _senderName = senderName;
            _senderEmail = senderEmail;
            _recipients = recipients;
            _appName = appName;
            _mailServer = mailServer;
            _portNumber = portNumber;
            _userName = userName;
            _password = password;
        }

        public override void Log(string logEvent, LogEventType type)
        {
            if (_recipients.Count == 0)
                throw new ArgumentOutOfRangeException("You must specify at least 1 recipient email address");
            var subject = Enum.GetName(typeof(LogEventType), type) + ": Log Entry for " + _appName;
            ThreadPool.QueueUserWorkItem((WaitCallback)(c =>
            {
                foreach (EmailRecipient recipient in _recipients)
                {
                    try
                    {
                        EmailHelper.SendEmail(_senderName, _senderEmail, recipient.Name, recipient.EmailAddress, subject, logEvent, null, _mailServer, _portNumber, _userName, _password, null);
                    }
                    catch
                    {
                        //If this fails, we'll have to count on the other loggers. 
                    }
                }
            }));
        }
    }

    public class EmailRecipient
    {
        public EmailRecipient()
        {
            Name = string.Empty;
            EmailAddress = string.Empty;
        }

        public string Name { get; set; }

        public string EmailAddress { get; set; }
    }
}
