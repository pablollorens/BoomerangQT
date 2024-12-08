using System;
using System.Net;
using System.Net.Mail;
using TradingPlatform.BusinessLayer;
using TradingPlatform.BusinessLayer.Utils;

namespace BoomerangQT
{
    public partial class BoomerangQT
    {
        [InputParameter("Enable Email Notifications", 200)]
        public bool enableEmailNotification = false;

        [InputParameter("Email Recipient", 201)]
        public string emailRecipient = "your_email@gmail.com";

        [InputParameter("Email Subject", 202)]
        public string emailSubject = "Notification trade closed";

        [InputParameter("Email Body Template", 203)]
        public string emailBodyTemplate = "Closed trade in {Symbol} with PnL: {PnL}, Max Drawdown: {MaxDrawdown}";

        [InputParameter("SMTP Server", 204)]
        public string smtpServer = "smtp.server.com";

        [InputParameter("SMTP Port", 205)]
        public int smtpPort = 587;

        [InputParameter("SMTP Username", 206)]
        public string smtpUser = "sender@gmail.com";

        [InputParameter("SMTP Password", 207)]
        public string smtpPassword = "your_smtp_app_password";

        private void SendTradeClosedEmail(string symbol, double pnl, double maxDrawdown)
        {
            if (!enableEmailNotification)
                return;

            string body = emailBodyTemplate
                .Replace("{Symbol}", symbol)
                .Replace("{PnL}", pnl.ToString("F2"))
                .Replace("{MaxDrawdown}", maxDrawdown.ToString("F2"));

            SendEmail(emailRecipient, emailSubject, body);
        }

        private void SendEmail(string to, string subject, string body)
        {
            try
            {
                using (var client = new SmtpClient(smtpServer, smtpPort))
                {
                    client.UseDefaultCredentials = false; // Make sure default creds are not used
                    client.EnableSsl = true; // Attempt STARTTLS
                    client.Credentials = new NetworkCredential(smtpUser, smtpPassword);

                    var mail = new MailMessage();
                    mail.From = new MailAddress(smtpUser);
                    mail.To.Add(to);
                    mail.Subject = subject;
                    mail.Body = body;

                    client.Send(mail);
                }
            }
            catch (SmtpException ex)
            {
                Log($"SMTP Error sending email: {ex.Message} - {ex.InnerException?.Message}", StrategyLoggingLevel.Error);
            }
            catch (Exception ex)
            {
                Log($"Error sending email: {ex.Message}", StrategyLoggingLevel.Error);
            }
        }
    }
}
