using System.Text;
using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Engine.Import;

/// <summary>Классификатор из таблицы (лист Excel или вставка из буфера): строки → позиции.</summary>
public static class PositionsImport
{
    public static (List<Position> Positions, List<string> Skipped) FromRows(IReadOnlyList<string[]> rows, int codeColumn, int descriptionColumn,
        int firstRow, IReadOnlyDictionary<string, int> extraColumns)
    {
        var positions = new List<Position>();
        var skipped = new List<string>();
        var seen = new HashSet<string>();
        for (var r = firstRow; r < rows.Count; r++)
        {
            var row = rows[r];
            var code = Cell(row, codeColumn);
            if (code.Length == 0)
            {
                skipped.Add($"строка {r + 1}: пустой код");
                continue;
            }

            if (!seen.Add(code))
            {
                skipped.Add($"строка {r + 1}: код {code} повторяется");
                continue;
            }

            var position = new Position { Code = code, Description = Cell(row, descriptionColumn) };
            foreach (var extra in extraColumns) position.Extra[extra.Key] = Cell(row, extra.Value);
            positions.Add(position);
        }

        return (positions, skipped);
    }

    private static string Cell(string[] row, int column) => column >= 0 && column < row.Length ? (row[column] ?? string.Empty).Trim() : string.Empty;

    /// <summary>Колонки кода и описания и первая строка данных по заголовку; без заголовка — 0, 1, 0.</summary>
    public static (int Code, int Description, int FirstRow) Guess(IReadOnlyList<string[]> rows)
    {
        if (rows.Count == 0) return (0, 1, 0);
        var header = rows[0].Select(c => (c ?? string.Empty).ToLowerInvariant()).ToList();
        var code = header.FindIndex(c => c.Contains("код"));
        var description = header.FindIndex(c => c.Contains("описан") || c.Contains("наимен"));
        if (code < 0 && description < 0) return (0, 1, 0);
        if (code < 0) code = description == 0 ? 1 : 0;
        if (description < 0) description = code == 0 ? 1 : 0;
        return (code, description, 1);
    }

    /// <summary>Текст из буфера (Excel копирует TSV): табуляция, перевод строки, ячейки в кавычках.</summary>
    public static List<string[]> ParseTsv(string? text)
    {
        var rows = new List<string[]>();
        if (string.IsNullOrEmpty(text)) return rows;

        var row = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text!.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    cell.Append('"');
                    i++;
                }
                else if (c == '"') quoted = false;
                else cell.Append(c);
                continue;
            }

            switch (c)
            {
                case '"' when cell.Length == 0:
                    quoted = true;
                    break;
                case '\t':
                    row.Add(cell.ToString());
                    cell.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    row.Add(cell.ToString());
                    cell.Clear();
                    rows.Add(row.ToArray());
                    row.Clear();
                    break;
                default:
                    cell.Append(c);
                    break;
            }
        }

        if (cell.Length > 0 || row.Count > 0)
        {
            row.Add(cell.ToString());
            rows.Add(row.ToArray());
        }

        return rows;
    }
}
