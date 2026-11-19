// Замороженная копия логики ClassifierCode до перехода на профили — эталон для тестов паритета встроенного профиля.
// Не менять: тесты сравнивают с ней новый движок.
namespace ClassifierCodeLegacy.Models;

/// <summary>Позиция классификатора: код и описание.</summary>
public sealed class ClassifierItem(string code, string description)
{
    public string Code { get; } = code;
    public string Description { get; } = description;
}
