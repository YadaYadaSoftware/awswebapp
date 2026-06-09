using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.HttpOverrides;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// ALB-aware forwarded-headers wiring for the Tjb web framework.
/// </summary>
public static class ForwardedHeadersApplicationBuilderExtensions
{
    /// <summary>
    /// Applies <c>X-Forwarded-For</c>/<c>X-Forwarded-Proto</c> processing with the
    /// <see cref="ForwardedHeadersOptions.KnownProxies"/> and <see cref="ForwardedHeadersOptions.KnownNetworks"/>
    /// collections cleared, so the OAuth <c>/signin-google</c> callback sees HTTPS behind an ALB with dynamic IPs.
    /// </summary>
    public static IApplicationBuilder UseAwsWebAppForwardedHeaders(this IApplicationBuilder app)
    {
        var forwardedHeadersOptions = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
        };
        // Clear known proxies/networks to allow ALB (which has dynamic IPs)
        forwardedHeadersOptions.KnownProxies.Clear();
        forwardedHeadersOptions.KnownNetworks.Clear();
        app.UseForwardedHeaders(forwardedHeadersOptions);
        return app;
    }
}
