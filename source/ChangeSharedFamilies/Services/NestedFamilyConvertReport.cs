namespace ChangeSharedFamilies.Services;

public class NestedFamilyConvertReport
{
    public int ConvertedFamiliesCount { get; set; }

    public List<string> ConvertedFamilyNames { get; } = new List<string>();

    public List<string> Errors { get; } = new List<string>();

    /// <summary>Пользователь прервал замену — часть семейств осталась общими.</summary>
    public bool Cancelled { get; set; }
}
