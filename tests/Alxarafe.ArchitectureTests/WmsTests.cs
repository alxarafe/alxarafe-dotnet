using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class WmsTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("EA")]
    [InlineData("BOX")]
    [InlineData("A1")]
    [InlineData("BOX10")]
    [InlineData("ABCDEFGHIJ")]
    public void ExactAsciiCodesAreAccepted(string code) => Assert.Equal(code, Uom.Create(Guid.NewGuid(), code, "Each")!.Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1A")]
    [InlineData("box")]
    [InlineData("Box")]
    [InlineData("A-B")]
    [InlineData("A_B")]
    [InlineData("ABCDEFGHIJK")]
    [InlineData(" EA ")]
    [InlineData("KG\n")]
    [InlineData("ÉA")]
    public void InvalidCodesAreNotNormalized(string? code) => Assert.Null(Uom.Create(Guid.NewGuid(), code, "Each"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("x\0y")]
    public void EmptyAndNulDescriptionsAreInvalid(string? description) => Assert.Null(Uom.Create(Guid.NewGuid(), "EA", description));

    [Fact]
    public void DescriptionTrimsOnlyContractEdgesAndPreservesAllInternalContent()
    {
        Assert.Equal("Standard   Box", Uom.Create(Guid.NewGuid(), "EA", " \t\r\nStandard   Box\r\n\t ")!.Description);
        const string preserved = "\u00a0\u2003x\vy\f\u00a0";
        Assert.Equal(preserved, Uom.Create(Guid.NewGuid(), "EA", preserved)!.Description);
        Assert.Equal("X\r\n\tY", Uom.Create(Guid.NewGuid(), "EA", "X\r\n\tY")!.Description);
    }

    [Fact]
    public void ScalarBoundariesHandleSupplementaryCharactersAndRejectUnpairedSurrogates()
    {
        string maximum = string.Concat(Enumerable.Repeat("📦", Uom.DescriptionMaximum));
        Assert.Equal(maximum, Uom.Create(Guid.NewGuid(), "EA", new string(' ', 1000) + maximum + "\t")!.Description);
        Assert.Null(Uom.Create(Guid.NewGuid(), "EA", maximum + "X"));
        foreach (string broken in new[] { new string((char)0xd800, 1), new string((char)0xdc00, 1), "x" + (char)0xd800 + "y" })
            Assert.Null(Uom.Create(Guid.NewGuid(), "EA", broken));
        Assert.Null(Uom.Create(Guid.Empty, "EA", "Each"));
    }

    [Fact]
    public async Task ApplicationValidatesBeforeStoreAndGeneratesDistinctServerIdentities()
    {
        var store = new Store();
        var catalogue = new UomCatalogue(store);
        Assert.Equal(CreateUomOutcome.InvalidRequest, (await catalogue.CreateAsync("ea", "Each")).Outcome);
        Assert.Empty(store.Inserted);
        var created = await catalogue.CreateAsync("EA", " Each ");
        Assert.Equal(CreateUomOutcome.Created, created.Outcome);
        Assert.Equal("Each", Assert.Single(store.Inserted).Description);
        Assert.NotEqual(Guid.Empty, created.Uom!.Id);
        var second = await catalogue.CreateAsync("BOX", "Box");
        Assert.NotEqual(created.Uom.Id, second.Uom!.Id);
        store.Available = false;
        Assert.Equal(CreateUomOutcome.CodeConflict, (await catalogue.CreateAsync("EA", "Changed")).Outcome);
        await catalogue.ListAsync(BigInteger.Pow(10, 30), 1);
        Assert.Equal(BigInteger.Pow(10, 30), store.Offset);
    }

    [Fact]
    public async Task UnexpectedStoreFailureIsNotReportedAsConflictOrInvalidInput()
    {
        var catalogue = new UomCatalogue(new Store { Failure = new InvalidOperationException("fixture storage failure") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalogue.CreateAsync("EA", "Each"));
    }

    [Fact]
    public void WmsProjectReferencesKeepEachLayerAndFunctionalModuleIndependent()
    {
        const string prefix = "Alxarafe.Modules.Wms.";
        var expected = new Dictionary<string, string[]>
        {
            ["Domain"] = [],
            ["Application"] = ["Domain"],
            ["Infrastructure"] = ["Application"],
            ["Http"] = ["Application"],
            ["Module"] = ["Http", "Infrastructure"]
        };
        foreach (var (layer, dependencies) in expected)
        {
            var project = System.Xml.Linq.XDocument.Load(System.IO.Path.Combine(HostBoundaryTests.RepositoryRoot,
                "src/Modules/Wms", prefix + layer, prefix + layer + ".csproj"));
            var moduleReferences = project.Descendants("ProjectReference")
                .Select(reference => System.IO.Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
                .Where(name => name.StartsWith("Alxarafe.Modules.", StringComparison.Ordinal)).Order(StringComparer.Ordinal);
            Assert.Equal(dependencies.Select(name => prefix + name).Order(StringComparer.Ordinal), moduleReferences);
        }
    }

    private sealed class Store : IUomStore
    {
        public List<Uom> Inserted { get; } = [];
        public bool Available { get; set; } = true;
        public Exception? Failure { get; init; }
        public BigInteger Offset { get; private set; }
        public Task<bool> InsertIfCodeAvailableAsync(Uom uom, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            Inserted.Add(uom);
            return Task.FromResult(Available);
        }
        public Task<UomWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default)
        {
            Offset = offset;
            return Task.FromResult(new UomWindow([], Inserted.Count));
        }
    }
}
