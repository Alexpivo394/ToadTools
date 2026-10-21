using ParamChecker.Core.Checking;
using ParamChecker.Core.Profiles;

namespace ParamChecker.Core;

/// <summary>Проверка профилей перед запуском — все ошибки сразу, а не по одной посреди долгой проверки.</summary>
public static class RunValidator
{
    /// <param name="profiles">Отмеченные к запуску профили.</param>
    /// <param name="needsModels">Профилю нужны модели; при проверке открытой модели — нет.</param>
    public static IReadOnlyList<string> Problems(IReadOnlyList<CheckProfile> profiles, bool needsModels = true)
    {
        if (profiles.Count == 0) return ["Отметьте хотя бы один профиль."];

        var problems = new List<string>();
        foreach (var profile in profiles)
        {
            var name = $"«{Display(profile.Name, "без имени")}»";
            if (needsModels)
            {
                if (profile.Models.Count == 0) problems.Add($"{name}: нет моделей.");
                if (profile.Models.Any(model => model.Path.Trim().Length == 0)) problems.Add($"{name}: у модели не указан путь.");

                foreach (var duplicate in profile.Models.Where(model => model.Path.Trim().Length > 0)
                             .GroupBy(model => ModelNames.Key(model.Path), StringComparer.OrdinalIgnoreCase).Where(group => group.Count() > 1))
                    problems.Add($"{name}: модель {ModelNames.FromPath(duplicate.Key)} добавлена несколько раз.");
            }

            if (profile.Rules.Count == 0) problems.Add($"{name}: нет правил.");

            for (var i = 0; i < profile.Rules.Count; i++)
            {
                var rule = profile.Rules[i];
                var ruleName = $"{name}, правило «{Display(rule.Title, $"№{i + 1}")}»";
                if (!rule.Parameters.Any(p => !string.IsNullOrWhiteSpace(p))) problems.Add($"{ruleName}: не выбраны параметры для проверки.");

                var filter = rule.Filter;
                if (filter.Mode is SelectionMode.CategoriesOnly or SelectionMode.CategoriesAndParameters && filter.Categories.Count == 0)
                    problems.Add($"{ruleName}: не выбраны категории.");
                if (filter.Mode == SelectionMode.ParametersOnly && !ConditionEvaluator.HasConditions(filter))
                    problems.Add($"{ruleName}: отбор только по условиям, но условий нет.");
                if (filter.Mode == SelectionMode.CategoriesOrParameters && filter.Categories.Count == 0 && !ConditionEvaluator.HasConditions(filter))
                    problems.Add($"{ruleName}: нет ни категорий, ни условий.");
            }
        }

        return problems;
    }

    private static string Display(string value, string fallback) => value.Trim().Length == 0 ? fallback : value.Trim();
}
