namespace CodeIndex.Models;

/// <summary>
/// Public taxonomy for persisted symbol and reference kind values.
/// 永続化される symbol/reference kind 値の公開 taxonomy。
/// </summary>
public static class SymbolKindCatalog
{
    public static readonly string[] SymbolKinds =
    [
        "async_function",
        "async_generator",
        "attribute",
        "associatedtype",
        "class",
        "code",
        "constant",
        "delegate",
        "enum",
        "event",
        "field",
        "file_module",
        "function",
        "generator",
        "heading",
        "hook",
        "implements",
        "import",
        "interface",
        "lambda",
        "layout",
        "method",
        "module",
        "namespace",
        "operator",
        "package",
        "property",
        "protocol",
        "reference",
        "route",
        "service",
        "specialization",
        "struct",
        "test.method",
        "trait",
        "type",
        "typealias",
        "union",
        "variable",
    ];

    public static readonly string[] ReferenceKinds =
    [
        "annotation",
        "attribute",
        "augmentation",
        "call",
        "capture",
        "consumes_hook",
        "const_assertion",
        "copy_from",
        "extends",
        "from",
        "friend",
        "implement",
        "implicit_implementation",
        "import",
        "instantiate",
        "metadata",
        "reference",
        "stage",
        "razor_event_binding",
        "subscribe",
        "type_reference",
        "unsubscribe",
        "use",
    ];

    public static bool IsValidSymbolKind(string? kind)
        => Contains(SymbolKinds, kind);

    public static bool IsValidReferenceKind(string? kind)
        => Contains(ReferenceKinds, kind);

    public static string ToSqlCheckInList(IEnumerable<string> values)
        => string.Join(", ", values.Select(value => $"'{value.Replace("'", "''", StringComparison.Ordinal)}'"));

    private static bool Contains(IEnumerable<string> values, string? value)
        => !string.IsNullOrWhiteSpace(value)
        && values.Contains(value, StringComparer.Ordinal);
}
