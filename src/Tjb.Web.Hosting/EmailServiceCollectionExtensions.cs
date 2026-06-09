using Amazon;
using Amazon.SimpleEmail;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Tjb.Web.Services;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// AWS SES email wiring for the Tjb web framework.
/// </summary>
public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AwsSesOptions"/> (section <c>AwsSes</c>), an <see cref="IAmazonSimpleEmailService"/>
    /// client (region empty ⇒ SDK auto-detects from the environment), the <see cref="IEmailService"/> and
    /// <see cref="IViewRenderService"/> implementations, and the <see cref="IHttpContextAccessor"/> the
    /// view renderer needs.
    /// </summary>
    public static IServiceCollection AddAwsWebAppEmail(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // AWS SES email service. Region is empty in deployed envs so the SDK auto-detects from
        // Fargate task metadata; populate AwsSes:Region explicitly only for local dev.
        services.Configure<AwsSesOptions>(configuration.GetSection(AwsSesOptions.SectionName));
        services.AddSingleton<IAmazonSimpleEmailService>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AwsSesOptions>>().Value;
            return string.IsNullOrEmpty(options.Region)
                ? new AmazonSimpleEmailServiceClient()
                : new AmazonSimpleEmailServiceClient(RegionEndpoint.GetBySystemName(options.Region));
        });
        services.AddScoped<IEmailService, AwsSesEmailService>();

        services.AddHttpContextAccessor();
        services.AddScoped<IViewRenderService, ViewRenderService>();

        return services;
    }
}
