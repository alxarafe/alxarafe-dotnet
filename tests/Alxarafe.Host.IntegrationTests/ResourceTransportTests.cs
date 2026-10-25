using System.Text.Json;
using Alxarafe.Resources;
using Alxarafe.Resources.AspNetCore;
using Xunit;

namespace Alxarafe.Host.IntegrationTests;

public sealed class ResourceTransportTests
{
    [Theory]
    [InlineData(ScalarType.Boolean, "true")]
    [InlineData(ScalarType.String, "\"text\"")]
    [InlineData(ScalarType.Text, "\"long text\"")]
    [InlineData(ScalarType.Int8, "-128")]
    [InlineData(ScalarType.UInt8, "255")]
    [InlineData(ScalarType.Int16, "-32768")]
    [InlineData(ScalarType.UInt16, "65535")]
    [InlineData(ScalarType.Int32, "-2147483648")]
    [InlineData(ScalarType.UInt32, "4294967295")]
    [InlineData(ScalarType.Int64, "9223372036854775807")]
    [InlineData(ScalarType.Decimal, "123456789012345678901234567890.1234567890123456789")]
    [InlineData(ScalarType.Uuid, "\"035a2161-c936-42dc-8b4b-d3cf5d1953a6\"")]
    [InlineData(ScalarType.Date, "\"2026-10-25\"")]
    [InlineData(ScalarType.Time, "\"12:34:56.1234567\"")]
    [InlineData(ScalarType.DateTime, "\"2026-10-25T08:00:00.0000000Z\"")]
    [InlineData(ScalarType.Enum, "\"open\"")]
    [InlineData(ScalarType.Binary, "\"AA==\"")]
    [InlineData(ScalarType.String, "null")]
    public void EverySemanticCategoryHasAnExplicitStableJsonProjection(ScalarType type, string json)
    {
        using var document = JsonDocument.Parse(json);
        Assert.Equal(json, ResourceTransport.Project(ResourceTransport.Parse(type, document.RootElement)).GetRawText());
    }

    [Theory]
    [InlineData(ScalarType.Boolean, "\"true\"")]
    [InlineData(ScalarType.Int64, "\"123\"")]
    [InlineData(ScalarType.Decimal, "\"1.5\"")]
    [InlineData(ScalarType.String, "123")]
    [InlineData(ScalarType.Uuid, "123")]
    [InlineData(ScalarType.Date, "\"2026-02-30\"")]
    [InlineData(ScalarType.Time, "\"24:00:00\"")]
    [InlineData(ScalarType.DateTime, "\"2026-10-25T08:00:00\"")]
    [InlineData(ScalarType.Binary, "\"invalid!\"")]
    public void TransportNeverCoercesInvalidValues(ScalarType type, string json)
    {
        using var document = JsonDocument.Parse(json);
        var error = Record.Exception(() => ResourceTransport.Parse(type, document.RootElement));
        Assert.NotNull(error);
        Assert.Equal("ResourceTransportException", error.GetType().Name);
    }
}
