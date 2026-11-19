using ClassifierCode.Editor;
using ClassifierCode.Editor.ViewModels;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Models;

namespace ToadTools.Tests.ClassifierCode;

public sealed class FakeEditorHost : IEditorHost
{
    public List<string> Parameters { get; } = ["ADSK_Код по классификатору", "ADSK_Описание по классификатору", "Мой_Код"];
    public List<long> Selection { get; } = [];
    public List<long> Picked { get; } = [];
    public IReadOnlyList<long> PickElements() => Picked;
    public Dictionary<long, FakeFacts> Elements { get; } = new();
    public Dictionary<long, string> Current { get; } = new();
    public List<long> Selected { get; } = [];
    public IReadOnlyList<string[]>? Xlsx { get; set; }
    public string? Clipboard { get; set; }
    public string? SavePath { get; set; }
    public string? OpenPath { get; set; }

    public IReadOnlyList<string> ProjectParameterNames() => Parameters;
    public IReadOnlyList<long> CurrentSelection() => Selection;
    public IElementFacts? Facts(long id) => Elements.TryGetValue(id, out var facts) ? facts : null;
    public string Label(long id) => Elements.TryGetValue(id, out var facts) ? facts.ToString() : id.ToString();
    public IReadOnlyList<long> CollectScope(RunScope scope, ClassifierProfile profile) => scope == RunScope.Selection ? Selection : Elements.Keys.ToList();
    public string CurrentCode(long id, ClassifierProfile profile) => Current.TryGetValue(id, out var code) ? code : string.Empty;
    public void Select(IEnumerable<long> ids) => Selected.AddRange(ids);
    public IReadOnlyList<string[]>? ReadXlsx() => Xlsx;
    public string? ClipboardText() => Clipboard;
    public string? AskSavePath(string defaultName) => SavePath;
    public string? AskOpenPath() => OpenPath;
}

public sealed class FakeDialogs : IDialogs
{
    public bool? Answer { get; set; } = true;
    public List<string> Asked { get; } = [];
    public List<string> Shown { get; } = [];

    public bool? Ask(string title, string message)
    {
        Asked.Add(message);
        return Answer;
    }

    public void Info(string title, string message) => Shown.Add(message);
}

/// <summary>Редактор над копией встроенного профиля во временной папке.</summary>
public static class EditorSetup
{
    public static ClassifierProfile DonskoyCopy() => ProfileSerializer.Clone(Donskoy.Profile.Source);

    public static (ProfileEditorViewModel Editor, FakeEditorHost Host, FakeDialogs Dialogs) Open(TempFolder folder, bool builtin = false, string? settingsPath = null, ClassifierSettings? settings = null)
    {
        var library = new ProfileLibrary(folder.Path, [], () => [Donskoy.Profile.Source]);
        var entry = builtin
            ? library.List()[0]
            : library.Save(library.Duplicate(Donskoy.Profile.Source), null);
        var host = new FakeEditorHost();
        var dialogs = new FakeDialogs();
        settings ??= settingsPath != null ? ClassifierSettings.Load(settingsPath) : new ClassifierSettings();
        return (new ProfileEditorViewModel(entry, library, host, dialogs, action => action(), settings, settingsPath), host, dialogs);
    }
}
