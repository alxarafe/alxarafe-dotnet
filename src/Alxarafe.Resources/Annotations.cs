namespace Alxarafe.Resources;

[Flags]
public enum AnnotationTarget { Resource = 1, Field = 2, Action = 4, ActionParameter = 8 }
public enum AnnotationValueKind { Text, Boolean, Number, FieldNames }
public sealed record AnnotationTerm(string Vocabulary, string Name, AnnotationValueKind Kind, AnnotationTarget Targets)
{
    public string Key => Vocabulary + "." + Name;
}
public abstract record AnnotationValue;
public sealed record AnnotationText(string Value) : AnnotationValue;
public sealed record AnnotationBoolean(bool Value) : AnnotationValue;
public sealed record AnnotationNumber(ResourceNumber Value) : AnnotationValue;
public sealed record AnnotationFieldNames : AnnotationValue
{
    public IReadOnlyList<string> Fields { get; }
    public AnnotationFieldNames(IEnumerable<string> fields) => Fields = Snapshot.Of(fields);
}
public sealed record Annotation(string Vocabulary, string Term, AnnotationValue Value)
{
    public string Key => Vocabulary + "." + Term;
}
public sealed record AnnotationVocabulary
{
    public string Name { get; }
    public IReadOnlyList<AnnotationTerm> Terms { get; }
    public AnnotationVocabulary(string name, IEnumerable<AnnotationTerm> terms)
    {
        Name = name; Terms = Snapshot.Of(terms);
    }
}

public static class StandardAnnotations
{
    public static AnnotationTerm Label { get; } = new("Common", "Label", AnnotationValueKind.Text,
        AnnotationTarget.Resource | AnnotationTarget.Field | AnnotationTarget.Action | AnnotationTarget.ActionParameter);
    public static AnnotationTerm Hidden { get; } = new("UI", "Hidden", AnnotationValueKind.Boolean, AnnotationTarget.Field | AnnotationTarget.Action);
    public static AnnotationTerm FormExcluded { get; } = new("UI", "FormExcluded", AnnotationValueKind.Boolean, AnnotationTarget.Field);
    public static AnnotationTerm SelectionFields { get; } = new("UI", "SelectionFields", AnnotationValueKind.FieldNames, AnnotationTarget.Resource);
    public static IReadOnlyList<AnnotationVocabulary> Vocabularies { get; } = Snapshot.Of(new[]
    {
        new AnnotationVocabulary(Label.Vocabulary, [Label]),
        new AnnotationVocabulary(Hidden.Vocabulary, [Hidden, FormExcluded, SelectionFields])
    });
}
