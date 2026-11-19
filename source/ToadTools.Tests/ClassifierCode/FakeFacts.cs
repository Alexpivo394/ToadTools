using ClassifierCode.Engine.Facts;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Факты элемента без Revit: всё задаётся в тесте.</summary>
public sealed class FakeFacts : IElementFacts
{
    private static long _nextId = 1;

    public long Id { get; init; } = Interlocked.Increment(ref _nextId);
    public string Category { get; init; } = string.Empty;
    public NetworkDomain Domain { get; init; } = NetworkDomain.Other;
    public bool HasType { get; init; } = true;
    public string InstanceName { get; init; } = string.Empty;
    public string FamilyName { get; init; } = string.Empty;
    public string TypeName { get; init; } = string.Empty;
    public Dictionary<string, string> Instance { get; init; } = new();
    public Dictionary<string, string> Type { get; init; } = new();
    public string InstanceComments { get; init; } = string.Empty;
    public string TypeComments { get; init; } = string.Empty;
    public string SystemText { get; init; } = string.Empty;
    public string? Workset { get; init; }
    public string DocumentTitle { get; init; } = string.Empty;
    public IElementFacts? Parent { get; set; }
    public IElementFacts? Host { get; set; }

    public string InstanceParameter(string name) => Instance.TryGetValue(name, out var value) ? value : string.Empty;
    public string TypeParameter(string name) => Type.TryGetValue(name, out var value) ? value : string.Empty;
    public IEnumerable<string> InstanceStringValues() => Instance.Values.Where(v => v.Length > 0);
    public IEnumerable<string> TypeStringValues() => Type.Values.Where(v => v.Length > 0);

    public override string ToString() => $"{Category} {FamilyName}/{TypeName} {InstanceParameter("ADSK_Наименование")}";
}
