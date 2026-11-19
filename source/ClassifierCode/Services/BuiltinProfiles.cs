using System.IO;
using System.Reflection;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Services;

/// <summary>Профили, вшитые в сборку (Profiles/Builtin/*.classifier.json). Только для чтения — правят их копию.</summary>
public static class BuiltinProfiles
{
    private static IReadOnlyList<ClassifierProfile>? _profiles;

    public static IReadOnlyList<ClassifierProfile> Load()
    {
        if (_profiles != null) return _profiles;

        var assembly = typeof(BuiltinProfiles).Assembly;
        var profiles = new List<ClassifierProfile>();
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.EndsWith(".classifier.json", StringComparison.OrdinalIgnoreCase)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream);
            profiles.Add(ProfileSerializer.Read(reader.ReadToEnd()));
        }

        _profiles = profiles;
        return profiles;
    }
}
