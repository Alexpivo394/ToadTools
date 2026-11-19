using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ProfileLibraryTests
{
    private static readonly Guid BuiltinId = new("6f1c3b8e-2d4a-4e7b-9a51-0c8d7e2f4a10");

    private static ClassifierProfile Profile(string name, Guid? id = null) => new() { Id = id ?? Guid.NewGuid(), Name = name, Version = "1.0" };

    private static ProfileLibrary Library(TempFolder personal, string? shared = null, Func<string, bool>? canWrite = null) =>
        new(personal.Path, shared != null ? [shared] : [], () => [Profile("Пионер. Донской", BuiltinId)], canWrite);

    [Test]
    public void List_BuiltinFirst_ThenFiles()
    {
        using var temp = new TempFolder();
        var library = Library(temp);
        library.Save(Profile("Объект А"), null);

        var entries = library.List();
        entries[0].Origin.ShouldBe(ProfileOrigin.Builtin);
        entries[0].ReadOnly.ShouldBeTrue();
        entries.ShouldContain(e => e.Profile!.Name == "Объект А" && e.Origin == ProfileOrigin.Personal && !e.ReadOnly);
    }

    [Test]
    public void List_BrokenFile_ReturnsEntryWithError()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("битый.classifier.json"), "{не json");

        var broken = Library(temp).List().Single(e => e.Path != null);
        broken.Profile.ShouldBeNull();
        broken.Error!.ShouldContain("Не удалось прочитать профиль");
    }

    [Test]
    public void Save_MakesBak()
    {
        using var temp = new TempFolder();
        var library = Library(temp);
        var profile = Profile("Объект А");
        var entry = library.Save(profile, null);
        profile.Description = "второй";
        library.Save(profile, entry.Path);

        File.Exists(entry.Path + ".bak").ShouldBeTrue();
        File.ReadAllText(entry.Path + ".bak").ShouldNotContain("второй");
        File.ReadAllText(entry.Path!).ShouldContain("второй");
    }

    [Test]
    public void Save_SameNameOtherProfile_GetsOwnFile()
    {
        using var temp = new TempFolder();
        var library = Library(temp);
        var first = library.Save(Profile("Объект"), null);
        var second = library.Save(Profile("Объект"), null);
        second.Path.ShouldNotBe(first.Path);
    }

    [Test]
    public void Save_ReadOnlyFolder_Refuses()
    {
        using var temp = new TempFolder();
        using var shared = new TempFolder();
        File.WriteAllText(shared.File("общий.classifier.json"), ProfileSerializer.Write(Profile("Общий")));
        var library = Library(temp, shared.Path, folder => folder != shared.Path);

        var entry = library.List().Single(e => e.Origin == ProfileOrigin.Shared);
        entry.ReadOnly.ShouldBeTrue();
        Should.Throw<ProfileSaveException>(() => library.Save(entry.Profile!, entry.Path));
    }

    [Test]
    public void Save_Builtin_Refuses()
    {
        using var temp = new TempFolder();
        var library = Library(temp);
        Should.Throw<ProfileSaveException>(() => library.Save(library.List()[0].Profile!, null));
    }

    [Test]
    public void Read_NewerSchema_NotOverwritten()
    {
        using var temp = new TempFolder();
        var path = temp.File("новый.classifier.json");
        File.WriteAllText(path, "{\"schemaVersion\":9,\"name\":\"Из будущего\"}");
        var library = Library(temp);

        library.List().Single(e => e.Path == path).Error!.ShouldContain("новее");
        Should.Throw<ProfileSaveException>(() => library.Save(Profile("Затираю"), path));
        File.ReadAllText(path).ShouldContain("Из будущего");
    }

    [Test]
    public void Duplicate_NewIdAndName()
    {
        using var temp = new TempFolder();
        var source = Profile("Пионер. Донской", BuiltinId);
        var copy = Library(temp).Duplicate(source);
        copy.Id.ShouldNotBe(BuiltinId);
        copy.Name.ShouldBe("Пионер. Донской (копия)");
        source.Name.ShouldBe("Пионер. Донской");
    }

    [Test]
    public void Import_CopiesIntoPersonalFolder()
    {
        using var temp = new TempFolder();
        using var outside = new TempFolder();
        var file = outside.File("x.classifier.json");
        File.WriteAllText(file, ProfileSerializer.Write(Profile("Чужой")));

        var entry = Library(temp).Import(file);
        entry.Origin.ShouldBe(ProfileOrigin.Personal);
        Path.GetDirectoryName(entry.Path).ShouldBe(temp.Path);
    }

    [Test]
    public void Settings_RoundTrip_AndBrokenFileGivesDefaults()
    {
        using var temp = new TempFolder();
        var path = temp.File("settings.json");
        var settings = new ClassifierSettings { SharedFolders = [@"\\server\bim"], LastProfileId = BuiltinId };
        settings.Save(path);
        ClassifierSettings.Load(path).SharedFolders.ShouldBe([@"\\server\bim"]);

        File.WriteAllText(path, "{oops");
        ClassifierSettings.Load(path).SharedFolders.ShouldBeEmpty();
    }
}

public class ProfileChoiceTests
{
    private static readonly ClassifierProfile Donskoy = new()
    {
        Id = Guid.NewGuid(), Name = "Донской", Version = "1.0",
        Modes = [new ModeDef { Key = "fire" }, new ModeDef { Key = "vent" }]
    };

    private static readonly ClassifierProfile Other = new()
    {
        Id = Guid.NewGuid(), Name = "Объект Б", Version = "2.0", FileNamePatterns = ["ОБЪБ*"],
        Modes = [new ModeDef { Key = "vent" }]
    };

    private static readonly IReadOnlyList<ProfileEntry> Entries =
    [
        new(Donskoy, null, ProfileOrigin.Builtin, true, null),
        new(Other, "b.classifier.json", ProfileOrigin.Personal, false, null),
        new(null, "broken.classifier.json", ProfileOrigin.Personal, false, "битый")
    ];

    [Test]
    public void Resolve_BindingWins()
    {
        var choice = ProfileChoice.Resolve(Entries, new ModelBinding(Other.Id, "Объект Б", "2.0", "vent"), "ОБЪБ-ПТ", Donskoy.Id);
        choice.Entry.Profile.ShouldBe(Other);
        choice.ModeKey.ShouldBe("vent");
        choice.Warning.ShouldBeNull();
    }

    [Test]
    public void Resolve_BindingMissingProfile_Warns_FallsBack()
    {
        var choice = ProfileChoice.Resolve(Entries, new ModelBinding(Guid.NewGuid(), "Пропавший", "3.1", "fire"), "ОБЪБ-ПТ", null);
        choice.Warning!.ShouldContain("«Пропавший» 3.1");
        choice.Entry.Profile.ShouldBe(Other); // по шаблону имени файла
        choice.ModeKey.ShouldBeNull();
    }

    [Test]
    public void Resolve_BindingModeMissing_ModeNull()
    {
        var choice = ProfileChoice.Resolve(Entries, new ModelBinding(Other.Id, "Объект Б", "2.0", "fire"), "Модель", null);
        choice.Entry.Profile.ShouldBe(Other);
        choice.ModeKey.ShouldBeNull();
    }

    [Test]
    public void Resolve_FileNamePattern_ThenLast_ThenBuiltin()
    {
        ProfileChoice.Resolve(Entries, null, "ОБЪБ-ВК", Donskoy.Id).Entry.Profile.ShouldBe(Other);
        ProfileChoice.Resolve(Entries, null, "Модель", Other.Id).Entry.Profile.ShouldBe(Other);
        ProfileChoice.Resolve(Entries, null, "Модель", null).Entry.Profile.ShouldBe(Donskoy);
        ProfileChoice.Resolve(Entries, null, "Модель", Guid.NewGuid()).Entry.Profile.ShouldBe(Donskoy);
    }
}
