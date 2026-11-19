using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Running;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

public class ExportFactsTests
{
    /// <summary>Выгрузка из FakeFacts (с родителями и хостами) — чтобы сравнить движок на двух представлениях.</summary>
    public static (ExportModel Model, long RootId) FromFake(FakeFacts root, string modelName = "Модель")
    {
        var model = new ExportModel { Model = modelName, Categories = ["OST_PipeCurves"] };
        var ids = new Dictionary<FakeFacts, long>(); // FakeFacts не переопределяет Equals — сравнение по ссылке

        long Add(FakeFacts f)
        {
            if (ids.TryGetValue(f, out var known)) return known;
            var id = ids.Count + 1;
            ids[f] = id;
            var typeId = f.HasType ? "t" + id : null;
            if (typeId != null)
                model.Types[typeId] = new ExportType
                {
                    Family = f.FamilyName, Name = f.TypeName, Comments = f.TypeComments,
                    Parameters = f.Type.Select(p => new ExportParameter { Name = p.Key, Value = p.Value, IsString = true }).ToList()
                };
            var element = new ExportElement
            {
                Id = id, Category = f.Category, TypeId = typeId, Name = f.InstanceName, Comments = f.InstanceComments,
                SystemText = f.SystemText, Domain = ExportFacts.DomainKey(f.Domain), Workset = f.Workset,
                Parameters = f.Instance.Select(p => new ExportParameter { Name = p.Key, Value = p.Value, IsString = true }).ToList()
            };
            model.Elements.Add(element);
            if (f.Parent is FakeFacts parent) element.ParentId = Add(parent);
            if (f.Host is FakeFacts host) element.HostId = Add(host);
            return id;
        }

        return (model, Add(root));
    }

    [Test]
    public void RoundTrip()
    {
        using var temp = new TempFolder();
        var (model, _) = FromFake(new FakeFacts { Category = "OST_PipeCurves", SystemText = "В1", Instance = { ["ADSK_Наименование"] = "Труба" } });
        model.Binding = new global::ClassifierCode.Engine.Library.ModelBinding(Guid.NewGuid(), "Донской", "1.0", "water");
        var path = temp.File("m.export.json");
        ExportWriter.Write(model, path);
        var again = ExportReader.Read(path);
        again.Elements.Single().Parameters.Single().Value.ShouldBe("Труба");
        again.Binding!.Mode.ShouldBe("water");
        again.Categories.ShouldBe(["OST_PipeCurves"]);
    }

    /// <summary>Параметров у элемента десятки — пишутся массивом, без имён полей: выгрузка вдвое меньше.</summary>
    [Test]
    public void Parameters_WrittenCompact()
    {
        using var temp = new TempFolder();
        var model = new ExportModel
        {
            Model = "ШФТ-ВК",
            Elements =
            [
                new ExportElement
                {
                    Id = 1, Category = "OST_PipeCurves",
                    Parameters =
                    [
                        new ExportParameter { Name = "ADSK_Наименование", Value = "Труба \"ПЭ\"", IsString = true },
                        new ExportParameter { Name = "Длина", Value = "1500" }
                    ]
                }
            ]
        };
        var path = temp.File("m.export.json");
        ExportWriter.Write(model, path);
        var text = File.ReadAllText(path);
        text.ShouldContain("[\"ADSK_Наименование\",\"Труба \\\"ПЭ\\\"\",1]");
        text.ShouldContain("[\"Длина\",\"1500\"]");

        var parameters = ExportReader.Read(path).Elements.Single().Parameters;
        parameters[0].IsString.ShouldBeTrue();
        parameters[0].Value.ShouldBe("Труба \"ПЭ\"");
        parameters[1].IsString.ShouldBeFalse();
        parameters[1].Name.ShouldBe("Длина");
    }

    [Test]
    public void Parameters_FirstByName_AndOrderKept()
    {
        var model = new ExportModel
        {
            Elements =
            [
                new ExportElement
                {
                    Id = 1, Category = "OST_PipeCurves",
                    Parameters =
                    [
                        new ExportParameter { Name = "ADSK_Наименование", Value = "", IsString = true },
                        new ExportParameter { Name = "Диаметр", Value = "50 мм", IsString = false },
                        new ExportParameter { Name = "ADSK_Наименование", Value = "Второй", IsString = true },
                        new ExportParameter { Name = "Группа", Value = "Отопление", IsString = true }
                    ]
                }
            ]
        };
        var facts = ExportFacts.Load(model).Single();
        facts.InstanceParameter("ADSK_Наименование").ShouldBe(string.Empty);
        facts.InstanceParameter("Диаметр").ShouldBe("50 мм");
        facts.InstanceStringValues().ShouldBe(["Второй", "Отопление"]);
        facts.HasType.ShouldBeFalse();
        facts.FamilyName.ShouldBe(string.Empty);
    }

    [Test]
    public void ParentAndHost_ResolvedById()
    {
        var parent = new FakeFacts { Category = "OST_MechanicalEquipment", FamilyName = "Шкаф" };
        var pipe = new FakeFacts { Category = "OST_PipeCurves", SystemText = "Т12" };
        var child = new FakeFacts { Category = "OST_PipeInsulations", Parent = parent, Host = pipe };
        var (model, root) = FromFake(child);
        var facts = ExportFacts.Load(model).Single(f => f.Id == root);
        facts.Parent!.FamilyName.ShouldBe("Шкаф");
        facts.Host!.SystemText.ShouldBe("Т12");
    }

    public static IEnumerable<Func<(string Mode, FakeFacts Facts, string Expected)>> AllCases() =>
        DonskoyIosParityTests.Cases().Concat(DonskoyEomSsParityTests.Cases());

    [Test]
    [MethodDataSource(nameof(AllCases))]
    public void SameAsFake_ForEngine((string Mode, FakeFacts Facts, string Expected) c)
    {
        var (model, root) = FromFake(c.Facts);
        var fromExport = ExportFacts.Load(model).Single(f => f.Id == root);
        var a = Donskoy.Engine(c.Mode).Classify(c.Facts);
        var b = Donskoy.Engine(c.Mode).Classify(fromExport);
        Donskoy.Code(b).ShouldBe(Donskoy.Code(a));
        b.Type.ShouldBe(a.Type);
        b.Discipline.ShouldBe(a.Discipline);
    }

    [Test]
    public void ExportFileName_Unique()
    {
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        ExportNames.FileName("ШФТ-СОБ-1-ОВВ", taken).ShouldBe("ШФТ-СОБ-1-ОВВ.export.json");
        ExportNames.FileName("ШФТ-СОБ-1-ОВВ", taken).ShouldBe("ШФТ-СОБ-1-ОВВ (2).export.json");
        ExportNames.ModelOf("ШФТ-СОБ-1-ОВВ (2).export.json").ShouldBe("ШФТ-СОБ-1-ОВВ");
        ExportNames.FileName("a:b?", taken).ShouldBe("a_b_.export.json");
    }
}
