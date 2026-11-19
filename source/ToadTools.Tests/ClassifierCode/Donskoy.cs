using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;
using ClassifierCode.Engine.Running;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Встроенный профиль «Пионер. Донской» из TestData и короткие помощники для тестов паритета.</summary>
public static class Donskoy
{
    private static readonly Lazy<CompiledProfile> Compiled = new(() =>
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "ClassifierCode", "pioneer-donskoy.classifier.json");
        var (profile, issues) = ProfileCompiler.Compile(ProfileSerializer.Read(File.ReadAllText(path)));
        return profile ?? throw new InvalidOperationException("Профиль Донского не собрался:\n" + string.Join("\n", issues));
    });

    public static CompiledProfile Profile => Compiled.Value;

    public static ClassificationEngine Engine(string mode) => new(Profile, mode);

    /// <summary>Код позиции, "skip" или "none".</summary>
    public static string Code(string mode, IElementFacts facts) => Code(Engine(mode).Classify(facts));

    public static string Code(EngineResult result) => result.Status switch
    {
        EngineStatus.Classified => result.Position!.Code,
        EngineStatus.Skipped => "skip",
        _ => "none"
    };

    private static readonly HashSet<string> PipeCategories =
        ["OST_PipeCurves", "OST_FlexPipeCurves", "OST_PipeFitting", "OST_PipeAccessory", "OST_PipeInsulations", "OST_Sprinklers", "OST_PlumbingFixtures"];

    private static readonly HashSet<string> AirCategories =
        ["OST_DuctCurves", "OST_FlexDuctCurves", "OST_DuctFitting", "OST_DuctAccessory", "OST_DuctTerminal", "OST_DuctInsulations", "OST_DuctLinings"];

    /// <summary>Элемент: категория, ADSK_Наименование, семейство, система. Домен — по категории, как старый GetDomain.</summary>
    public static FakeFacts F(string category, string name = "", string family = "", string system = "", NetworkDomain? domain = null) => new()
    {
        Category = category,
        Instance = { ["ADSK_Наименование"] = name },
        FamilyName = family,
        SystemText = system,
        Domain = domain ?? (PipeCategories.Contains(category) ? NetworkDomain.Pipe : AirCategories.Contains(category) ? NetworkDomain.Air : NetworkDomain.Other)
    };

    public static string Camel(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);
}
