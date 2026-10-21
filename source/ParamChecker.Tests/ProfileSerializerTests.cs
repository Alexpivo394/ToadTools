using ParamChecker.Core;
using ParamChecker.Core.Profiles;
using Shouldly;

namespace ParamChecker.Tests;

public class ProfileSerializerTests
{
    private static string? CategoryName(int id) => id switch
    {
        -2008000 => "OST_DuctCurves",
        -2008020 => "OST_FlexDuctCurves",
        _ => null
    };

    /// <summary>Файл экспорта прежнего ParamChecker: массив профилей, фильтр и параметры — строки JSON.</summary>
    private const string Legacy = """
        [
          {
            "ProfileName": "ОВ",
            "IsChecked": true,
            "Models": [
              { "ServerPath": "RSN://rs01/Объект/Объект_ОВ_R22.rvt", "ViewName": "Navisworks", "WorksetKeyword": "Связь" },
              { "ServerPath": "RSN://rs01/Объект/Объект_ВК_R22.rvt", "ViewName": "navisworks", "WorksetKeyword": "Связь" }
            ],
            "Rules": [
              {
                "Title": "Воздуховоды",
                "FilterConfigJson": "{\"SelectedCategories\":[-2008000,-2008020,-1],\"CategoryParameterLogic\":3,\"ParameterLogic\":1,\"Conditions\":[{\"Type\":\"Simple\",\"ParameterName\":\"ADSK_Наименование\",\"Value\":\"Воздуховод\",\"SelectedLogic\":0},{\"Type\":\"Group\",\"Children\":[{\"Type\":\"Simple\",\"ParameterName\":\"Имя системы\",\"Value\":\"П1\",\"SelectedLogic\":6}]}]}",
                "ParameterConfigJson": "{\"Parameters\":[{\"Value\":\"ADSK_Марка\"},{\"Value\":\" \"},{\"Value\":\"ADSK_Код изделия\"}]}"
              },
              { "Title": "Пустое", "FilterConfigJson": "", "ParameterConfigJson": "мусор" }
            ]
          }
        ]
        """;

    [Test]
    public void ReadsLegacyFile()
    {
        var set = ProfileSerializer.Deserialize(Legacy, CategoryName);

        var profile = set.Profiles.ShouldHaveSingleItem();
        profile.Name.ShouldBe("ОВ");
        profile.IsEnabled.ShouldBeTrue();
        profile.Models.Select(m => m.Name).ShouldBe(["Объект_ОВ_R22", "Объект_ВК_R22"]);

        var rule = profile.Rules[0];
        rule.Title.ShouldBe("Воздуховоды");
        rule.Parameters.ShouldBe(["ADSK_Марка", "ADSK_Код изделия"]);
        rule.Filter.Categories.ShouldBe(["OST_DuctCurves", "OST_FlexDuctCurves"]);
        rule.Filter.Mode.ShouldBe(SelectionMode.CategoriesAndParameters);
        rule.Filter.Logic.ShouldBe(ConditionLogic.Or);
        rule.Filter.Conditions[0].Parameter.ShouldBe("ADSK_Наименование");
        rule.Filter.Conditions[0].Operator.ShouldBe(ConditionOperator.Equals);
        rule.Filter.Conditions[1].Group!.ShouldHaveSingleItem().Operator.ShouldBe(ConditionOperator.Contains);

        profile.Rules[1].Parameters.ShouldBeEmpty();
        profile.Rules[1].Filter.Categories.ShouldBeEmpty();
    }

    [Test]
    public void LegacyFile_CommonViewAndWorksetBecomeProfileDefaults()
    {
        var profile = ProfileSerializer.Deserialize(Legacy, CategoryName).Profiles[0];

        profile.ViewName.ShouldBe("Navisworks");
        profile.WorksetKeyword.ShouldBe("Связь");
        profile.Models.ShouldAllBe(model => model.ViewName == null && model.WorksetKeyword == null);
    }

    [Test]
    public void LegacyFile_DifferentViewsStayOnModels()
    {
        var json = """[{"ProfileName":"A","Models":[{"ServerPath":"a.rvt","ViewName":"V1"},{"ServerPath":"b.rvt","ViewName":"V2"}]}]""";

        var profile = ProfileSerializer.Deserialize(json, CategoryName).Profiles[0];

        profile.ViewName.ShouldBe("");
        profile.Models.Select(m => m.ViewName).ShouldBe(["V1", "V2"]);
    }

    [Test]
    public void RoundTrip()
    {
        var set = ProfileSerializer.Deserialize(Legacy, CategoryName);

        var json = ProfileSerializer.Serialize(set);
        var again = ProfileSerializer.Deserialize(json, _ => throw new InvalidOperationException("числа в новом формате не нужны"));

        json.ShouldContain("\"CategoriesAndParameters\"");
        ProfileSerializer.Serialize(again).ShouldBe(json);
    }

    [Test]
    public void RuleClipboard_RoundTripAndRejectsForeignText()
    {
        var rule = ProfileSerializer.Deserialize(Legacy, CategoryName).Profiles[0].Rules[0];

        var copy = ProfileSerializer.DeserializeRule(ProfileSerializer.SerializeRule(rule)).ShouldNotBeNull();

        copy.ShouldNotBeSameAs(rule);
        copy.Parameters.ShouldBe(rule.Parameters);
        copy.Filter.Conditions.Count.ShouldBe(2);
        ProfileSerializer.DeserializeRule("просто текст").ShouldBeNull();
        ProfileSerializer.DeserializeRule("""{"Profiles":[]}""").ShouldBeNull();
    }

    [Test]
    [Arguments("{}")]
    [Arguments("не json")]
    [Arguments("42")]
    public void RejectsForeignFiles(string json) =>
        Should.Throw<InvalidDataException>(() => ProfileSerializer.Deserialize(json, CategoryName));

    [Test]
    [Arguments("RSN://rs01/Объект/Объект_ОВ_R22.rvt", "Объект_ОВ_R22")]
    [Arguments(@"C:\Модели\Дом_АР.rvt", "Дом_АР")]
    [Arguments("Дом_АР.RVT", "Дом_АР")]
    public void ModelNameIsFileNameWithoutExtension(string path, string name) => ModelNames.FromPath(path).ShouldBe(name);

    [Test]
    public void ServerPaths_WithBackslashes_AreTheSameModel()
    {
        const string legacy = @"RSN:\\vsrevit03\Projects\HL\HL_OVK_K4_R22.rvt";

        ModelNames.IsServerPath(legacy).ShouldBeTrue();
        ModelNames.Key(legacy).ShouldBe("RSN://vsrevit03/Projects/HL/HL_OVK_K4_R22.rvt");
        ModelNames.ServerOf(legacy).ShouldBe("vsrevit03");
        ModelNames.ServerOf(@"C:\Модели\Дом.rvt").ShouldBeNull();
        ModelNames.Key(@"C:\Модели\Дом.rvt").ShouldBe(@"C:\Модели\Дом.rvt");
    }

    [Test]
    public void LegacyFile_DropsDuplicateParameters()
    {
        var json = """[{"ProfileName":"A","Rules":[{"Title":"R","ParameterConfigJson":"{\"Parameters\":[{\"Value\":\"Марка\"},{\"Value\":\"Марка \"}]}"}]}]""";

        ProfileSerializer.Deserialize(json, CategoryName).Profiles[0].Rules[0].Parameters.ShouldBe(["Марка"]);
    }
}
