#nullable enable
using System.Diagnostics;
using System.IO;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using ParamChecker.Models;
using ParamChecker.Models.ExportProfiles;
using ParamChecker.Models.Filters;
using ParamChecker.ViewModels.PagesViewModels;
using ParamChecker.ViewModels.Windows;

namespace ParamChecker.Services;

public class ExportService
{
    private readonly UIApplication _uiApplication;
    private readonly CategoryService _categoryService;
    private readonly SettingsViewModel? _settingsViewModel;
    private readonly Logger _logger;

    public ExportService(UIApplication uiApplication, CategoryService categoryService,
        SettingsViewModel? settingsViewModel, Logger logger)
    {
        _logger = logger;
        _settingsViewModel = settingsViewModel;
        _uiApplication = uiApplication;
        _categoryService = categoryService;
    }


    public void ExportProfile(ExportProfile profile)
    {
        try
        {
            _logger.Log($"Начало экспорта профиля: {profile.ProfileName}");
            foreach (var model in profile.Models)
            {
                _logger.Log($"Обработка модели: {model.ServerPath}");
                var doc = OpenDocumentAsDetach(model.ServerPath, model.WorksetKeyword);
                int greenCount = 0, redCount = 0, yellowCount = 0;

                ExcelPackage.License.SetNonCommercialPersonal("ToadTools");
                using var excelDoc = new ExcelPackage();
                foreach (var rule in profile.Rules)
                {
                    _logger.Log($"Обработка правила: {rule.Title}");
                    var worksheet = excelDoc.Workbook.Worksheets.Add(rule.Title);

                    worksheet.Cells[1, 1].Value = "Имя элемента";
                    worksheet.Cells[1, 2].Value = "ID элемента";

                    var paramvm = new ParameterConfigViewModel();

                    paramvm.LoadFromJson(rule.ParameterConfigJson);

                    var parameters = paramvm.Parameters.Select(p => p.Value).ToList();

                    for (var i = 0; i < parameters.Count; i++) worksheet.Cells[1, 3 + i].Value = parameters[i];

                    var filtervm = new FilterConfigViewModel(_categoryService);
                    var filterConfigModel = filtervm.ParseConfig(rule.FilterConfigJson);

                    var elements = FilterElementsFromConfig(doc, model.ViewName, filterConfigModel);

                    var row = 2;
                    int redCountLocal = 0, yellowCountLocal = 0, greenCountLocal = 0;

                    foreach (var element in elements)
                    {
                        worksheet.Cells[row, 1].Value = element.Name;
#if REVIT2024_OR_GREATER
                        worksheet.Cells[row, 2].Value = element.Id.Value;
#else
                        worksheet.Cells[row, 2].Value = element.Id.IntegerValue;
#endif


                        for (var i = 0; i < parameters.Count; i++)
                        {
                            var param = element.LookupParameter(parameters[i]);
                            try
                            {
                                if (param == null)
                                {
                                    var typeId = element.GetTypeId();
                                    if (typeId != ElementId.InvalidElementId)
                                    {
                                        var elementType = element.Document.GetElement(typeId) as ElementType;
                                        if (elementType != null) param = elementType.LookupParameter(parameters[i]);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(
                                    $"Ошибка при получении параметра {parameters[i]} для элемента {element.Id}", ex);
                                Debug.WriteLine(ex.Message);
                            }

                            var cell = worksheet.Cells[row, 3 + i];
                            if (param == null)
                            {
                                cell.Value = "N/A";
                                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Red);
                                redCountLocal++;
                            }
                            else
                            {
                                cell.Value = param.AsValueString();
                            }

                            if (cell.Value == null ||
                                (cell.Value is string && string.IsNullOrWhiteSpace((string)cell.Value)))
                            {
                                cell.Value = string.Empty;
                                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Yellow);
                                yellowCountLocal++;
                            }
                            else if ((string)cell.Value == "N/A")
                            {
                                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Red);
                            }
                            else
                            {
                                cell.Style.Fill.PatternType = ExcelFillStyle.Solid;
                                cell.Style.Fill.BackgroundColor.SetColor(System.Drawing.Color.Green);
                                greenCountLocal++;
                            }
                        }

                        row++;
                    }

                    var totalCountLocal = redCountLocal + yellowCountLocal + greenCountLocal;
                    worksheet.Cells[row + 1, 1].Value = "Итоги";
                    worksheet.Cells[row + 2, 1].Value = "Отсутствующих параметров";
                    worksheet.Cells[row + 2, 2].Value = redCountLocal;
                    worksheet.Cells[row + 3, 1].Value = "Пустых параметров";
                    worksheet.Cells[row + 3, 2].Value = yellowCountLocal;
                    worksheet.Cells[row + 4, 1].Value = "Заполненных параметров";
                    worksheet.Cells[row + 4, 2].Value = greenCountLocal;
                    worksheet.Cells[row + 5, 1].Value = "Всего параметров";
                    worksheet.Cells[row + 5, 2].Value = totalCountLocal;

                    redCount += redCountLocal;
                    yellowCount += yellowCountLocal;
                    greenCount += greenCountLocal;

                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                    _logger.Log(
                        $"Правило {rule.Title} обработано: заполнено {greenCountLocal}, пустых {yellowCountLocal}, отсутствует {redCountLocal}");
                }
    
                var totalworksheet = excelDoc.Workbook.Worksheets.Add("Итоги");
                var totalCount = redCount + yellowCount + greenCount;
                totalworksheet.Cells[1, 1].Value = "Итоги";
                totalworksheet.Cells[2, 1].Value = "Отсутствующих параметров";
                totalworksheet.Cells[2, 2].Value = redCount;
                totalworksheet.Cells[3, 1].Value = "Пустых параметров";
                totalworksheet.Cells[3, 2].Value = yellowCount;
                totalworksheet.Cells[4, 1].Value = "Заполненных параметров";
                totalworksheet.Cells[4, 2].Value = greenCount;
                totalworksheet.Cells[5, 1].Value = "Всего параметров";
                totalworksheet.Cells[5, 2].Value = totalCount;

                totalworksheet.Cells[totalworksheet.Dimension.Address].AutoFitColumns();

                var reportsPath = _settingsViewModel?.ReportsPath;
                if (string.IsNullOrWhiteSpace(reportsPath))
                    reportsPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                Directory.CreateDirectory(reportsPath!);

                var fileName = $"{ProcessDocTitle(doc?.Title ?? throw new InvalidOperationException())}_LOIReport.xlsx";

                var filePath = Path.Combine(reportsPath, fileName);
                var file = new FileInfo(filePath);
                excelDoc.SaveAs(file);
                _logger.Log($"Отчет сохранен: {filePath}");

                if (_settingsViewModel is { UpdateGeneralReport: true })
                {
                    _logger.Log("Обновление общего отчета");
                    string networkFilePath;
                    if (_settingsViewModel != null && _settingsViewModel.ReportFilePath != String.Empty)
                    {
                        networkFilePath = _settingsViewModel.ReportFilePath;
                    }
                    else
                    {
                        networkFilePath = @"Y:\13-BIM (разработка)\07_Bim отдел\Проекты и модели.xlsx";
                    }

                    var fileInfo = new FileInfo(networkFilePath);

                    using (var package = new ExcelPackage(fileInfo))
                    {
                        if (package.Workbook.Worksheets.Count == 0)
                        {
                            _logger.Log("Общий отчет не содержит листов");
                            return;  
                        }

                        var worksheet = package.Workbook.Worksheets.First();

                        var modelFound = false;
                        var row = 2;

                        while (worksheet.Cells[row, 5].Value != null)
                        {
                            if (worksheet.Cells[row, 5].Value.ToString() ==
                                ProcessDocTitle(doc.Title ?? throw new InvalidOperationException()))
                            {
                                worksheet.Cells[row, 13].Value = totalCount;
                                worksheet.Cells[row, 14].Value = greenCount;
                                worksheet.Cells[row, 15].Value = yellowCount;
                                worksheet.Cells[row, 16].Value = redCount;
                                modelFound = true;
                                _logger.Log($"Модель {ProcessDocTitle(doc.Title)} обновлены в общем отчете");
                                break;
                            }

                            row++;
                        }

                        if (modelFound)
                        {
                            package.Save();
                            _logger.Log("Общий отчет сохранен");
                        }
                        else
                        {
                            _logger.Log("Модель не найдена в общем отчете");
                        }
                    }
                }

                doc.Close(false);
                _logger.Log($"Экспорт завершен для модели: {model.ServerPath}");
            }

            _logger.Log("Экспорт профиля завершен успешно");
        }
        catch (Exception ex)
        {
            _logger.LogError("Критическая ошибка при экспорте профиля", ex);
            throw;
        }
    }

    private string ProcessDocTitle(string docTitle)
    {
        var lastUnderscoreIndex = docTitle.LastIndexOf('_');
        return lastUnderscoreIndex > -1 ? docTitle.Substring(0, lastUnderscoreIndex) : docTitle;
    }

    private Document? OpenDocumentAsDetach(string filePath, string badNameWorkset)
    {
        var app = _uiApplication;
        var modelPathServ = ModelPathUtils.ConvertUserVisiblePathToModelPath(filePath);
        var controlledApp = app.Application;

        var openOptions = new OpenOptions();

        try
        {
            _logger.Log($"Открытие документа: {filePath}");
            openOptions.DetachFromCentralOption = DetachFromCentralOption.DetachAndPreserveWorksets;
            var worksetConfiguration =
                new WorksetConfiguration(WorksetConfigurationOption.CloseAllWorksets);

            IList<WorksetPreview> worksets = WorksharingUtils.GetUserWorksetInfo(modelPathServ);
            IList<WorksetId> worksetIds;
            if (string.IsNullOrEmpty(badNameWorkset))
            {
                worksetIds = worksets.Select(workset => workset.Id).ToList();
            }
            else
            {
                worksetIds = worksets
                    .Where(workset => !workset.Name.ToLower().Contains(badNameWorkset.ToLower()))
                    .Select(workset => workset.Id)
                    .ToList();
            }

            worksetConfiguration.Open(worksetIds);

            openOptions.SetOpenWorksetsConfiguration(worksetConfiguration);
        }
        catch (Exception ex)
        {
            _logger.LogError("Ошибка при настройке параметров открытия документа", ex);
            _logger.Log($"Подробности ошибки: {ex.GetType().FullName}");
            Debug.WriteLine(ex.ToString());
        }

        FailureProcessorOpenDocument? failureProcessor;
        failureProcessor = null;
        Document? openDoc;

        try
        {
            failureProcessor = new FailureProcessorOpenDocument();

            controlledApp.FailuresProcessing += failureProcessor.OnFailuresProcessing;
            app.DialogBoxShowing += failureProcessor.OnDialogBoxShowing;


            openDoc = controlledApp.OpenDocumentFile(modelPathServ, openOptions);


            if (openDoc != null && openDoc.IsValidObject)
            {
                _logger.Log($"Документ успешно открыт: {openDoc.Title}");
                return openDoc;
            }

            _logger.Log("Не удалось открыть документ");
            return null;
        }
        catch (Exception e)
        {
            _logger.LogError("Ошибка при открытии документа", e);
            _logger.Log($"Подробности ошибки: {e.GetType().FullName}");
            throw;
        }
        finally
        {
            if (failureProcessor != null)
            {
                controlledApp.FailuresProcessing -= failureProcessor.OnFailuresProcessing;
                app.DialogBoxShowing -= failureProcessor.OnDialogBoxShowing;
            }
        }
    }

    public List<Element> FilterElementsFromConfig(Document? doc, string viewName, FilterConfigModel config)
    {
        try
        {
            // 🔎 Находим нужный вид
            var view = new FilteredElementCollector(doc)
                .OfClass(typeof(View3D))
                .Cast<View3D>()
                .FirstOrDefault(v =>
                    !v.IsTemplate &&
                    v.Name.Equals(viewName, StringComparison.OrdinalIgnoreCase));


            if (view == null) throw new Exception($"Вид с именем '{viewName}' не найден.");

            // 🧱 Начинаем с отбора по категориям
            IEnumerable<Element> elements = new FilteredElementCollector(doc, view.Id)
                .WhereElementIsNotElementType()
                .Where(e =>
                {
                    var category = e.Category;
#if REVIT2024_OR_GREATER
                    bool categoryMatch = category != null &&
                                         (config.SelectedCategories?.Contains((BuiltInCategory)category.Id.Value) ??
                                          false);
#else
                    bool categoryMatch = category != null
                                         && (config.SelectedCategories?.Contains(
                                             (BuiltInCategory)category.Id.IntegerValue) ?? false);
#endif
                    

                    return config.CategoryParameterLogic switch
                    {
                        CategoryParameterLogic.CategoriesOnly => categoryMatch,
                        CategoryParameterLogic.CategoriesAndParameters => categoryMatch,
                        CategoryParameterLogic.CategoriesOrParameters => categoryMatch,
                        CategoryParameterLogic.ParametersOnly => true,
                        _ => true
                    };
                });

            // 🔄 Добавляем ВСЕ помещения из документа, если категория OST_Rooms выбрана
            if (config.SelectedCategories?.Contains(BuiltInCategory.OST_Rooms) == true)
            {
                var rooms = new FilteredElementCollector(doc)
                    .OfCategory(BuiltInCategory.OST_Rooms)
                    .WhereElementIsNotElementType()
                    .ToList();

                elements = elements.Concat(rooms).Distinct();
            }


            // 📋 Применяем параметрические условия
            var filtered = elements.Where(e =>
            {
                bool paramResult = EvaluateConditions(config.Conditions, config.ParameterLogic, e);

                return config.CategoryParameterLogic switch
                {
                    CategoryParameterLogic.CategoriesOnly => true,
                    CategoryParameterLogic.ParametersOnly => paramResult,
                    CategoryParameterLogic.CategoriesAndParameters => paramResult,
                    CategoryParameterLogic.CategoriesOrParameters => paramResult,
                    _ => true
                };
            });

            return filtered.ToList();
        }
        catch (Exception ex)
        {
            _logger.LogError($"Ошибка при фильтрации элементов для вида {viewName}", ex);
            throw;
        }
    }

    private bool EvaluateConditions(List<ConditionModelBase> conditions, FilterParameterLogic logic, Element e)
    {
        var results = conditions.Select(c => EvaluateCondition(c, e)).ToList();
        return logic == FilterParameterLogic.And ? results.All(r => r) : results.Any(r => r);
    }

    private bool EvaluateCondition(ConditionModelBase cond, Element e)
    {
        return cond switch
        {
            SimpleConditionModel simple => EvaluateSimpleCondition(simple, e),
            GroupConditionModel group => group.Children.All(child => EvaluateCondition(child, e)),
            _ => false
        };
    }


    private bool EvaluateSimpleCondition(SimpleConditionModel cond, Element e)
    {
        try
        {
            // Пробуем найти параметр в самом элементе
            var param = e.LookupParameter(cond.ParameterName);

            // Если не нашли — пробуем найти в типе элемента
            if (param == null && e.Document != null)
            {
                var type = e.Document.GetElement(e.GetTypeId());
                param = type?.LookupParameter(cond.ParameterName);
            }

            string? val = param?.AsValueString() ?? param?.AsString();

            bool parsedVal = double.TryParse(val, out double number);
            bool parsedCond = double.TryParse(cond.Value, out double targetNumber);

            switch (cond.SelectedLogic)
            {
                case FilterLogic.Equals:
                    return string.Equals(val, cond.Value, StringComparison.OrdinalIgnoreCase);

                case FilterLogic.NotEquals:
                    return !string.Equals(val, cond.Value, StringComparison.OrdinalIgnoreCase);

                case FilterLogic.Contains:
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        return val?.Contains(cond.Value, StringComparison.OrdinalIgnoreCase) ?? false;
#else
                    return val?.IndexOf(cond.Value, StringComparison.OrdinalIgnoreCase) >= 0;
#endif

                case FilterLogic.NotContains:    
#if NETSTANDARD2_1_OR_GREATER || NETCOREAPP2_1_OR_GREATER
        return !(val?.Contains(cond.Value, StringComparison.OrdinalIgnoreCase) ?? false);
#else
                    return !(val?.IndexOf(cond.Value, StringComparison.OrdinalIgnoreCase) >= 0);
#endif

                case FilterLogic.Exists:
                    return param != null && (!string.IsNullOrWhiteSpace(val) || param.HasValue);

                case FilterLogic.NotExists:
                    return param == null || (string.IsNullOrWhiteSpace(val) && !param.HasValue);

                case FilterLogic.GreaterThan:
                    return parsedVal && parsedCond && number > targetNumber;

                case FilterLogic.GreaterThanOrEquals:
                    return parsedVal && parsedCond && number >= targetNumber;

                case FilterLogic.LessThan:
                    return parsedVal && parsedCond && number < targetNumber;

                case FilterLogic.LessThanOrEquals:
                    return parsedVal && parsedCond && number <= targetNumber;

                default:
                    return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Ошибка при оценке условия для параметра {cond.ParameterName}", ex);
            return false;
        }
    }
}