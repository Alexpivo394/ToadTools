using System.Text.Json;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Collisions.Core;
using Shouldly;
using ToadTools.Connection;

namespace ToadTools.Tests;

/// <summary>
///     Файлы и ответы, которые раньше разбирал Newtonsoft.Json, читаются так же: прежние версии плагина писали их иначе,
///     а профили правят руками.
/// </summary>
public class JsonCompatibilityTests
{
    [Test]
    public void Profile_NullList_KeepsDefault()
    {
        var profile = ProfileSerializer.Read("""{ "schemaVersion": 1, "name": "А", "modes": null, "texts": null }""");

        profile.Modes.ShouldBeEmpty();
        profile.Texts.ShouldBeEmpty();
    }

    [Test]
    public void Profile_CommentsTrailingCommaAndCase_Read()
    {
        var profile = ProfileSerializer.Read("""
            {
              // профиль, поправленный руками
              "schemaVersion": 1,
              "Name": "Объект",
              "categories": ["Трубы", "Воздуховоды",],
            }
            """);

        profile.Name.ShouldBe("Объект");
        profile.Categories.ShouldBe(["Трубы", "Воздуховоды"]);
    }

    [Test]
    public void Profile_Write_KeepsCyrillicAndSkipsEmpty()
    {
        var json = ProfileSerializer.Write(new ClassifierProfile { Name = "Донской", Description = "«Кавычки» & <теги>" });

        json.ShouldContain("\"name\": \"Донской\"");
        json.ShouldContain("«Кавычки» & <теги>");
        json.ShouldNotContain("\"modes\"");
        json.ShouldNotContain("\\u");
    }

    [Test]
    public void Profile_DuplicateKey_LastWins()
    {
        ProfileSerializer.Read("""{ "schemaVersion": 1, "name": "А", "name": "Б" }""").Name.ShouldBe("Б");
    }

    [Test]
    public void Export_ParameterWithNumberAndNull_ReadAsText()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("m.export.json"), """
            {
              "schemaVersion": 1, "model": "М", "exportedAt": "2026-11-19T19:58:15.363+03:00",
              "types": { "t": { "family": "Сем", "name": "Тип", "parameters": [["Код", 12.5, 1]] } },
              "elements": [{ "id": 7, "category": "Трубы", "typeId": "t", "parameters": [["Имя", null], ["Флаг", true, 3]] }]
            }
            """);

        var model = ExportReader.Read(temp.File("m.export.json"));

        var type = model.Types["t"].Parameters.Single();
        (type.Name, type.Value, type.IsString, type.Lookup).ShouldBe(("Код", "12.5", true, false));
        var parameters = model.Elements.Single().Parameters;
        (parameters[0].Value, parameters[0].IsString).ShouldBe((string.Empty, false));
        (parameters[1].Value, parameters[1].IsString, parameters[1].Lookup).ShouldBe(("true", true, true));
        model.ExportedAt.ToUniversalTime().ShouldBe(new DateTime(2026, 11, 19, 16, 58, 15, 363, DateTimeKind.Utc));
    }

    [Test]
    public void Export_Header_SkipsTypesAndElements()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("m.export.json"), """
            { "schemaVersion": 1, "model": "М", "types": { "t": { "name": "Тип" } }, "elements": [{ "id": 7 }], "source": "RSN://srv/М.rvt" }
            """);

        var header = ExportReader.ReadHeader(temp.File("m.export.json"));

        (header.Model, header.Source).ShouldBe(("М", "RSN://srv/М.rvt"));
        header.Types.ShouldBeEmpty();
        header.Elements.ShouldBeEmpty();
    }

    [Test]
    public void ClassifierSettings_WrittenByOldVersion_Read()
    {
        using var temp = new TempFolder();
        File.WriteAllText(temp.File("settings.json"), """
            {
              "SharedFolders": [ "Y:\\Профили" ],
              "LastProfileId": "5d0d3f0c-6a3e-4c6b-9c0a-2f1f1b6f0a11",
              "ShowAllEditorTabs": true,
              "AiExport": { "Source": "folder", "ObjectName": "Донской", "BaseProfileId": null }
            }
            """);

        var settings = ClassifierSettings.Load(temp.File("settings.json"));

        settings.SharedFolders.ShouldBe([@"Y:\Профили"]);
        settings.LastProfileId.ShouldBe(Guid.Parse("5d0d3f0c-6a3e-4c6b-9c0a-2f1f1b6f0a11"));
        settings.ShowAllEditorTabs.ShouldBeTrue();
        (settings.AiExport.Source, settings.AiExport.ObjectName, settings.AiExport.BaseProfileId).ShouldBe(("folder", "Донской", null));
    }

    [Test]
    public void ModelChoices_RoundTrip_CyrillicPath()
    {
        using var temp = new TempFolder();
        var choices = new ModelChoices(temp.File("models.json"));

        choices.Set(@"C:\Проекты\Корпус 1.rvt", "key-1");

        new ModelChoices(temp.File("models.json")).Get(@"c:\проекты\корпус 1.rvt").ShouldBe("key-1");
        File.ReadAllText(temp.File("models.json")).ShouldContain("Корпус 1.rvt");
    }

    private sealed record Loose(string Text, int Number, Guid Id, string? Missing);

    [Test]
    public void ToadFlow_NumberForStringAndStringForNumber_Read()
    {
        var read = JsonSerializer.Deserialize<Loose>(
            """{ "Text": 42, "number": "7", "id": "5d0d3f0c-6a3e-4c6b-9c0a-2f1f1b6f0a11", "extra": [1, 2] }""", ToadFlowClient.Json)!;

        (read.Text, read.Number, read.Missing).ShouldBe(("42", 7, null));
        read.Id.ShouldBe(Guid.Parse("5d0d3f0c-6a3e-4c6b-9c0a-2f1f1b6f0a11"));
    }

    [Test]
    public void ToadFlow_Body_CamelCaseEnumNameCyrillic()
    {
        var json = JsonSerializer.Serialize(new { Status = CollisionStatus.FixedPending, Note = "Гильза", Skipped = (string?)null }, ToadFlowClient.Json);

        json.ShouldBe("""{"status":"FixedPending","note":"Гильза","skipped":null}""");
    }

    [Test]
    public void ToadFlow_EnumAsNumber_Read()
    {
        JsonSerializer.Deserialize<CollisionStatus>("3", ToadFlowClient.Json).ShouldBe(CollisionStatus.Fake);
        JsonSerializer.Deserialize<CollisionStatus>("\"fake\"", ToadFlowClient.Json).ShouldBe(CollisionStatus.Fake);
    }
}
