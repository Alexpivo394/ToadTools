namespace ClassifierCode.Models;

/// <summary>Результат классификации одного элемента.</summary>
public sealed class Classification
{
    public Discipline Discipline { get; set; }
    public Role Role { get; set; }
    public ClassifierItem? Item { get; set; }
    public string? Reason { get; set; }

    /// <summary>Элемент намеренно не кодируется (задания на отверстия в ЭОМ).</summary>
    public bool Skip { get; set; }
}
