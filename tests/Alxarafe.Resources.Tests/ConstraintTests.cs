using System.Numerics;
using Alxarafe.Resources;
using Xunit;

namespace Alxarafe.Resources.Tests;

public sealed class ConstraintTests
{
    [Theory]
    [InlineData("-1", false)]
    [InlineData("0", true)]
    [InlineData("255", true)]
    [InlineData("256", false)]
    public void UInt8BoundaryValuesAreValidatedByTheSemanticDomain(string value, bool valid)
    {
        var field = new FieldDefinition("value", ScalarType.UInt8);
        Fixture.Registry(Fixture.WithField(field));
        Assert.Equal(valid, ResourceValueValidator.IsValid(field, new NumericValue(Fixture.Number(value))));
    }

    [Theory]
    [InlineData(ScalarType.Int8, "-128", "127")]
    [InlineData(ScalarType.UInt8, "0", "255")]
    [InlineData(ScalarType.Int16, "-32768", "32767")]
    [InlineData(ScalarType.UInt16, "0", "65535")]
    [InlineData(ScalarType.Int32, "-2147483648", "2147483647")]
    [InlineData(ScalarType.UInt32, "0", "4294967295")]
    [InlineData(ScalarType.Int64, "-9223372036854775808", "9223372036854775807")]
    public void IntegerDomainsExposeTheirExactSemanticRange(ScalarType type, string minimum, string maximum)
    {
        var intrinsic = NumericDomain.Intrinsic(type)!;
        Assert.Equal(minimum, intrinsic.Minimum!.CanonicalText);
        Assert.Equal(maximum, intrinsic.Maximum!.CanonicalText);
        foreach (string value in new[] { minimum, maximum })
            Assert.Single(Fixture.Registry(Fixture.WithField(new("value", type,
                constraints: new NumericConstraints(Fixture.Number(minimum), Fixture.Number(maximum)),
                defaultValue: new NumericValue(Fixture.Number(value))))).All);
    }

    [Theory]
    [InlineData(ScalarType.UInt8, "0", "10", true)]
    [InlineData(ScalarType.UInt8, null, "300", false)]
    [InlineData(ScalarType.UInt8, "-1", null, false)]
    [InlineData(ScalarType.Int8, "-128", "127", true)]
    [InlineData(ScalarType.Int8, "-129", "127", false)]
    [InlineData(ScalarType.UInt8, "10", "9", false)]
    public void BoundsRestrictButNeverWidenIntrinsicDomains(ScalarType type, string? minimum, string? maximum, bool valid)
    {
        var resource = Fixture.WithField(new("rating", type, constraints: new NumericConstraints(
            minimum is null ? null : Fixture.Number(minimum), maximum is null ? null : Fixture.Number(maximum))));
        if (valid) Assert.Single(Fixture.Registry(resource).All);
        else Assert.Equal(ResourceValidationCode.InvalidConstraint, Assert.Throws<ResourceValidationException>(() => Fixture.Registry(resource)).Code);
    }

    [Theory]
    [InlineData("0", "1", true, true, null)]
    [InlineData("1", "1", true, false, null)]
    [InlineData("1", "2", false, false, "3")]
    [InlineData("0", "10", false, false, "0")]
    [InlineData("0", "10", false, false, "-1")]
    public void ImpossibleDiscreteIntervalsAndInvalidMultiplesFail(string minimum, string maximum, bool exclusiveMinimum, bool exclusiveMaximum, string? multiple)
    {
        var constraints = new NumericConstraints(Fixture.Number(minimum), Fixture.Number(maximum), exclusiveMinimum, exclusiveMaximum,
            multiple is null ? null : Fixture.Number(multiple));
        Assert.Equal(ResourceValidationCode.InvalidConstraint, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.WithField(new("value", ScalarType.UInt8, constraints: constraints)))).Code);
    }

    [Theory]
    [InlineData(5, 4, null)]
    [InlineData(-1, 4, null)]
    [InlineData(0, 4, "[")]
    [InlineData(0, 4, "(?=x)x")]
    public void InvalidTextLengthsAndPatternsFail(int minimum, int maximum, string? pattern) =>
        Assert.Equal(ResourceValidationCode.InvalidConstraint, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.WithField(new("value", ScalarType.String, constraints: new TextConstraints(minimum, maximum, pattern))))).Code);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(2, 3)]
    [InlineData(2, -1)]
    public void InvalidFixedDecimalDeclarationsFail(int precision, int scale) =>
        Assert.Equal(ResourceValidationCode.InvalidConstraint, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.WithField(new("value", ScalarType.Decimal, constraints: new DecimalConstraints(precision, scale))))).Code);

    [Fact]
    public void ArbitraryPrecisionDecimalsDoNotInheritClrDecimalLimitations()
    {
        const string value = "1234567890123456789012345678901234567890.12345678901234567890";
        var number = Fixture.Number(value);
        var field = new FieldDefinition("amount", ScalarType.Decimal, defaultValue: new NumericValue(number), constraints: new DecimalConstraints(60, 20));
        Assert.Single(Fixture.Registry(Fixture.WithField(field)).All);
        Assert.Equal("1234567890123456789012345678901234567890.1234567890123456789", number.CanonicalText);
    }

    [Theory]
    [InlineData(ScalarType.Boolean)]
    [InlineData(ScalarType.Uuid)]
    [InlineData(ScalarType.Binary)]
    [InlineData(ScalarType.String)]
    public void NumericConstraintsCannotBeAppliedToNonNumericTypes(ScalarType type) =>
        Assert.Equal(ResourceValidationCode.InvalidConstraint, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.WithField(new("value", type, constraints: new NumericConstraints())))).Code);

    [Fact]
    public void StringLengthUsesUnicodeScalarsAndDefaultsRespectPatterns()
    {
        Assert.Single(Fixture.Registry(Fixture.WithField(new("name", ScalarType.String, defaultValue: new TextValue("😀"),
            constraints: new TextConstraints(1, 1)))).All);
        Assert.Equal(ResourceValidationCode.InvalidDefault, Assert.Throws<ResourceValidationException>(() =>
            Fixture.Registry(Fixture.WithField(new("code", ScalarType.String, defaultValue: new TextValue("abc"),
                constraints: new TextConstraints(Pattern: "^[A-Z]+$"))))).Code);
    }

    [Fact]
    public void DefaultsMustMatchTheTypeAndItsEffectiveRange()
    {
        FieldDefinition[] invalid =
        [
            new("value", ScalarType.UInt8, defaultValue: new NumericValue(Fixture.Number("11")), constraints: new NumericConstraints(Maximum: Fixture.Number("10"))),
            new("value", ScalarType.UInt32, defaultValue: new NumericValue(Fixture.Number("4294967296"))),
            new("value", ScalarType.Int8, defaultValue: new NumericValue(Fixture.Number("1.5"))),
            new("value", ScalarType.Int16, defaultValue: new NumericValue(Fixture.Number("3")), constraints: new NumericConstraints(MultipleOf: Fixture.Number("2"))),
            new("value", ScalarType.Decimal, defaultValue: new NumericValue(Fixture.Number("1.234")), constraints: new DecimalConstraints(4, 2)),
            new("value", ScalarType.Decimal, defaultValue: new NumericValue(Fixture.Number("100")), constraints: new DecimalConstraints(4, 2)),
            new("value", ScalarType.Boolean, defaultValue: new TextValue("true")),
            new("value", ScalarType.String, defaultValue: new NullValue()),
            new("value", ScalarType.Binary, defaultValue: new BinaryValue("not base64")),
            new("value", ScalarType.Enum, enumValues: ["open"], defaultValue: new TextValue("closed"))
        ];
        foreach (var field in invalid)
            Assert.Equal(ResourceValidationCode.InvalidDefault, Assert.Throws<ResourceValidationException>(() => Fixture.Registry(Fixture.WithField(field))).Code);
    }

    [Fact]
    public void AllScalarDefaultShapesAreValidatedWithoutStorageTypeMappings()
    {
        FieldDefinition[] fields =
        [
            new("boolean", ScalarType.Boolean, defaultValue: new BooleanValue(true)),
            new("text", ScalarType.Text, defaultValue: new TextValue("Long text")),
            new("uuid", ScalarType.Uuid, defaultValue: new UuidValue(Guid.Empty)),
            new("date", ScalarType.Date, defaultValue: new DateValue(new DateOnly(2026, 1, 1))),
            new("time", ScalarType.Time, defaultValue: new TimeValue(new TimeOnly(12, 0))),
            new("instant", ScalarType.DateTime, defaultValue: new DateTimeValue(DateTimeOffset.UnixEpoch)),
            new("enum", ScalarType.Enum, enumValues: ["open", "closed"], defaultValue: new TextValue("open")),
            new("binary", ScalarType.Binary, defaultValue: new BinaryValue("AA==")),
            new("optional", ScalarType.String, nullable: true, defaultValue: new NullValue())
        ];
        Assert.Single(Fixture.Registry(Fixture.Resource(new[] { Fixture.Id }.Concat(fields))).All);
    }

    [Theory]
    [InlineData("001.2300", "1.23")]
    [InlineData("-0.000", "0")]
    [InlineData("-0.0100", "-0.01")]
    public void MetadataNumbersHaveCanonicalExactRepresentations(string input, string expected) => Assert.Equal(expected, Fixture.Number(input).CanonicalText);

    [Theory]
    [InlineData("NaN")]
    [InlineData("1e3")]
    [InlineData("1,2")]
    public void NonDecimalNumberSyntaxIsNotAccepted(string input) => Assert.Throws<FormatException>(() => Fixture.Number(input));
}
