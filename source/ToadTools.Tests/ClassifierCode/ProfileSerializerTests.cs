using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ProfileSerializerTests
{
    private static ClassifierProfile Sample() => new()
    {
        SchemaVersion = 1,
        Id = new Guid("11111111-2222-3333-4444-555555555555"),
        Name = "Тест",
        Description = "Описание",
        Author = "Автор",
        Version = "1.0",
        FileNamePatterns = ["ШФТ*"],
        Outputs = [new OutputField { Field = "code", Parameter = "ADSK_Код по классификатору" }],
        Categories = ["OST_PipeCurves"],
        Texts = { ["primaryName"] = new TextSourceDef { Parts = ["instanceOrType:ADSK_Наименование"], Lowercase = true } },
        PatternSets = { ["kip"] = ["манометр"] },
        Positions = [new Position { Code = "01", Description = "Оборудование", Extra = { ["Ед"] = "шт" } }],
        Disciplines = [new KeyTitle { Key = "water", Title = "Водопровод", Position = "01" }],
        Types = [new KeyTitle { Key = "kip", Title = "КИП" }],
        Dictionaries =
        {
            ["types"] = new DictionaryDef
            {
                Mode = "earliest",
                Entries =
                [
                    new DictionaryEntryDef
                    {
                        Result = new ResultDef { Pipe = new ResultDef { Type = "pipeGroup" }, Air = new ResultDef { Type = "ductGroup" } },
                        Patterns = ["@kip"],
                        When = [new ConditionDef { Not = true, Category = ["OST_PipeAccessory"] }]
                    }
                ]
            }
        },
        RuleSets =
        {
            ["ios"] =
            [
                new RuleDef
                {
                    Title = "Трубы",
                    When = [new ConditionDef { Category = ["OST_PipeCurves"] }],
                    Steps = [new StepDef { Dictionary = "types", Source = "primaryName", Refine = [new RefineDef { When = [new ConditionDef { Type = ["kip"] }], To = new ResultDef { Type = "kip" } }] }],
                    Allow = new AllowDef { Values = ["kip"], Otherwise = "drop" },
                    Default = new ResultDef { Type = "kip" },
                    Fallthrough = true
                }
            ]
        },
        Tables = { ["ios"] = new TableDef { Rows = [new TableRow { Discipline = "water", Type = "kip", Position = "01" }], Fallbacks = { ["kip"] = ["armature"] } } },
        Modes = [new ModeDef { Key = "water", Title = "ВК", TypeRules = "ios", Table = "ios", DefaultDiscipline = "water", FileTokens = ["ВК"] }],
        Exceptions = [new RuleDef { Steps = [new StepDef { Fixed = new ResultDef { Skip = "вручную" } }] }]
    };

    [Test]
    public void Read_Write_RoundTrip_KeepsEverything()
    {
        var json = ProfileSerializer.Write(Sample());
        var again = ProfileSerializer.Write(ProfileSerializer.Read(json));

        again.ShouldBe(json);
        json.ShouldContain("\"schemaVersion\": 1");
        json.ShouldContain("\"fallthrough\": true");
        json.ShouldContain("ductGroup");
    }

    [Test]
    public void Read_NewerSchema_Throws()
    {
        var error = Should.Throw<ProfileReadException>(() => ProfileSerializer.Read("{\"schemaVersion\":2}"));
        error.Message.ShouldContain("новее");
    }

    [Test]
    public void Read_Garbage_Throws()
    {
        var error = Should.Throw<ProfileReadException>(() => ProfileSerializer.Read("{не json"));
        error.Message.ShouldStartWith("Не удалось прочитать профиль");
    }

    [Test]
    public void Read_UnknownField_Ignored()
    {
        var profile = ProfileSerializer.Read("{\"schemaVersion\":1,\"name\":\"А\",\"foo\":1}");
        profile.Name.ShouldBe("А");
    }
}
