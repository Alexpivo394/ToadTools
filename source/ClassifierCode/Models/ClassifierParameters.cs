namespace ClassifierCode.Models;

/// <summary>Параметры ADSK, в которые записывается классификатор.</summary>
public static class ClassifierParameters
{
    public const string Code = "ADSK_Код по классификатору";
    public const string Description = "ADSK_Описание по классификатору";

    public static IReadOnlyList<string> All { get; } = [Code, Description];
}
