//Command running revit application

using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using CableTrays;
using Nice3point.Revit.Toolkit;

namespace ArticulLotok;

public class ArticulService
{
    private const string NotFound = "ЗНАЧЕНИЕ НЕ НАЙДЕНО";
    private const string ManualFill = "Заполняй сам, ты же этого хотел";

    private const string ParamTypeName = "ADSK_Наименование (по типу)";
    private const string ParamIsp = "ADSK_Исполнение";
    private const string ParamCode = "ADSK_Код изделия";
    private const string ParamName = "ADSK_Наименование";
    private const string ParamCount = "ADSK_Количество";

    //артикулы по ключу "высота x ширина"
    private static readonly Dictionary<string, Dictionary<string, string>> Articuls = new()
    {
        ["Лоток неперфорированный"] = new()
        {
            ["50x50"] = "35020", ["50x100"] = "35022", ["50x150"] = "35023", ["50x200"] = "35024",
            ["50x300"] = "35025", ["50x400"] = "35026", ["50x500"] = "35027", ["50x600"] = "35028",
            ["100x100"] = "35101", ["100x150"] = "35102", ["100x200"] = "35103", ["100x300"] = "35104",
            ["100x400"] = "35105", ["100x500"] = "35106", ["100x600"] = "35107",
        },
        ["Лоток перфорированный"] = new()
        {
            ["50x50"] = "35260", ["50x100"] = "35262", ["50x150"] = "35263", ["50x200"] = "35264",
            ["50x300"] = "35265", ["50x400"] = "35266", ["50x500"] = "35267", ["50x600"] = "35268",
            ["80x80"] = "35301", ["80x100"] = "35302", ["80x150"] = "35303", ["80x200"] = "35304",
            ["80x300"] = "35305", ["80x400"] = "35306", ["80x500"] = "35307", ["80x600"] = "35308",
            ["100x100"] = "35341", ["100x150"] = "35342", ["100x200"] = "35343", ["100x300"] = "35344",
            ["100x400"] = "35345", ["100x500"] = "35346", ["100x600"] = "35347",
        },
        ["Лоток лестничный"] = new()
        {
            ["50x100"] = "LL5010", ["50x200"] = "LL5020", ["50x300"] = "LL5030",
            ["50x400"] = "LL5040", ["50x500"] = "LL5050", ["50x600"] = "LL5060",
            ["80x200"] = "LL8020", ["80x300"] = "LL8030", ["80x400"] = "LL8040",
            ["80x500"] = "LL8050", ["80x600"] = "LL8060",
            ["100x100"] = "LL1010", ["100x200"] = "LL1020", ["100x300"] = "LL1030",
            ["100x400"] = "LL1040", ["100x500"] = "LL1050", ["100x600"] = "LL1060",
        },
        ["Лоток проволочный"] = new()
        {
            ["50x50"] = "FC5005", ["50x100"] = "FC5010", ["50x150"] = "FC5015", ["50x200"] = "FC5020",
            ["50x300"] = "FC5030", ["50x400"] = "FC5040", ["50x500"] = "FC5050", ["50x600"] = "FC5060",
            ["80x80"] = "FC8080", ["80x100"] = "FC8010", ["80x150"] = "FC8015", ["80x200"] = "FC8020",
            ["80x300"] = "FC8030", ["80x400"] = "FC8040", ["80x500"] = "FC8050", ["80x600"] = "FC8060",
            ["100x100"] = "FC1010", ["100x150"] = "FC1015", ["100x200"] = "FC1020", ["100x300"] = "FC1030",
            ["100x400"] = "FC1040", ["100x500"] = "FC1050", ["100x600"] = "FC1060",
        },
    };

    public void Run() => TrayReport.Run("Артикулы лотков", Execute);

    private void Execute()
    {
        var uidoc = RevitContext.ActiveUiDocument!;
        var doc = uidoc.Document;

        var selection = uidoc.Selection
            .GetElementIds()
            .Select(id => doc.GetElement(id))
            .Where(e => e is CableTray)
            .ToList();

        var alllotkirealall = selection.Count == 0
            ? new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_CableTray)
                .WhereElementIsNotElementType()
                .ToList()
            : selection;

        if (alllotkirealall.Count == 0)
        {
            TrayReport.ShowWarning("В модели нет кабельных лотков — заполнять нечего.");
            return;
        }

        var filled = 0;
        var manual = 0;
        var skipped = new List<string>(); //не хватает параметров
        var sizeNotFound = new List<string>(); //нет артикула для такого размера
        var unknownIsp = new List<string>(); //исполнение не 1, 2 или 3
        var unknownType = new List<string>(); //не распознан ADSK_Наименование (по типу)

        using (var t0 = new Transaction(doc, "Артикулы лотков"))
        {
            t0.Start();

            foreach (var lotok in alllotkirealall)
            {
                var ispParam = lotok.LookupParameter(ParamIsp);
                var codeParam = lotok.LookupParameter(ParamCode);
                var nameParam = lotok.LookupParameter(ParamName);
                var countParam = lotok.LookupParameter(ParamCount);

                var missing = new List<string>();
                if (ispParam == null) missing.Add(ParamIsp);
                if (codeParam == null) missing.Add(ParamCode);
                if (nameParam == null) missing.Add(ParamName);
                if (countParam == null) missing.Add(ParamCount);
                if (missing.Count > 0)
                {
                    skipped.Add($"{TrayReport.Describe(lotok)}: нет {string.Join(", ", missing)}");
                    continue;
                }

                var isp = ispParam!.AsInteger();
                var typeName = doc.GetElement(lotok.GetTypeId())?.LookupParameter(ParamTypeName)?.AsString();

                if (isp == 0) //случай ручного заполнения наименования
                {
                    if (typeName != null && Articuls.ContainsKey(typeName))
                    {
                        nameParam!.Set(ManualFill);
                        manual++;
                    }
                }
                else if (typeName != null && Articuls.TryGetValue(typeName, out var table))
                {
                    if (isp is < 1 or > 3)
                        unknownIsp.Add($"{TrayReport.Describe(lotok)}: исполнение {isp}");

                    if (FillArticul(lotok, typeName, table, isp, codeParam!, nameParam!, out var size))
                        filled++;
                    else
                        sizeNotFound.Add($"{TrayReport.Describe(lotok)}: {typeName} {size}");
                }
                else //обнуление наименования и кода изделия
                {
                    codeParam!.Set("");
                    nameParam!.Set("");
                    unknownType.Add($"{TrayReport.Describe(lotok)}: «{typeName ?? "пусто"}»");
                }

                var length = lotok.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble() ?? 0;
#if REVIT2021_OR_GREATER
                var reallength =
                    Math.Round(UnitUtils.ConvertFromInternalUnits(length, UnitTypeId.Millimeters) / 100) / 10;
#else
                var reallength =
                    Math.Round(UnitUtils.ConvertFromInternalUnits(length, DisplayUnitType.DUT_MILLIMETERS) / 100) / 10;
#endif
                countParam!.Set(reallength);
            }

            t0.Commit();
        }

        var report = new TrayReport(
            (selection.Count > 0 ? "Обработаны выделенные лотки" : "Обработаны все лотки модели") +
            $": {alllotkirealall.Count}");
        report.Count("Артикул и наименование заполнены", filled);
        report.Count("Исполнение = 0, наименование заполняется вручную", manual);
        report.Count("Количество (длина) записано", alllotkirealall.Count - skipped.Count);
        report.Warning("Нет артикула для такого размера, в код записано «ЗНАЧЕНИЕ НЕ НАЙДЕНО»", sizeNotFound);
        report.Warning("Неизвестное исполнение (ожидается 1, 2 или 3), артикул записан без суффикса", unknownIsp);
        report.Warning($"Не распознан «{ParamTypeName}», код и наименование очищены", unknownType);
        report.Warning("Пропущены — у лотка нет нужных параметров", skipped);
        report.Show();
    }

    private static bool FillArticul(Element lotok, string typeName, Dictionary<string, string> table, int isp,
        Parameter codeParam, Parameter nameParam, out string size)
    {
        var width = lotok.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM)?.AsValueString();
        var height = lotok.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM)?.AsValueString();
        size = width + "x" + height;

        var artikul = NotFound;
        var name = typeName + ", ВАРИАНТ ИСПОЛНЕНИЯ НЕ НАЙДЕН, ПОПРАВЬ";

        //если в таблице есть значение артикула, здесь заполняется исполнение для наименования и СУФФИКС для артикула
        var found = table.TryGetValue(height + "x" + width, out var tableArtikul);
        if (found)
        {
            artikul = tableArtikul!;
            name = typeName + " " + width + "x" + height;
            if (isp == 1)
                name += ", оцинковка горячим способом по методу Сендзимира";
            if (isp == 2)
            {
                artikul += "HDZ";
                name += ", горячего цинкования";
            }

            if (isp == 3)
            {
                artikul += "INOX";
                name += ", из нержавеющей стали";
            }
        }

        codeParam.Set(artikul);
        nameParam.Set(name);
        return found;
    }
}
