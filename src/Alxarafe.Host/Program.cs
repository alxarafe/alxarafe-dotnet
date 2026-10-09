using System.Globalization;
using Alxarafe.AspNetCore;
using Alxarafe.Host;
using Alxarafe.Modularity;
using Alxarafe.Modules.AiAgent.ModuleDefinition;
using Alxarafe.Modules.Catalog.ModuleDefinition;
using Alxarafe.Security.AspNetCore;
using Alxarafe.Security.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);
if (builder.Environment.IsEnvironment("Testing"))
{
    RequireValidationDatabase("Security", "alxarafe_security_test");
}

void RequireValidationDatabase(string connectionName, string expectedDatabase)
{
    var connectionString = builder.Configuration.GetConnectionString(connectionName);
    if (string.IsNullOrWhiteSpace(connectionString) ||
        !string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, expectedDatabase, StringComparison.Ordinal))
        throw new InvalidOperationException($"Testing requires the {connectionName} connection to use {expectedDatabase}.");
}

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddDbContext<SecurityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Security")));
builder.Services.AddIdentityCore<AlxarafeUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<SecurityDbContext>().AddSignInManager().AddApiEndpoints();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
}).AddBearerToken(IdentityConstants.BearerScheme);
// Temporary static composition: only the module entry assemblies are known here.
builder.Services.AddAlxarafeModules(typeof(CatalogModule).Assembly, typeof(AiAgentModule).Assembly);
// Module handlers run first; this handler only handles transversal validation.
builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
builder.Services.AddAlxarafePermissionAuthorization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("es"), new CultureInfo("en") };
    options.DefaultRequestCulture = new RequestCulture("es");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityTransformer>();
    options.AddOperationTransformer<BearerSecurityTransformer>();
});
builder.Services.AddHealthChecks();

var app = builder.Build();
// Keep the request culture active while exception handlers build their response.
app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
await SecuritySeed.InitializeAsync(app.Services, app.Environment);
await app.Services.GetRequiredService<ModuleRuntime>().InitializeAsync(app.Services);
app.MapOpenApi();
AuthEndpoints.Map(app);
app.MapHealthChecks("/health", new HealthCheckOptions
{
    // Contractual HTTP liveness must not run database or module checks.
    Predicate = _ => false,
    ResponseWriter = (context, _) => context.Response.WriteAsJsonAsync(new { status = "ok" })
}).AllowAnonymous();
app.MapAlxarafeModuleEndpoints();
app.Run();

public partial class Program { }
