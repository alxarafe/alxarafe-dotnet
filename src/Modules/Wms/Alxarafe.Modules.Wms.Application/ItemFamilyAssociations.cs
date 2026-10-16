using System.Numerics;
using Alxarafe.Modules.Wms.Domain;

namespace Alxarafe.Modules.Wms.Application;

public enum AssociationOperation { List, Create, Replace, Change, Delete }
public enum AssociationIssue { FamilyMissing, AttributeMissing, AssociationMissing, AlreadyAssociated, NotAssociated }
public sealed record AttributeReference(Guid Id, string? Path = null);
public sealed record AssociationFailure(AssociationIssue Issue, Guid Id, string? Path = null);
public sealed record AssociationCommand(AssociationOperation Operation, IReadOnlyList<AttributeReference> Add,
    IReadOnlyList<AttributeReference> Remove, BigInteger Offset = default, int Limit = 1);
public sealed record AssociationResult(IReadOnlyList<AssociationFailure> Failures,
    IReadOnlyList<StorageAttribute> Items, long Total = 0);

public interface IItemFamilyAssociationStore
{
    Task<AssociationResult> ExecuteAsync(Guid familyId, AssociationCommand command, CancellationToken cancellationToken = default);
}

public sealed class ItemFamilyAssociations(IItemFamilyAssociationStore store)
{
    public Task<AssociationResult> ExecuteAsync(Guid familyId, AssociationCommand command, CancellationToken cancellationToken = default) =>
        store.ExecuteAsync(familyId, command, cancellationToken);

    // Evaluate the entire existence stage before examining any relation state.
    public static IReadOnlyList<AssociationFailure> Validate(Guid familyId, bool familyExists,
        AssociationCommand command, IReadOnlySet<Guid> existingAttributes, IReadOnlySet<Guid> associated)
    {
        var failures = new List<AssociationFailure>();
        if (!familyExists) failures.Add(new(AssociationIssue.FamilyMissing, familyId));
        foreach (var reference in command.Add.Concat(command.Remove))
            if (!existingAttributes.Contains(reference.Id)) failures.Add(new(AssociationIssue.AttributeMissing, reference.Id, reference.Path));
        if (failures.Count != 0) return failures;
        if (command.Operation is AssociationOperation.Create or AssociationOperation.Change)
            foreach (var reference in command.Add)
                if (associated.Contains(reference.Id)) failures.Add(new(AssociationIssue.AlreadyAssociated, reference.Id, reference.Path));
        if (command.Operation is AssociationOperation.Delete or AssociationOperation.Change)
            foreach (var reference in command.Remove)
                if (!associated.Contains(reference.Id)) failures.Add(new(command.Operation == AssociationOperation.Delete
                    ? AssociationIssue.AssociationMissing : AssociationIssue.NotAssociated, reference.Id, reference.Path));
        return failures;
    }
}
