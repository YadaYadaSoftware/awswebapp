using Amazon.SimpleEmail;
using Amazon.SimpleEmail.Model;
using Microsoft.Extensions.Options;
using SesSendEmailRequest = Amazon.SimpleEmail.Model.SendEmailRequest;

namespace Tjb.Web.Services
{
    public class AwsSesEmailService : IEmailService
    {
        private readonly IAmazonSimpleEmailService _sesClient;
        private readonly AwsSesOptions _options;
        private readonly ILogger<AwsSesEmailService> _logger;

        public AwsSesEmailService(
            IAmazonSimpleEmailService sesClient,
            IOptions<AwsSesOptions> options,
            ILogger<AwsSesEmailService> logger)
        {
            _sesClient = sesClient;
            _options = options.Value;
            _logger = logger;
        }

        public async Task SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(_options.SenderEmail))
            {
                throw new InvalidOperationException(
                    "AwsSes:SenderEmail is not configured. Set the AwsSes__SenderEmail environment variable or appsettings value.");
            }

            var sesRequest = new SesSendEmailRequest
            {
                Source = _options.SenderEmail,
                Destination = new Destination
                {
                    ToAddresses = new List<string> { request.To }
                },
                Message = new Message
                {
                    Subject = new Content(request.Subject),
                    Body = new Body
                    {
                        Html = new Content(request.HtmlBody),
                        Text = new Content(request.TextBody),
                    }
                }
            };

            try
            {
                var response = await _sesClient.SendEmailAsync(sesRequest, cancellationToken);
                _logger.LogInformation(
                    "SES sent email to {To} from {Sender} (MessageId={MessageId}).",
                    request.To, _options.SenderEmail, response.MessageId);
            }
            catch (MessageRejectedException ex)
            {
                _logger.LogError(ex, "SES rejected email to {To} from {Sender}: {Message}",
                    request.To, _options.SenderEmail, ex.Message);
                throw;
            }
            catch (AmazonSimpleEmailServiceException ex)
            {
                _logger.LogError(ex, "SES failure sending email to {To} (ErrorCode={ErrorCode}, StatusCode={StatusCode}).",
                    request.To, ex.ErrorCode, ex.StatusCode);
                throw;
            }
        }
    }
}
