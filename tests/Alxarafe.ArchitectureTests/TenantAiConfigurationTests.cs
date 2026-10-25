using Alxarafe.Core;
using Alxarafe.EntityFrameworkCore;
using Alxarafe.Modules.Ai.Application;
using Alxarafe.Modules.Ai.Infrastructure;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class TenantAiConfigurationTests
{
    private static readonly string[] SettingsFields = ["ModelOverride", "ProviderOverride", "TenantId"];

    [Fact]
    public void SettingsBelongToPlatformAndCannotCarryCredentialsOrBusinessAuthority()
    {
        Assert.Equal(typeof(Alxarafe.Core.AssemblyMarker).Assembly, typeof(TenantAiSettings).Assembly);
        Assert.Equal(typeof(PlatformDbContext).Assembly, typeof(TenantAiSettingsRow).Assembly);
        Assert.Equal(SettingsFields, typeof(TenantAiSettings).GetProperties().Select(property => property.Name).Order());
        Assert.Equal(SettingsFields, typeof(TenantAiSettingsRow).GetProperties().Select(property => property.Name).Order());
        Assert.Null(new UnavailableTenantContext().TenantId);
        Assert.DoesNotContain(typeof(AiAssistant).GetConstructors().SelectMany(constructor => constructor.GetParameters()),
            parameter => parameter.ParameterType == typeof(EffectiveAiConfigurationResolver)
                || parameter.ParameterType == typeof(PlatformDbContext)
                || parameter.ParameterType == typeof(ITenantAiSettingsStore));
    }

    [Fact]
    public void ProviderClientsHaveNoTenantContextOrSettingsDependencies()
    {
        foreach (var type in new[] { typeof(GroqAiModelClient), typeof(OllamaAiModelClient), typeof(OpenAiCompatibleModelClient) })
            Assert.DoesNotContain(type.GetConstructors().SelectMany(constructor => constructor.GetParameters()),
                parameter => parameter.ParameterType == typeof(ICurrentTenantContext)
                    || parameter.ParameterType == typeof(ITenantAiSettingsStore));
    }
}
