using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Движок на маленьком профиле, собранном прямо в тесте.</summary>
public class ClassificationEngineTests
{
    private static ResultDef T(string type) => new() { Type = type };
    private static ResultDef D(string discipline) => new() { Discipline = discipline };

    private static ClassifierProfile Profile()
    {
        var profile = new ClassifierProfile
        {
            Outputs = [new OutputField { Field = "code", Parameter = "Код" }],
            Texts =
            {
                ["name"] = new TextSourceDef { Parts = ["instance:Имя"], Lowercase = true },
                ["system"] = new TextSourceDef { Parts = ["systems"], Normalize = true }
            },
            Types =
            [
                new KeyTitle { Key = "pipe", Title = "Трубы" }, new KeyTitle { Key = "armature", Title = "Арматура" },
                new KeyTitle { Key = "equipment", Title = "Оборудование" }, new KeyTitle { Key = "insulation", Title = "Изоляция" },
                new KeyTitle { Key = "fire", Title = "Огнезащита" }, new KeyTitle { Key = "kip", Title = "КИП" },
                new KeyTitle { Key = "cabinet", Title = "Шкаф" }, new KeyTitle { Key = "ductGroup", Title = "Воздуховоды" },
                new KeyTitle { Key = "pipeGroup", Title = "Трубы-группа" }
            ],
            Disciplines = [new KeyTitle { Key = "water", Title = "Водопровод" }, new KeyTitle { Key = "heat", Title = "Отопление" }],
            Positions =
            [
                new Position { Code = "W.ARM", Description = "Арматура" }, new Position { Code = "W.EQ", Description = "Оборудование" },
                new Position { Code = "W.PIPE", Description = "Трубы" }, new Position { Code = "ANY.KIP", Description = "КИП" },
                new Position { Code = "H.EQ", Description = "Оборудование отопления" }, new Position { Code = "999", Description = "-" },
                new Position { Code = "W.DG", Description = "Возд" }
            ],
            Dictionaries =
            {
                ["types"] = new DictionaryDef
                {
                    Mode = "earliest",
                    Entries =
                    [
                        new DictionaryEntryDef { Result = T("armature"), Patterns = ["клапан"] },
                        new DictionaryEntryDef { Result = T("pipe"), Patterns = ["труба"] },
                        new DictionaryEntryDef { Result = T("insulation"), Patterns = ["изоляц"] },
                        new DictionaryEntryDef { Result = T("kip"), Patterns = ["манометр"] },
                        new DictionaryEntryDef { Result = new ResultDef { Pipe = T("pipeGroup"), Air = T("ductGroup") }, Patterns = ["кронштейн"] }
                    ]
                },
                ["systems"] = new DictionaryDef
                {
                    Entries = [new DictionaryEntryDef { Result = D("heat"), Patterns = ["#Т1"] }, new DictionaryEntryDef { Result = D("water"), Patterns = ["#В1"] }]
                }
            },
            RuleSets =
            {
                ["types"] =
                [
                    new RuleDef
                    {
                        Title = "Вложенный в шкаф", Steps = [new StepDef { Inherit = "parent", OnlyIf = ["cabinet"] }], Fallthrough = true
                    },
                    new RuleDef
                    {
                        Title = "Шкафы", When = [new ConditionDef { Category = ["OST_Casework"] }], Default = T("cabinet")
                    },
                    new RuleDef
                    {
                        Title = "Оборудование",
                        When = [new ConditionDef { Category = ["OST_MechanicalEquipment"] }],
                        Steps =
                        [
                            new StepDef
                            {
                                Dictionary = "types", Source = "name",
                                Refine = [new RefineDef { When = [new ConditionDef { Type = ["insulation"] }, new ConditionDef { Source = "$source", Matches = "ei" }], To = T("fire") }]
                            }
                        ],
                        Allow = new AllowDef { Values = ["armature", "kip", "fire"], Otherwise = "equipment" },
                        Default = T("equipment")
                    },
                    new RuleDef { Title = "По имени", Steps = [new StepDef { Dictionary = "types", Source = "name" }], Fallthrough = true },
                    new RuleDef { Title = "Арматура труб", When = [new ConditionDef { Category = ["OST_PipeAccessory"] }], Default = T("armature") }
                ]
            },
            Tables =
            {
                ["main"] = new TableDef
                {
                    Rows =
                    [
                        new TableRow { Discipline = "water", Type = "armature", Position = "W.ARM" },
                        new TableRow { Discipline = "water", Type = "equipment", Position = "W.EQ" },
                        new TableRow { Discipline = "water", Type = "pipe", Position = "W.PIPE" },
                        new TableRow { Discipline = "water", Type = "ductGroup", Position = "W.DG" },
                        new TableRow { Discipline = "heat", Type = "equipment", Position = "H.EQ" },
                        new TableRow { Discipline = "*", Type = "kip", Position = "ANY.KIP" }
                    ],
                    Fallbacks = { ["fire"] = ["insulation", "equipment"], ["insulation"] = ["pipe"], ["cabinet"] = ["armature"] }
                }
            },
            Modes =
            [
                new ModeDef
                {
                    Key = "auto", Title = "Авто", TypeRules = "types", Table = "main", DefaultDiscipline = "water",
                    DisciplineRules =
                    [
                        new RuleDef { Title = "Изоляция", When = [new ConditionDef { Category = ["OST_PipeInsulations"] }], Steps = [new StepDef { Inherit = "host" }], Fallthrough = true },
                        new RuleDef { Title = "Система", Steps = [new StepDef { Dictionary = "systems", Source = "system" }, new StepDef { Inherit = "parent" }], Fallthrough = true }
                    ]
                },
                new ModeDef { Key = "vent", Title = "Вент", TypeRules = "types", Table = "main", DefaultDiscipline = "water", UnconnectedAsAir = true }
            ],
            Exceptions =
            [
                new RuleDef
                {
                    Title = "Не учитывать", When = [new ConditionDef { Source = "name", Matches = "не учитыв" }],
                    Default = new ResultDef { Position = "999" }
                },
                new RuleDef
                {
                    Title = "Вручную", When = [new ConditionDef { Source = "name", Matches = "вручную" }],
                    Default = new ResultDef { Skip = "по заданию" }
                }
            ]
        };
        return profile;
    }

    private static ClassificationEngine Engine(string mode = "auto", ClassifierProfile? profile = null)
    {
        var (compiled, issues) = ProfileCompiler.Compile(profile ?? Profile());
        issues.ShouldBeEmpty();
        return new ClassificationEngine(compiled!, mode);
    }

    private static FakeFacts F(string category, string name = "", string system = "", NetworkDomain domain = NetworkDomain.Pipe) =>
        new() { Category = category, Instance = { ["Имя"] = name }, SystemText = system, Domain = domain };

    [Test]
    public void Exception_PositionWins()
    {
        var result = Engine().Classify(F("OST_PipeAccessory", "Клапан НЕ УЧИТЫВАТЬ"));
        result.Status.ShouldBe(EngineStatus.Classified);
        result.Position!.Code.ShouldBe("999");
    }

    [Test]
    public void Exception_Skip()
    {
        var result = Engine().Classify(F("OST_PipeAccessory", "клапан вручную"));
        result.Status.ShouldBe(EngineStatus.Skipped);
        result.Reason.ShouldBe("по заданию");
    }

    [Test]
    public void Type_FirstStepWins_AndTableExact()
    {
        var result = Engine().Classify(F("OST_PipeAccessory", "Клапан", "В1"));
        result.Type.ShouldBe("armature");
        result.Discipline.ShouldBe("water");
        result.Position!.Code.ShouldBe("W.ARM");
    }

    [Test]
    public void Type_AllowReplace()
    {
        Engine().Classify(F("OST_MechanicalEquipment", "труба гибкая")).Type.ShouldBe("equipment");
        Engine().Classify(F("OST_MechanicalEquipment", "манометр")).Type.ShouldBe("kip");
    }

    [Test]
    public void StepRefine_ThenRuleAllow_Order()
    {
        // изоляция → (уточнение шага) огнезащита → разрешена правилом оборудования.
        Engine().Classify(F("OST_MechanicalEquipment", "изоляция ei60")).Type.ShouldBe("fire");
        // изоляция без EI не разрешена → оборудование.
        Engine().Classify(F("OST_MechanicalEquipment", "изоляция")).Type.ShouldBe("equipment");
    }

    [Test]
    public void Default_WhenStepsGiveNothing()
    {
        Engine().Classify(F("OST_MechanicalEquipment", "насос")).Type.ShouldBe("equipment");
    }

    [Test]
    public void Fallthrough_NextRule()
    {
        Engine().Classify(F("OST_PipeAccessory", "задвижка")).Type.ShouldBe("armature");
        var none = Engine().Classify(F("OST_GenericModel", "задвижка"));
        none.Status.ShouldBe(EngineStatus.Unclassified);
        none.Reason.ShouldBe("не определён тип элемента");
    }

    [Test]
    public void Inherit_ParentWithOnlyIf()
    {
        var cabinet = F("OST_Casework", "шкаф");
        var nested = F("OST_PipeAccessory", "манометр");
        nested.Parent = cabinet;
        Engine().Classify(nested).Type.ShouldBe("cabinet");

        var plainParent = F("OST_PipeAccessory", "клапан");
        var child = F("OST_GenericModel", "манометр");
        child.Parent = plainParent;
        Engine().Classify(child).Type.ShouldBe("kip");
    }

    [Test]
    public void Inherit_HostDiscipline()
    {
        var pipe = F("OST_PipeCurves", "труба", "Т1");
        var insulation = F("OST_PipeInsulations", "изоляция");
        insulation.Host = pipe;
        var result = Engine().Classify(insulation);
        result.Discipline.ShouldBe("heat");
    }

    [Test]
    public void Inherit_PropagatesSkip()
    {
        var profile = Profile();
        profile.Modes[0].DisciplineRules.Insert(0, new RuleDef
        {
            When = [new ConditionDef { Source = "name", Matches = "скз" }], Default = new ResultDef { Skip = "вручную {match}" }
        });
        var parent = F("OST_PipeAccessory", "клапан скз");
        var child = F("OST_PipeAccessory", "клапан");
        child.Parent = parent;

        var result = Engine(profile: profile).Classify(child);
        result.Status.ShouldBe(EngineStatus.Skipped);
    }

    [Test]
    public void Inherit_Cycle_NoStackOverflow()
    {
        var a = F("OST_PipeInsulations", "изоляция");
        var b = F("OST_PipeInsulations", "изоляция");
        a.Host = b;
        b.Host = a;
        a.Parent = a;

        var result = Engine().Classify(a);
        result.Discipline.ShouldBe("water"); // раздел по умолчанию
    }

    [Test]
    public void UnconnectedAsAir()
    {
        Engine("vent").Classify(F("OST_GenericModel", "кронштейн", domain: NetworkDomain.Other)).Type.ShouldBe("ductGroup");
        Engine("auto").Classify(F("OST_GenericModel", "кронштейн", domain: NetworkDomain.Other)).Type.ShouldBe("pipeGroup");
    }

    [Test]
    public void Table_ExactThenFallbackThenStar()
    {
        Engine().Classify(F("OST_GenericModel", "манометр", "Т1")).Position!.Code.ShouldBe("ANY.KIP");
        // огнезащита → нет строки → замены: insulation (нет) → equipment (есть)
        Engine().Classify(F("OST_MechanicalEquipment", "изоляция ei", "Т1")).Position!.Code.ShouldBe("H.EQ");
    }

    [Test]
    public void Fallback_NotTransitive()
    {
        // fire → insulation → pipe была бы транзитивной; в отоплении нет ни insulation, ни pipe → только equipment.
        // cabinet → armature: в отоплении арматуры нет, дальше замены armature не берутся.
        var result = Engine().Classify(F("OST_Casework", "шкаф", "Т1"));
        result.Status.ShouldBe(EngineStatus.Unclassified);
        result.Reason!.ShouldContain("Отопление");
        result.Reason!.ShouldContain("Шкаф");
    }

    [Test]
    public void Trace_HasEveryStage()
    {
        var result = Engine().Classify(F("OST_PipeAccessory", "Клапан", "В1"));
        result.Trace.Select(t => t.Stage).Distinct().ShouldBe(["Тип", "Раздел", "Позиция"]);
        string.Join("\n", result.Trace.Select(t => t.Text)).ShouldContain("клапан");
    }
}

public class ClassificationEngineTimeoutTests
{
    [Test]
    public void PatternTimeout_MakesElementUnclassified_AndNextElementWorks()
    {
        var profile = new ClassifierProfile
        {
            Outputs = [new OutputField { Field = "code", Parameter = "Код" }],
            Texts = { ["name"] = new TextSourceDef { Parts = ["instance:Имя"] } },
            Types = [new KeyTitle { Key = "x", Title = "Икс" }],
            Disciplines = [new KeyTitle { Key = "d", Title = "Д" }],
            Positions = [new Position { Code = "01" }],
            Dictionaries = { ["slow"] = new DictionaryDef { Entries = [new DictionaryEntryDef { Result = new ResultDef { Type = "x" }, Patterns = ["re:^(a+)+$", "ok"] }] } },
            RuleSets = { ["t"] = [new RuleDef { Steps = [new StepDef { Dictionary = "slow", Source = "name" }] }] },
            Tables = { ["m"] = new TableDef { Rows = [new TableRow { Discipline = "d", Type = "x", Position = "01" }] } },
            Modes = [new ModeDef { Key = "m", Title = "М", TypeRules = "t", Table = "m", DefaultDiscipline = "d" }]
        };
        var engine = new ClassificationEngine(ProfileCompiler.Compile(profile).Profile!, "m");

        var slow = engine.Classify(new FakeFacts { Instance = { ["Имя"] = new string('a', 40) + "!" } });
        slow.Status.ShouldBe(EngineStatus.Unclassified);
        slow.Reason!.ShouldContain("re:^(a+)+$");

        // Сломанный шаблон помечен на весь прогон: следующий элемент не ждёт таймаут заново.
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var again = engine.Classify(new FakeFacts { Instance = { ["Имя"] = new string('a', 41) + "!" } });
        watch.Elapsed.ShouldBeLessThan(TimeSpan.FromSeconds(1));
        again.Reason!.ShouldContain("re:^(a+)+$");

        engine.Classify(new FakeFacts { Instance = { ["Имя"] = "ok" } }).Status.ShouldBe(EngineStatus.Unclassified);
    }
}
