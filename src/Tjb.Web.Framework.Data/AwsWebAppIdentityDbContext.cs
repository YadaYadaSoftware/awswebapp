using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tjb.Web.Framework.Data;

/// <summary>
/// Identity-only EF Core base context for the Tjb web framework. Carries the ASP.NET Identity
/// model configuration (<see cref="IdentityDbContext{TUser}"/> over <see cref="IdentityUser"/>) and
/// <strong>no</strong> application entities, so a consumer derives this and adds its own
/// <c>DbSet</c>s while reusing the Identity schema.
/// </summary>
/// <remarks>
/// A derived context MUST call <c>base.OnModelCreating(builder)</c> before its own configuration so
/// the Identity model is configured first — matching the historical
/// <c>TjbDbContext : IdentityDbContext&lt;IdentityUser&gt;</c> ordering (the migration-equivalence guarantee).
/// </remarks>
public abstract class AwsWebAppIdentityDbContext : IdentityDbContext<IdentityUser>
{
    protected AwsWebAppIdentityDbContext(DbContextOptions options)
        : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        // Identity-side model configuration. There is no Tjb-specific Identity customization today;
        // the default IdentityDbContext<IdentityUser> mapping is the shared contract.
        base.OnModelCreating(builder);
    }
}
