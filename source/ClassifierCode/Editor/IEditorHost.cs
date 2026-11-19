using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;

namespace ClassifierCode.Editor;

/// <summary>Всё, что редактору нужно от Revit и от окон Windows. Реализация — RevitEditorHost; в тестах — подделка.</summary>
public interface IEditorHost
{
    IReadOnlyList<string> ProjectParameterNames();

    /// <summary>Элементы, выделенные в Revit, когда открывали окно.</summary>
    IReadOnlyList<long> CurrentSelection();

    /// <summary>Выбрать элементы щелчком в модели (окна на это время сворачиваются). Пусто — отменили.</summary>
    IReadOnlyList<long> PickElements();

    IElementFacts? Facts(long id);
    string Label(long id);

    /// <summary>Элементы категорий профиля в области, у которых есть параметр кода.</summary>
    IReadOnlyList<long> CollectScope(RunScope scope, ClassifierProfile profile);

    /// <summary>Код, который сейчас записан у элемента (или его типа).</summary>
    string CurrentCode(long id, ClassifierProfile profile);

    void Select(IEnumerable<long> ids);

    /// <summary>Выбрать файл Excel и прочитать первый лист. null — отменили.</summary>
    IReadOnlyList<string[]>? ReadXlsx();

    string? ClipboardText();
    string? AskSavePath(string defaultName);
    string? AskOpenPath();
}

/// <summary>Вопросы и сообщения пользователю.</summary>
public interface IDialogs
{
    /// <summary>true — да, false — нет, null — отмена.</summary>
    bool? Ask(string title, string message);

    void Info(string title, string message);
}
