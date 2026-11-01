using System.IO;

namespace Collisions.Core;

/// <summary>Имена файлов моделей Revit.</summary>
public static class ModelNames
{
    /// <summary>
    ///     Имя центральной модели по имени локальной копии: Revit сохраняет копию как <c>{имя}_{пользователь}.rvt</c>.
    ///     Убирается только хвост <c>_{пользователь}</c> (без учёта регистра); остальное имя не трогается.
    /// </summary>
    public static string CentralName(string fileName, string windowsUser)
    {
        var extension = Path.GetExtension(fileName);
        var name = fileName.Substring(0, fileName.Length - extension.Length);
        var suffix = "_" + windowsUser.Trim();
        if (windowsUser.Trim().Length == 0 || name.Length <= suffix.Length || !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) return fileName;
        return name.Substring(0, name.Length - suffix.Length) + extension;
    }
}
