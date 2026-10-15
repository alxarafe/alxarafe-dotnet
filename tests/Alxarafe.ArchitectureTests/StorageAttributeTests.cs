using System.Numerics;
using Alxarafe.Modules.Wms.Application;
using Alxarafe.Modules.Wms.Domain;
using Xunit;

namespace Alxarafe.ArchitectureTests;

public sealed class StorageAttributeTests
{
    [Theory]
    [InlineData("A")]
    [InlineData("EA")]
    [InlineData("BOX")]
    [InlineData("A1")]
    [InlineData("COLD_CHAIN")]
    [InlineData("IS_FOOD")]
    [InlineData("ZONE2")]
    [InlineData("A_1")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZABCD")]
    public void ExactAsciiCodesAreAccepted(string code) => Assert.Equal(code, StorageAttribute.Create(Guid.NewGuid(), code, "Each")!.Code);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("1A")]
    [InlineData("box")]
    [InlineData("Box")]
    [InlineData("A-B")]
    [InlineData("_COLD")]
    [InlineData("COLD SPACE")]
    [InlineData("COLD-")]
    [InlineData("ABCDEFGHIJKLMNOPQRSTUVWXYZABCDE")]
    [InlineData(" EA ")]
    [InlineData("KG\n")]
    [InlineData("ÉA")]
    public void InvalidCodesAreNotNormalized(string? code) => Assert.Null(StorageAttribute.Create(Guid.NewGuid(), code, "Each"));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("\t\r\n")]
    [InlineData("x\0y")]
    public void EmptyAndNulNamesAreInvalid(string? name) => Assert.Null(StorageAttribute.Create(Guid.NewGuid(), "EA", name));

    [Fact]
    public void NameTrimsOnlyContractEdgesAndPreservesAllInternalContent()
    {
        Assert.Equal("Standard   Box", StorageAttribute.Create(Guid.NewGuid(), "EA", " \t\r\nStandard   Box\r\n\t ")!.Name);
        const string preserved = "\u00a0\u2003x\vy\f\u00a0";
        Assert.Equal(preserved, StorageAttribute.Create(Guid.NewGuid(), "EA", preserved)!.Name);
        Assert.Equal("X\r\n\tY", StorageAttribute.Create(Guid.NewGuid(), "EA", "X\r\n\tY")!.Name);
    }

    [Fact]
    public void ScalarBoundariesHandleSupplementaryCharactersAndRejectUnpairedSurrogates()
    {
        string maximum = string.Concat(Enumerable.Repeat("📦", StorageAttribute.NameMaximum));
        Assert.Equal(maximum, StorageAttribute.Create(Guid.NewGuid(), "EA", new string(' ', 1000) + maximum + "\t")!.Name);
        Assert.Null(StorageAttribute.Create(Guid.NewGuid(), "EA", maximum + "X"));
        foreach (string broken in new[] { new string((char)0xd800, 1), new string((char)0xdc00, 1), "x" + (char)0xd800 + "y" })
            Assert.Null(StorageAttribute.Create(Guid.NewGuid(), "EA", broken));
        Assert.Equal("📦", StorageAttribute.Create(Guid.NewGuid(), "A", "📦")!.Name);
        Assert.Null(StorageAttribute.Create(Guid.Empty, "EA", "Each"));
    }

    [Fact]
    public async Task ApplicationValidatesBeforeStoreAndGeneratesDistinctServerIdentities()
    {
        var store = new Store();
        var catalogue = new StorageAttributeCatalogue(store);
        Assert.Equal(CreateStorageAttributeOutcome.InvalidRequest, (await catalogue.CreateAsync("ea", "Each")).Outcome);
        Assert.Empty(store.Inserted);
        var created = await catalogue.CreateAsync("EA", " Each ");
        Assert.Equal(CreateStorageAttributeOutcome.Created, created.Outcome);
        Assert.Equal("Each", Assert.Single(store.Inserted).Name);
        Assert.NotEqual(Guid.Empty, created.StorageAttribute!.Id);
        var second = await catalogue.CreateAsync("BOX", "Box");
        Assert.NotEqual(created.StorageAttribute.Id, second.StorageAttribute!.Id);
        var original = store.Inserted.ToArray();
        store.Available = false;
        Assert.Equal(CreateStorageAttributeOutcome.CodeConflict, (await catalogue.CreateAsync("EA", "Changed")).Outcome);
        Assert.Equal(original, store.Inserted);
        await catalogue.ListAsync(BigInteger.Pow(10, 30), 1);
        Assert.Equal(BigInteger.Pow(10, 30), store.Offset);
    }

    [Fact]
    public async Task UnexpectedStoreFailureIsNotReportedAsConflictOrInvalidInput()
    {
        var catalogue = new StorageAttributeCatalogue(new Store { Failure = new InvalidOperationException("fixture storage failure") });
        await Assert.ThrowsAsync<InvalidOperationException>(() => catalogue.CreateAsync("EA", "Each"));
    }

    private sealed class Store : IStorageAttributeStore
    {
        public List<StorageAttribute> Inserted { get; } = [];
        public bool Available { get; set; } = true;
        public Exception? Failure { get; init; }
        public BigInteger Offset { get; private set; }
        public Task<bool> InsertIfCodeAvailableAsync(StorageAttribute attribute, CancellationToken cancellationToken = default)
        {
            if (Failure is not null) throw Failure;
            if (Available) Inserted.Add(attribute);
            return Task.FromResult(Available);
        }
        public Task<StorageAttributeWindow> ListAsync(BigInteger offset, int limit, CancellationToken cancellationToken = default)
        {
            Offset = offset;
            return Task.FromResult(new StorageAttributeWindow([], Inserted.Count));
        }
    }
}
