using ClassifierCode.Engine.Running;

namespace ClassifierCode.Engine.Library;

/// <summary>Какой профиль и режим запомнила модель.</summary>
public sealed record ModelBinding(Guid Id, string Name, string Version, string Mode);

/// <summary>Какой профиль предложить при открытии окна.</summary>
public static class ProfileChoice
{
    /// <summary>
    ///     Порядок: профиль, которым заполнялась модель → шаблон имени файла → последний выбранный → встроенный.
    ///     Если профиль модели лежит в нескольких папках, берётся копия той же версии, иначе — старшая версия.
    ///     ModeKey — режим из привязки, если он есть в профиле; ModeMissing — режим из привязки пропал, и
    ///     подставлять другой нельзя (иначе элементы молча получат коды чужого раздела).
    /// </summary>
    public static (ProfileEntry Entry, string? ModeKey, string? Warning, bool ModeMissing) Resolve(IReadOnlyList<ProfileEntry> entries,
        ModelBinding? binding, string documentTitle, Guid? lastProfileId)
    {
        var usable = entries.Where(e => e.Profile != null).ToList();

        if (binding != null)
        {
            var candidates = usable.Where(e => e.Profile!.Id == binding.Id).ToList();
            if (candidates.Count > 0)
            {
                var bound = candidates.FirstOrDefault(e => e.Profile!.Version == binding.Version) ??
                            candidates.OrderByDescending(e => VersionKey(e.Profile!.Version)).First();
                var warnings = new List<string>();
                if (bound.Profile!.Version != binding.Version)
                    warnings.Add($"Модель заполнялась версией {binding.Version} профиля «{binding.Name}», сейчас выбрана {bound.Profile.Version}.");

                var modeFound = bound.Profile.Modes.Any(m => m.Key == binding.Mode);
                if (!modeFound)
                    warnings.Add($"Модель заполнялась разделом «{binding.Mode}», в этой версии профиля его нет — выберите раздел вручную.");
                return (bound, modeFound ? binding.Mode : null, warnings.Count > 0 ? string.Join(" ", warnings) : null, !modeFound);
            }
        }

        var warning = binding != null ? $"Модель заполнялась профилем «{binding.Name}» {binding.Version}, его нет в библиотеке — выбран другой." : null;
        var byName = usable.FirstOrDefault(e => ModeDetector.MatchesProfile(e.Profile!, documentTitle));
        var last = usable.FirstOrDefault(e => e.Profile!.Id == lastProfileId);
        var builtin = usable.FirstOrDefault(e => e.Origin == ProfileOrigin.Builtin) ?? usable.First();
        return (byName ?? last ?? builtin, null, warning, false);
    }

    /// <summary>«1.10» старше «1.9»; нечисловая версия — в конец.</summary>
    private static Version VersionKey(string version) =>
        System.Version.TryParse(version.Contains(".") ? version : version + ".0", out var parsed) ? parsed : new Version(0, 0);
}
