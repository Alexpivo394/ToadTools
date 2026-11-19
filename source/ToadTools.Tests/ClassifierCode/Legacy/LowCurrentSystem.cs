// Замороженная копия логики ClassifierCode до перехода на профили — эталон для тестов паритета встроенного профиля.
// Не менять: тесты сравнивают с ней новый движок.
namespace ClassifierCodeLegacy.Models;

/// <summary>Подсистема СС (04.05.03.XX) и её позиции по ролям.</summary>
public sealed class LowCurrentSystem(string name)
{
    public string Name { get; } = name;
    public Dictionary<Role, ClassifierItem> Items { get; } = new();
}
