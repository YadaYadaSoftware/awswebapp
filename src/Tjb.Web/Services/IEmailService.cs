namespace Tjb.Web.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(SendEmailRequest request, CancellationToken cancellationToken = default);
    }
}
