namespace WorkingSet.Models;

/// <summary>Создаёт рабочие наборы, которых ещё нет в модели.</summary>
public static class WorksetCreator
{
    /// <returns>Сколько рабочих наборов создано (уже существующие пропускаются).</returns>
    public static int Create(Document doc, IEnumerable<string> worksetNames)
    {
        var created = 0;
        using var transaction = new Transaction(doc, "Создание рабочих наборов");
        transaction.Start();

        foreach (var worksetName in worksetNames)
        {
            if (!WorksetTable.IsWorksetNameUnique(doc, worksetName)) continue;

            Workset.Create(doc, worksetName);
            created++;
        }

        transaction.Commit();
        return created;
    }
}
