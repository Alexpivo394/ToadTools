using ClassifierCode.Engine.Compile;
using ClassifierCode.Engine.Facts;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Kit;

/// <summary>Проверка профиля перед выдачей координатору: ошибки компиляции, выгруженные категории, коды из таблицы заказчика.</summary>
public static class KitValidator
{
    /// <summary>Служебная позиция «Не учитывать» — её нет в таблицах заказчиков, но она допустима.</summary>
    public static readonly IReadOnlyList<string> ServiceCodes = ["999"];

    public static IReadOnlyList<ProfileIssue> Validate(ClassifierProfile profile, IEnumerable<ExportModel>? exports, IReadOnlyList<Position>? classifier)
    {
        var issues = new List<ProfileIssue>(ProfileCompiler.Compile(profile, null).Issues);

        if (exports != null)
        {
            var exported = new HashSet<string>(exports.SelectMany(e => e.Categories));
            if (exported.Count > 0)
            {
                foreach (var category in profile.Categories.Where(c => !exported.Contains(c)))
                    issues.Add(new ProfileIssue("Категории", $"категория «{category}» не выгружалась — проверка на этой выгрузке её не увидит; нужна повторная выгрузка"));
            }
        }

        if (classifier != null)
        {
            var known = new HashSet<string>(classifier.Select(p => p.Code).Concat(ServiceCodes));
            foreach (var position in profile.Positions.Where(p => !known.Contains(p.Code)))
                issues.Add(new ProfileIssue("Классификатор", $"позиции «{position.Code}» нет в таблице заказчика"));
        }

        return issues;
    }
}
