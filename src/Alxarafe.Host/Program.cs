using System.Globalization;
using Alxarafe.AspNetCore;
using Alxarafe.Host;
using Alxarafe.Modularity;
using Alxarafe.Modules.Catalog.ModuleDefinition;
using Alxarafe.Security.EntityFrameworkCore;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddLocalization(options => options.ResourcesPath = "Resources");
builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
    context.ProblemDetails.Extensions["traceId"] = context.HttpContext.TraceIdentifier);
builder.Services.AddExceptionHandler<PlatformExceptionHandler>();
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
builder.Services.AddAlxarafeModules(typeof(CatalogModule).Assembly);
builder.Services.AddAlxarafePermissionAuthorization();
builder.Services.Configure<RequestLocalizationOptions>(options =>
{
    var supportedCultures = new[] { new CultureInfo("es"), new CultureInfo("en") };
    options.DefaultRequestCulture = new RequestCulture("es");
    options.SupportedCultures = supportedCultures;
    options.SupportedUICultures = supportedCultures;
});
builder.Services.AddOpenApi();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRequestLocalization();
app.UseAuthentication();
app.UseAuthorization();
await SecuritySeed.InitializeAsync(app.Services, app.Environment, app.Configuration);
await app.Services.GetRequiredService<ModuleRuntime>().InitializeAsync(app.Services);
app.MapOpenApi();
AuthEndpoints.Map(app);
app.MapHealthChecks("/health");
app.MapAlxarafeModuleEndpoints();
app.Run();

public partial class Program { }
