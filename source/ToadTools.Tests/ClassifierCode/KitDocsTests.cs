using System.Collections;
using System.Reflection;
using System.Text.RegularExpressions;
using ClassifierCode.Check;
using ClassifierCode.Engine.Profile;
using Newtonsoft.Json.Linq;
using Shouldly;

namespace ToadTools.Tests.ClassifierCode;

/// <summary>Тексты набора агента не расходятся с моделью профиля и командами проверялки.</summary>
public class KitDocsTests
{
    private static string Kit(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "ClassifierCode", "AiKit", name);

    private static JObject Schema => JObject.Parse(File.ReadAllText(Kit("profile.schema.json")));

    private static string CamelCase(string name) => char.ToLowerInvariant(name[0]) + name.Substring(1);

    private static Type? Element(Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Dictionary<,>)) return Element(type.GetGenericArguments()[1]);
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return Element(type.GetGenericArguments()[0]);
        return type.Namespace == typeof(ClassifierProfile).Namespace && type.IsClass ? type : null;
    }

    /// <summary>Все классы профиля, достижимые из <see cref="ClassifierProfile" />.</summary>
    private static List<Type> ProfileTypes()
    {
        var found = new List<Type>();
        var queue = new Queue<Type>([typeof(ClassifierProfile)]);
        while (queue.Count > 0)
        {
            var type = queue.Dequeue();
            if (found.Contains(type)) continue;
            found.Add(type);
            foreach (var property in Properties(type))
                if (Element(property.PropertyType) is { } nested) queue.Enqueue(nested);
        }

        return found;
    }

    private static IEnumerable<PropertyInfo> Properties(Type type) =>
        type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite);

    private static JObject Definition(JObject schema, Type type) =>
        type == typeof(ClassifierProfile) ? schema : (JObject)schema["definitions"]![type.Name]!;

    [Test]
    public void Schema_CoversAllProfileProperties()
    {
        var schema = Schema;
        schema["$schema"]!.ToString().ShouldContain("draft-07");
        foreach (var type in ProfileTypes())
        {
            var definition = Definition(schema, type);
            var expected = Properties(type).Select(p => CamelCase(p.Name)).OrderBy(n => n).ToList();
            var actual = ((JObject)definition["properties"]!).Properties().Select(p => p.Name).OrderBy(n => n).ToList();
            actual.ShouldBe(expected, $"схема {type.Name}");
            definition["additionalProperties"]!.Value<bool>().ShouldBeFalse(type.Name);
        }
    }

    [Test]
    public void Schema_AcceptsBuiltinKeys()
    {
        var schema = Schema;
        var builtin = JObject.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "ClassifierCode", "pioneer-donskoy.classifier.json")));
        var unknown = new List<string>();
        Walk(schema, schema, builtin, "", unknown);
        unknown.ShouldBeEmpty();
    }

    /// <summary>Каждый ключ встроенного профиля описан в схеме (по $ref, items, additionalProperties).</summary>
    private static void Walk(JObject root, JObject node, JToken value, string path, List<string> unknown)
    {
        if (node["$ref"] is { } reference)
            node = (JObject)root.SelectToken(reference.ToString().TrimStart('#', '/').Replace('/', '.'))!;
        switch (value)
        {
            case JObject obj when node["properties"] is JObject properties:
                foreach (var property in obj.Properties())
                {
                    if (properties[property.Name] is JObject sub) Walk(root, sub, property.Value, path + "." + property.Name, unknown);
                    else unknown.Add(path + "." + property.Name);
                }

                break;
            case JObject obj when node["additionalProperties"] is JObject map:
                foreach (var property in obj.Properties()) Walk(root, map, property.Value, path + "." + property.Name, unknown);
                break;
            case JArray array when node["items"] is JObject items:
                foreach (var item in array) Walk(root, items, item, path + "[]", unknown);
                break;
        }
    }

    [Test]
    public void Format_MentionsEveryProperty()
    {
        var format = File.ReadAllText(Kit("FORMAT.md"));
        var missing = ProfileTypes().SelectMany(Properties).Select(p => CamelCase(p.Name)).Distinct()
            .Where(name => !format.Contains("`" + name + "`")).ToList();
        missing.ShouldBeEmpty();
    }

    [Test]
    public void Agents_CommandsExistInCli()
    {
        var agents = File.ReadAllText(Kit("AGENTS.md"));
        var used = Regex.Matches(agents, @"check\.exe (\w+)").Cast<Match>().Select(m => m.Groups[1].Value).Distinct().ToList();
        used.ShouldNotBeEmpty();
        used.Where(c => !CheckCli.Commands.Contains(c)).ShouldBeEmpty();
        CheckCli.Commands.Where(c => !used.Contains(c)).ShouldBeEmpty("каждая команда проверялки описана агенту");
        agents.ShouldContain("REVIEW.md");
        agents.ShouldContain("ОШИБКИ_В_МОДЕЛЯХ.tsv");
    }

    [Test]
    public void Readme_NamesFolders()
    {
        var readme = File.ReadAllText(Kit("README_КООРДИНАТОРУ.txt"));
        readme.ShouldContain("REVIEW.md");
        readme.ShouldContain("profile.classifier.json");
    }
}
