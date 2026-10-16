using System.Text.Json;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

internal static class ContractErrors
{
    private static readonly string[] Fields = ["code", "errors"];

    public static void Single(JsonElement body, string code)
    {
        Assert.Equal(Fields, body.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Equal(code, body.GetProperty("code").GetString());
        var detail = Assert.Single(body.GetProperty("errors").EnumerateArray());
        Assert.Equal("code", Assert.Single(detail.EnumerateObject()).Name);
        Assert.Equal(code, detail.GetProperty("code").GetString());
    }
}
