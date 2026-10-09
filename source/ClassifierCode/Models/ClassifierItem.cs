namespace ClassifierCode.Models;

/// <summary>Позиция классификатора: код и описание.</summary>
public sealed class ClassifierItem(string code, string description)
{
    public string Code { get; } = code;
    public string Description { get; } = description;
}
