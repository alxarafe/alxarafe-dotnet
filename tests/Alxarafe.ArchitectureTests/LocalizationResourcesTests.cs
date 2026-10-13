using System.Globalization;
using System.Reflection;
using System.Resources;
using Alxarafe.AspNetCore;
using Alxarafe.Security.AspNetCore;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class LocalizationResourcesTests
{
    [Fact]
    public void SecurityOwnsTranslationsForEveryNativeIdentityErrorDescriberMethod()
    {
        var methods = typeof(IdentityErrorDescriber).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly);
        var resources = new ResourceManager("Alxarafe.Security.AspNetCore.Resources.SecurityMessages", typeof(SecurityMessages).Assembly);
        foreach (var method in methods)
        {
            var implementation = typeof(LocalizedIdentityErrorDescriber).GetMethod(method.Name,
                method.GetParameters().Select(parameter => parameter.ParameterType).ToArray());
            Assert.Equal(typeof(LocalizedIdentityErrorDescriber), implementation!.DeclaringType);
            var key = SecurityMessages.IdentityPrefix + method.Name;
            Assert.False(string.IsNullOrEmpty(resources.GetString(key, CultureInfo.GetCultureInfo("en"))));
            Assert.False(string.IsNullOrEmpty(resources.GetString(key, CultureInfo.GetCultureInfo("es"))));
        }
        Assert.NotEqual(typeof(Program).Assembly, typeof(SecurityMessages).Assembly);
        Assert.NotEqual(typeof(Program).Assembly, typeof(CoreMessages).Assembly);
    }
}
