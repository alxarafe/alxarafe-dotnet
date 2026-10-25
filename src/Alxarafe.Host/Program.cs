using System.Globalization;
using Alxarafe.AspNetCore;
using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Host;
using Alxarafe.Modularity;
using Alxarafe.Modules.Ai;
using Alxarafe.Modules.Wms;
using Alxarafe.Resources;
using Alxarafe.Resources.Runtime;
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
    RequireValidationDatabase(PlatformStorage.ConnectionName, PlatformStorage.ValidationDatabase);
}

void RequireValidationDatabase(string connectionName, string expectedDatabase)
{
    var connectionString = builder.Configuration.GetConnectionString(connectionName);
    if (string.IsNullOrWhiteSpace(connectionString) ||
        !string.Equals(new NpgsqlConnectionStringBuilder(connectionString).Database, expectedDatabase, StringComparison.Ordinal))
        throw new InvalidOperationException($"Testing requires the {connectionName} connection to use {expectedDatabase}.");
}

builder.Services.AddDbContextFactory<PlatformDbContext>(options =>
    PlatformStorage.Configure(options, builder.Configuration.GetConnectionString(PlatformStorage.ConnectionName)
        ?? throw new InvalidOperationException("The Platform connection string must be configured.")));
builder.Services.AddSingleton<IModuleStateStore, EfModuleStateStore>();
builder.Services.AddSingleton<ITenantAiSettingsStore, EfTenantAiSettingsStore>();
builder.Services.AddSingleton<ITenantDirectory, EfTenantDirectory>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IRequestTenantContext, TenantContext>();
builder.Services.AddScoped<ICurrentTenantContext>(provider => provider.GetRequiredService<IRequestTenantContext>());
builder.Services.AddSingleton<TenantDatabaseResolver>();
builder.Services.AddSingleton<ITenantDatabaseResolver>(provider => provider.GetRequiredService<TenantDatabaseResolver>());
builder.Services.AddSingleton<ITenantDatabaseMigrationRunner>(provider => provider.GetRequiredService<TenantDatabaseResolver>());
if ((builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    && builder.Configuration.GetValue<bool>(DevelopmentTenantBootstrap.EnabledConfiguration))
{
    builder.Services.AddSingleton<DevelopmentTenantBootstrap>();
    builder.Services.AddSingleton<IDevelopmentTenantMembership>(provider => provider.GetRequiredService<DevelopmentTenantBootstrap>());
}
builder.Services.AddSingleton<ModuleStateService>();
builder.Services.AddScoped<ICurrentSecurityContext, CurrentSecurityContext>();
builder.Services.AddScoped<IReadPrimitiveService, ReadPrimitiveService>();
builder.Services.AddScoped<ICapabilityInvocationService, CapabilityInvocationService>();

builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier;
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
    // Module codes and native validation responses already carry owner-localized titles.
    if (!context.ProblemDetails.Extensions.ContainsKey(CoreMessages.CodeProperty)
        && context.ProblemDetails is not HttpValidationProblemDetails)
    {
        var localizer = context.HttpContext.RequestServices
            .GetRequiredService<Microsoft.Extensions.Localization.IStringLocalizer<CoreMessages>>();
        var title = localizer[$"core.http.{status}"];
        context.ProblemDetails.Title = title.ResourceNotFound ? localizer[CoreMessages.RequestTitle].Value : title.Value;
    }
});
builder.Services.AddDbContext<SecurityDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Security")));
builder.Services.AddIdentityCore<AlxarafeUser>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 8;
    options.Password.RequireDigit = true;
    options.Password.RequireNonAlphanumeric = false;
}).AddEntityFrameworkStores<SecurityDbContext>().AddSignInManager().AddApiEndpoints()
    .AddErrorDescriber<LocalizedIdentityErrorDescriber>();
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
}).AddBearerToken(IdentityConstants.BearerScheme);
// Production WMS/AI and explicit conformance sources share one registry/runtime.
var modules = new ModuleBuilder(builder.Services).AddModulesFromAssemblies(typeof(WmsModule).Assembly, typeof(AiModule).Assembly);
if (builder.Environment.IsEnvironment(ConformanceModuleSource.EnvironmentName)
    && builder.Configuration.GetValue<bool>(ConformanceModuleSource.EnabledConfiguration))
    modules.AddSource(new ConformanceModuleSource());
modules.Build();
builder.Services.AddSingleton<ResourceRegistry>();
builder.Services.AddSingleton<ResourceRepositoryCatalog>();
builder.Services.AddScoped<ResourceRepositoryRegistry>();
builder.Services.AddScoped<IResourceAuthorization, CurrentResourceAuthorization>();
builder.Services.AddScoped<ResourceRuntime>();
// Module handlers run first; this handler only handles transversal validation.
builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
builder.Services.AddAlxarafePermissionAuthorization();
builder.Services.AddScoped<UserAdministration>();
builder.Services.AddSingleton<Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler, SharedContractAuthorizationResultHandler>();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var baseCulture = CultureInfo.GetCultureInfo("en");
    var supportedCultures = new[] { baseCulture, CultureInfo.GetCultureInfo("es") };
    options.DefaultRequestCulture = new RequestCulture(baseCulture);
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
    options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
});
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer<BearerSecurityTransformer>();
    options.AddOperationTransformer<BearerSecurityTransformer>();
    options.AddSchemaTransformer((schema, context, _) =>
    {
        if (context.JsonTypeInfo.Type == typeof(System.Numerics.BigInteger))
        {
            schema.Type = Microsoft.OpenApi.JsonSchemaType.Integer;
            schema.Minimum = "0";
            schema.Properties = null;
            schema.Format = null;
        }
        if (context.JsonTypeInfo.Type == typeof(UpdateUserRequest))
        {
            schema.MinProperties = 1;
            schema.Required = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in schema.Properties!.Values.OfType<Microsoft.OpenApi.OpenApiSchema>())
                property.Type = Microsoft.OpenApi.JsonSchemaType.Boolean;
        }
        return Task.CompletedTask;
    });
});
builder.Services.AddHealthChecks();

var app = builder.Build();
// Validate/freeze typed resource contributions before persistence initialization or HTTP.
_ = app.Services.GetRequiredService<ResourceRegistry>();
_ = app.Services.GetRequiredService<ResourceRepositoryCatalog>();
using (var resourceScope = app.Services.CreateScope())
    _ = resourceScope.ServiceProvider.GetRequiredService<ResourceRepositoryRegistry>();
// Keep the request culture active while exception handlers build their response.
app.UseRequestLocalization();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseMiddleware<CurrentUserMiddleware>();
app.UseMiddleware<TenantContextMiddleware>();
app.UseMiddleware<CapabilityInvocationAuditMiddleware>();
AiModule.UseInvocationAudit(app);
app.UseAuthorization();
await using (var context = await app.Services.GetRequiredService<IDbContextFactory<PlatformDbContext>>().CreateDbContextAsync())
    await context.Database.MigrateAsync();
if (app.Services.GetService<DevelopmentTenantBootstrap>() is { } tenantBootstrap)
    await tenantBootstrap.InitializeAsync();
await SecuritySeed.InitializeAsync(app.Services, app.Environment);
// Existing installations also need the small storage upgrade outside seed environments.
await using (var scope = app.Services.CreateAsyncScope())
    await SecuritySchema.UpgradeEmailStorageAsync(scope.ServiceProvider.GetRequiredService<SecurityDbContext>());
await app.Services.GetRequiredService<ModuleStateService>().SynchronizeAsync();
await app.Services.GetRequiredService<ModuleRuntime>().InitializeAsync(app.Services);
app.MapOpenApi();
AuthEndpoints.Map(app);
UserEndpoints.Map(app);
ModuleEndpoints.Map(app);
CapabilityEndpoints.Map(app);
app.MapHealthChecks("/health", new HealthCheckOptions
{
    // Contractual HTTP liveness must not run database or module checks.
    Predicate = _ => false,
    ResponseWriter = (context, _) => context.Response.WriteAsJsonAsync(new { status = "ok" })
}).AllowAnonymous();
app.MapAlxarafeModuleEndpoints();
app.Run();

public partial class Program { }
