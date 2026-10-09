namespace ClassifierCode.Models;

/// <summary>Подсистема СС (04.05.03.XX) и её позиции по ролям.</summary>
public sealed class LowCurrentSystem(string name)
{
    public string Name { get; } = name;
    public Dictionary<Role, ClassifierItem> Items { get; } = new();
}
