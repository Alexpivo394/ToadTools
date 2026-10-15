#nullable enable
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB.Mechanical;
using Microsoft.Win32;
using Nice3point.Revit.Toolkit;
using OfficeOpenXml;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;
using Wpf.Ui.Controls;

namespace WarmSync;

public class ExportSpacesToExcel
{
    public void Run()
    {
        var uidoc = RevitContext.ActiveUiDocument!;
        var doc = uidoc.Document;

        var dialog = new SaveFileDialog
        {
            Title = "Сохранить Excel файл",
            Filter = "Excel files (*.xlsx)|*.xlsx",
            DefaultExt = "xlsx",
            AddExtension = true,
            FileName = "spaces_export.xlsx"
        };
        
        string excelPath = "";
        
        if (dialog.ShowDialog() == true) excelPath = dialog.FileName;

        ExcelPackage.License.SetNonCommercialPersonal("ToadTools");

        if (string.IsNullOrEmpty(excelPath)) return;

        try
        {
            List<Space> spaces = GetSpaces(doc);

            using (var progress = ToadProgress.Show("Выгрузка пространств",
                       new ProgressOptions { Icon = SymbolRegular.TableArrowUp24 }))
            using (var package = new ExcelPackage())
            {
                var ws = package.Workbook.Worksheets.Add("Spaces");

                // Заголовки
                ws.Cells[1, 1].Value = "ID элемента";
                ws.Cells[1, 2].Value = "Уровень";
                ws.Cells[1, 3].Value = "Номер";
                ws.Cells[1, 4].Value = "Имя";
                ws.Cells[1, 5].Value = "Площадь";
                ws.Cells[1, 6].Value = "Объем";
                ws.Cells[1, 7].Value = "ADSK_Температура в помещении";
                ws.Cells[1, 8].Value = "ADSK_Теплопотери";

                int row = 2;

                foreach (var sp in progress.Track(spaces, "Чтение пространств"))
                {
                    Debug.WriteLine($"Обрабатываю Space ID {sp.Id}");
#if REVIT2024_OR_GREATER
                    ws.Cells[row, 1].Value = sp.Id.Value;
#else
                    ws.Cells[row, 1].Value = sp.Id.IntegerValue;
#endif
                    ws.Cells[row, 2].Value = sp.Level?.Name ?? "";
                    ws.Cells[row, 3].Value = sp.Number ?? "";
                    ws.Cells[row, 4].Value = sp.Name ?? "";
                    {
                        var p = sp.get_Parameter(BuiltInParameter.ROOM_AREA);
                        ws.Cells[row, 5].Value = p != null ? p.AsValueString() : "";
                    }
                    {
                        var p = sp.get_Parameter(BuiltInParameter.ROOM_VOLUME);
                        ws.Cells[row, 6].Value = p != null ? p.AsValueString() : "";
                    }

                    ws.Cells[row, 7].Value = GetParamValue(sp, "ADSK_Температура в помещении");
                    ws.Cells[row, 8].Value = GetParamValue(sp, "ADSK_Теплопотери");

                    row++;
                }

                progress.Stage("Сохранение файла");

                // автоширина, чтобы таблица не выглядела как жопа
                ws.Cells[ws.Dimension.Address].AutoFitColumns();

                FileInfo fi = new FileInfo(excelPath);
                package.SaveAs(fi);
            }
            
            var dial = ToadDialogService.Show(
                "Успех!",
                $"Выгружено пространств: {GetSpaces(doc).Count}\nФайл:\n{excelPath}",
                DialogButtons.OK,
                DialogIcon.Info
            );
        }
        catch (OperationCanceledException)
        {
            ToadDialogService.Show("Прервано", "Выгрузка прервана, файл не сохранён.",
                DialogButtons.OK, DialogIcon.Info);
        }
        catch (Exception ex)
        {
            var dial = ToadDialogService.Show(
                "Ошибка",
                ex.ToString(),
                DialogButtons.OK,
                DialogIcon.Error
            );
            throw;
        }
    }

    private List<Space> GetSpaces(Document doc)
    {
        var col = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_MEPSpaces)
            .WhereElementIsNotElementType();

        var res = new List<Space>();

        foreach (var e in col)
            if (e is Space sp)
                res.Add(sp);

        return res;
    }

    private object? GetParamValue(Element el, string paramName)
    {
        Parameter? p = el.LookupParameter(paramName);
        if (p == null) return null;

        return p.StorageType switch
        {
            StorageType.Double => p.AsDouble(),
            StorageType.Integer => p.AsInteger(),
            StorageType.String => p.AsString(),
#if REVIT2024_OR_GREATER
            StorageType.ElementId => p.AsElementId()?.Value,
#else
            StorageType.ElementId => p.AsElementId()?.IntegerValue,
#endif
            _ => null
        };
    }
}