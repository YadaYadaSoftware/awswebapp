using Microsoft.EntityFrameworkCore;
using Sample.Data;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

// Register the sample's consumer DbContext (Identity + Guestbook data).
builder.Services.AddDbContext<SampleDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString),
        mysqlOptions => mysqlOptions.EnableRetryOnFailure(3)));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// Web framework wiring — all from the Tjb.Web.Hosting package (the thin-host shape).
builder.Services.AddAwsWebAppIdentity<SampleDbContext>();
builder.Services.AddAwsWebAppGoogleAuth<SampleDbContext>(builder.Configuration);
builder.Services.AddAwsWebAppEmail(builder.Configuration);

// Host-owned registrations.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddHealthChecks();

var app = builder.Build();

app.LogAwsWebAppAuthConfig();

await app.ApplyDatabaseMigrationsAsync<SampleDbContext>();

app.UseAwsWebAppForwardedHeaders();
app.UseAuthentication();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapRazorPages();          // Identity UI pages (from the framework RCL)
app.MapHealthChecks("/health");
app.MapBlazorHub();
app.MapFallbackToPage("/_Host");

app.Run();
