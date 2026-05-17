namespace Tjb.Web.Services
{
    public class LoggingEmailService : IEmailService
    {
        private readonly ILogger<LoggingEmailService> _logger;

        public LoggingEmailService(ILogger<LoggingEmailService> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default)
        {
            _logger.LogInformation(
                "[STUB EMAIL] To: {To} | Subject: {Subject}\nText body:\n{TextBody}\nHTML body:\n{HtmlBody}",
                request.To, request.Subject, request.TextBody, request.HtmlBody);

            return Task.CompletedTask;
        }
    }
}
