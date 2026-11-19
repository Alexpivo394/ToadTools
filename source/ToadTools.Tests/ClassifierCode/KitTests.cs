using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Kit;
using ClassifierCode.Engine.Library;
using ClassifierCode.Engine.Profile;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Выгрузка из набора FakeFacts: удобно собирать модели для проверялки.</summary>
public static class Exp
{
    public static ExportModel Model(string name, IEnumerable<FakeFacts> elements, bool withCode = true)
    {
        var model = new ExportModel { Model = name, Categories = global::ClassifierCode.Editor.ViewModels.CategoryCatalog.All.Select(c => c.Name).ToList() };
        long id = 0;
        foreach (var f in elements)
        {
            id++;
            var typeId = "t" + id;
            model.Types[typeId] = new ExportType
            {
                Family = f.FamilyName, Name = f.TypeName,
                Parameters = f.Type.Select(p => new ExportParameter { Name = p.Key, Value = p.Value, IsString = true }).ToList()
            };
            model.Elements.Add(new ExportElement
            {
                Id = id, Category = f.Category, TypeId = typeId, Name = f.InstanceName, SystemText = f.SystemText,
                Domain = ExportFacts.DomainKey(f.Domain), Workset = f.Workset, HasCodeParameter = withCode,
                CurrentCode = f.Instance.TryGetValue("__code", out var code) ? code : null,
                Parameters = f.Instance.Where(p => p.Key != "__code").Select(p => new ExportParameter { Name = p.Key, Value = p.Value, IsString = true }).ToList()
            });
        }

        return model;
    }

    public static FakeFacts Pipe(string system, string name = "Труба", string? workset = null, string? code = null)
    {
        var f = new FakeFacts { Category = "OST_PipeCurves", Domain = NetworkDomain.Pipe, SystemText = system, Workset = workset, FamilyName = "Труба", TypeName = "Сталь" };
        f.Instance["ADSK_Наименование"] = name;
        if (code != null) f.Instance["__code"] = code;
        return f;
    }

    public static FakeFacts Accessory(string name, string system, string? workset = null)
    {
        var f = new FakeFacts { Category = "OST_PipeAccessory", Domain = NetworkDomain.Pipe, SystemText = system, Workset = workset, FamilyName = "Арматура", TypeName = name };
        f.Instance["ADSK_Наименование"] = name;
        return f;
    }

    public static FakeFacts Radiator(string system) =>
        new() { Category = "OST_MechanicalEquipment", Domain = NetworkDomain.Pipe, SystemText = system, FamilyName = "Радиатор", TypeName = "500", Instance = { ["ADSK_Наименование"] = "Радиатор стальной" } };
}

public class KitTests
{
    [Test]
    public void Summary_GroupsAndSystems()
    {
        var model = Exp.Model("ШФТ-ОВО", [Exp.Pipe("Т1"), Exp.Pipe("Т1"), Exp.Pipe("Т12"), Exp.Accessory("Клапан", "Т1", "ОВ")]);
        var summary = SummaryBuilder.Build([model]);
        summary.Groups.ShouldContain(g => g.Family == "Труба" && g.Name == "Труба" && g.Count == 3);
        summary.Systems.ShouldContain(s => s.Name == "Т1" && s.Count == 3);
        summary.Worksets.ShouldContain(w => w.Workset == "ОВ" && w.Count == 1);
        summary.Parameters.ShouldContain(p => p.Name == "ADSK_Наименование" && p.FilledShare == 1.0);
        summary.Text().ShouldContain("ШФТ-ОВО");
    }

    [Test]
    public void Run_CountsAndTopNames()
    {
        var model = Exp.Model("ШФТ-ОВО", [Exp.Pipe("Т1"), Exp.Pipe("Т1"), Exp.Accessory("Клапан", "Т1"), Exp.Accessory("Нечто", "Т1")]);
        var run = RunReport.Build(Donskoy.Profile, [model], null);
        var m = run.Models.Single();
        m.Mode.ShouldBe("heating");
        m.Total.ShouldBe(4);
        m.Classified.ShouldBe(4);
        var pipes = run.Codes.Single(c => c.Code == "04.05.01.07.04.01");
        pipes.Count.ShouldBe(2);
        pipes.Top.ShouldContain(t => t.Label.Contains("Труба") && t.Count == 2);
        run.Text().ShouldContain("04.05.01.07.04.01");
    }

    [Test]
    public void Run_ModeUndetected_ReportedNotSkipped()
    {
        var run = RunReport.Build(Donskoy.Profile, [Exp.Model("Проект1", [Exp.Pipe("Т1")])], null);
        var m = run.Models.Single();
        m.Mode.ShouldBeNull();
        m.Note!.ShouldContain("modes.json");
        run.Text().ShouldContain("раздел не определён");

        var fixedRun = RunReport.Build(Donskoy.Profile, [Exp.Model("Проект1", [Exp.Pipe("Т1")])], new Dictionary<string, string> { ["Проект1"] = "heating" });
        fixedRun.Models.Single().Classified.ShouldBe(1);
    }

    [Test]
    public void Run_WithoutCodeParameter_CountedSeparately()
    {
        var run = RunReport.Build(Donskoy.Profile, [Exp.Model("ВК", [Exp.Pipe("В1")], withCode: false)], new Dictionary<string, string> { ["ВК"] = "water" });
        run.Models.Single().WithoutCodeParameter.ShouldBe(1);
        run.Models.Single().Classified.ShouldBe(1);
    }

    [Test]
    public void Run_Unclassified_GroupedWithReason()
    {
        var run = RunReport.Build(Donskoy.Profile, [Exp.Model("ВК", [Exp.Pipe(""), Exp.Pipe(""), Exp.Pipe("")])], null);
        var group = run.Unclassified.Single();
        group.Count.ShouldBe(3);
        group.Reason.ShouldContain("раздел");
        group.SampleIds.Count.ShouldBe(3);
    }

    [Test]
    public void Anomaly_NameVsSystem()
    {
        // 30 радиаторов в отоплении и один — в системе теплоснабжения: это ошибка модели, а не повод для правила.
        var radiators = Enumerable.Range(0, 30).Select(_ => Exp.Radiator("Т1")).Append(Exp.Radiator("Т12")).ToList();
        var a = AnomalyFinder.Find(Donskoy.Profile, [Exp.Model("ОВО", radiators)], null).Single();
        a.Where.ShouldContain("теплоснабжения");
        a.Expected.ShouldContain("отопления");
        a.Why.ShouldContain("раздел");
    }

    [Test]
    public void Anomaly_RareWorksetInGroup()
    {
        var items = Enumerable.Range(0, 97).Select(_ => Exp.Accessory("Клапан", "Т1", "ОВ")).Concat(Enumerable.Range(0, 3).Select(_ => Exp.Accessory("Клапан", "Т1", "ВК"))).ToList();
        var found = AnomalyFinder.Find(Donskoy.Profile, [Exp.Model("ОВО", items)], null);
        found.Count(a => a.Why.Contains("рабочий набор")).ShouldBe(3);
    }

    [Test]
    public void Anomaly_NotFlagged_WhenCommon()
    {
        var items = Enumerable.Range(0, 50).Select(_ => Exp.Accessory("Клапан", "Т1", "ОВ")).Concat(Enumerable.Range(0, 50).Select(_ => Exp.Accessory("Клапан", "Т1", "ВК"))).ToList();
        AnomalyFinder.Find(Donskoy.Profile, [Exp.Model("ОВО", items)], null).ShouldBeEmpty();
    }

    [Test]
    public void Diff_ManualOnlyAgainstFilledBy()
    {
        var model = Exp.Model("ОВО", [Exp.Pipe("Т1", code: "04.05.01.07.04.01"), Exp.Pipe("Т1", code: "999.РУЧНОЙ")]);
        model.Binding = new global::ClassifierCode.Engine.Library.ModelBinding(Donskoy.Profile.Source.Id, Donskoy.Profile.Source.Name, Donskoy.Profile.Source.Version, "heating");
        var diffs = DiffFinder.Find(Donskoy.Profile, Donskoy.Profile, [model], null);
        diffs.Single().IsManual.ShouldBeTrue();
        diffs.Single().Current.ShouldBe("999.РУЧНОЙ");
        DiffFinder.Find(Donskoy.Profile, null, [model], null).Single().IsManual.ShouldBeFalse();
    }

    [Test]
    public void Validate_CategoryNotExported()
    {
        var profile = ProfileSerializer.Clone(Donskoy.Profile.Source);
        profile.Categories.Add("OST_Walls");
        var model = Exp.Model("ВК", [Exp.Pipe("В1")]);
        KitValidator.Validate(profile, [model], null).ShouldContain(i => i.Message.Contains("OST_Walls") && i.Message.Contains("выгруз"));
    }

    [Test]
    public void Validate_CodeNotInClassifier()
    {
        var classifier = Donskoy.Profile.Source.Positions.Where(p => p.Code != "04.05.01.01.01" && p.Code != "999").ToList();
        var issues = KitValidator.Validate(Donskoy.Profile.Source, null, classifier);
        issues.ShouldContain(i => i.Message.Contains("04.05.01.01.01"));
        issues.ShouldNotContain(i => i.Message.Contains("«999»")); // служебная позиция «Не учитывать»
    }

    [Test]
    public void Summary_LargeExport_Streams()
    {
        var elements = Enumerable.Range(0, 60000).Select(i => Exp.Pipe("Т" + (i % 7), "Труба " + (i % 300), "ОВ" + (i % 5))).ToList();
        var model = Exp.Model("Большая", elements);
        var watch = System.Diagnostics.Stopwatch.StartNew();
        SummaryBuilder.Build([model]).Groups.Count.ShouldBe(300);
        RunReport.Build(Donskoy.Profile, [model], new Dictionary<string, string> { ["Большая"] = "heating" }).Models.Single().Total.ShouldBe(60000);
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(30));
    }
}
