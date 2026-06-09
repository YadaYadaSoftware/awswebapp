// Minimal API for the sample, mirroring Tjb.Api's vestigial role: a /health endpoint and
// Swagger in development, for project-set shape parity with the framework's expected topology.
// Intentionally inert beyond health — no auth surface.
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapHealthChecks("/health");
app.MapGet("/", () => "Sample.Api — see /health and /swagger (dev).");

app.Run();
