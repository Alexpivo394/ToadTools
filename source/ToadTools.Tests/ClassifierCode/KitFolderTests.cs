using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class KitFolderTests
{
    /// <summary>Набор, как он лежит в установке: publish\ToadTools\ClassifierCode\AiKit.</summary>
    private static TempFolder Source()
    {
        var kit = new TempFolder();
        File.WriteAllText(kit.File("AGENTS.md"), "# Инструкция агенту");
        File.WriteAllText(kit.File("FORMAT.md"), "# Формат");
        File.WriteAllText(kit.File("profile.schema.json"), "{}");
        File.WriteAllText(kit.File("README_КООРДИНАТОРУ.txt"), "читать");
        Directory.CreateDirectory(kit.File("check"));
        File.WriteAllText(kit.File("check\\check.exe"), "exe");
        File.WriteAllText(kit.File("check\\EPPlus.dll"), "dll");
        return kit;
    }

    [Test]
    public void Prepare_LaysOutKit()
    {
        using var kit = Source();
        using var work = new TempFolder();
        KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, Donskoy.Profile.Source);

        File.ReadAllText(work.File("CLAUDE.md")).ShouldContain("@AGENTS.md");
        File.ReadAllText(work.File("AGENTS.md")).ShouldBe("# Инструкция агенту");
        File.Exists(work.File("README_КООРДИНАТОРУ.txt")).ShouldBeTrue();
        File.Exists(work.File("kit\\FORMAT.md")).ShouldBeTrue();
        File.Exists(work.File("kit\\profile.schema.json")).ShouldBeTrue();
        File.Exists(work.File("kit\\check\\check.exe")).ShouldBeTrue();
        File.Exists(work.File("kit\\check\\EPPlus.dll")).ShouldBeTrue();
        Directory.Exists(work.File("exports")).ShouldBeTrue();
        ProfileSerializer.Read(File.ReadAllText(work.File("kit\\base.classifier.json"))).Id.ShouldBe(Donskoy.Profile.Source.Id);
        File.Exists(work.File("kit\\filled-by.classifier.json")).ShouldBeTrue();
    }

    [Test]
    public void Prepare_Again_KeepsWork()
    {
        using var kit = Source();
        using var work = new TempFolder();
        KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, Donskoy.Profile.Source);
        Directory.CreateDirectory(work.File("work"));
        File.WriteAllText(work.File("work\\notes.md"), "заметки");
        File.WriteAllText(work.File("profile.classifier.json"), "профиль агента");
        File.WriteAllText(work.File("REVIEW.md"), "отчёт");
        File.WriteAllText(work.File("kit\\check\\old.dll"), "старый");

        KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, null);

        File.ReadAllText(work.File("work\\notes.md")).ShouldBe("заметки");
        File.ReadAllText(work.File("profile.classifier.json")).ShouldBe("профиль агента");
        File.ReadAllText(work.File("REVIEW.md")).ShouldBe("отчёт");
        File.Exists(work.File("kit\\check\\old.dll")).ShouldBeFalse(); // проверялка обновляется целиком
        File.Exists(work.File("kit\\filled-by.classifier.json")).ShouldBeTrue(); // без нового — прежний остаётся
    }

    [Test]
    public void Prepare_MissingKit_Throws()
    {
        using var kit = new TempFolder();
        using var work = new TempFolder();
        Should.Throw<IOException>(() => KitFolder.Prepare(work.Path, kit.Path, Donskoy.Profile.Source, null)).Message.ShouldContain("AGENTS.md");
    }

    [Test]
    public void CommonBinding_MostFrequent()
    {
        var a = new ModelBinding(Guid.NewGuid(), "А", "1.0", "heating");
        var b = new ModelBinding(Guid.NewGuid(), "Б", "2.0", "water");
        KitFolder.CommonBinding([a, b, null, b with { Mode = "fire" }]).ShouldBe(b);
        KitFolder.CommonBinding([null, null]).ShouldBeNull();
    }

    [Test]
    public void DefaultFolder_SafeName()
    {
        KitFolder.DefaultFolder("ЖК: Донской/2").ShouldEndWith(Path.Combine("ToadTools", "Классификатор", "ЖК_ Донской_2"));
    }
}
