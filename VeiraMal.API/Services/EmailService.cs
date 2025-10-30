using Microsoft.Extensions.Configuration;
using System.Threading.Tasks;
using System.Net;
using System.Net.Mail;
using VeiraMal.API.Services.Interfaces;


namespace VeiraMal.API.Services
{
    public class EmailService : IEmailService
    {
        private readonly IConfiguration _cfg;
        private readonly string _host;
        private readonly int _port;
        private readonly string _username;
        private readonly string _password;
        private readonly string _from;
        private readonly string _fromName;

        public EmailService(IConfiguration cfg)
        {
            _cfg = cfg;
            var smtp = _cfg.GetSection("Smtp");
            _host = smtp.GetValue<string>("Host") ?? "";
            _port = smtp.GetValue<int>("Port");
            _username = smtp.GetValue<string>("Username") ?? "";
            _password = smtp.GetValue<string>("Password") ?? "";
            _from = smtp.GetValue<string>("From") ?? _username;
            _fromName = smtp.GetValue<string>("FromName") ?? "No Reply";
        }

        public async Task SendEmailAsync(string toEmail, string subject, string htmlBody)
        {
            using var client = new SmtpClient(_host, _port)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(_username, _password)
            };

            var mail = new MailMessage()
            {
                From = new MailAddress(_from, _fromName),
                Subject = subject,
                Body = htmlBody,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
        }
    }
}
