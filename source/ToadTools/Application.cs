using Nice3point.Revit.Toolkit.External;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using Autodesk.Revit.UI;
using Nice3point.Revit.Extensions.UI;

namespace ToadTools
{
    /// <summary>
    ///     Application entry point
    /// </summary>
    [UsedImplicitly]
    public class Application : ExternalApplication
    {
        private const string TabName = "ToadTools";
        /// <summary>Вики плагина: страница справки открывается по F1 на кнопке ленты.</summary>
        internal const string WikiUrl = "https://github.com/Alexpivo394/ToadTools/wiki/";

        public override void OnStartup()
        {
            // Кэш каталогов Revit Server — в прежней папке ToadTools, а не в папке пакета по умолчанию.
            Alexpivo394.Revit.Export.Sources.ServerCatalog.CacheRoot = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ToadTools", "RevitServerCache");
            // Закрепляемую панель Revit принимает только во время запуска.
            Collisions.CollisionsPane.Register(Application);
            Families.FamiliesPane.Register(Application);
            NotificationWatcher.UiDispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
            // Год Revit для раздела «О ToadTools»: окно настроек не ссылается на Revit API.
            ToadTools.Connection.SettingsDialog.RevitVersion = Application.ControlledApplication.VersionNumber;
            // Кнопка «Настройки» в окнах плагинов открывает то же окно, что и лента.
            ToadTools.UI.Services.SettingsLauncher.Handler = ToadTools.Connection.SettingsDialog.Show;
            try
            {
                Host.Start(Application);
                CreateRibbon();
                Telemetry.TelemetryHost.RememberButtons(Application.GetRibbonPanels(TabName));
            }
            catch (Exception exception)
            {
                // Сбой запуска не уходит в Revit: его окно причины не называет. Лента может остаться неполной.
                ToadTools.UI.Services.ErrorReport.Show("Запуск ToadTools", exception);
            }
        }

        public override void OnShutdown()
        {
            try
            {
                Host.Stop();
            }
            catch (Exception exception)
            {
                ToadTools.UI.Services.ErrorReport.Log("Завершение ToadTools", exception);
            }

            ToadTools.UI.Services.ToadLog.Info("ToadTools", "Revit закрыт");
            ToadTools.UI.Services.ToadLog.Flush();
        }

        /// <summary>
        ///     ToadTools — кнопка «Настройки», всегда первая панель вкладки.
        ///     BIM — основные команды крупными кнопками, остальные стопкой маленьких с подписью.
        ///     Общие — «Коллизии» крупной кнопкой, остальное — маленькие кнопки с короткой подписью, по три в столбик.
        ///     Панели разделов (Лотки, Сети связи, ОВ, АР, КР) — полноразмерные кнопки.
        /// </summary>
        private void CreateRibbon()
        {
            CreateSignInPanel();
            CreateBimPanel();
            CreateGeneralPanel();
            CreateDisciplinePanels();
        }

        private void CreateSignInPanel()
        {
            var panel = CreatePanel("ToadTools", MediaColor.FromRgb(173, 216, 230));
            panel.AddPushButton<Commands.SettingsCommand>("Настройки")
                .WithIcon("Settings")
                .WithInfo("Вход в ToadFlow, тема, компактный режим,\nжурнал и диагностика — для всех\nинструментов ToadTools.", "ToadFlow");
        }

        private void CreateBimPanel()
        {
            var panel = CreatePanel("BIM", MediaColor.FromRgb(173, 216, 230));

            var worksets = panel.AddSplitButton("Worksets", "Рабочие\nнаборы");
            worksets.AddPushButton<Commands.WorkingSetCommand>("Создание\nнаборов")
                .WithIcon("WorkingSet")
                .WithInfo("Создает в модели рабочие наборы\nдля выбранного раздела.", "WorkingSet");
            worksets.AddPushButton<Commands.WorksetCheckCommand>("Проверка\nнаборов")
                .WithIcon("WorksetCheck")
                .WithInfo("Проверяет распределение элементов\nоткрытой модели по рабочим наборам\nи исправляет найденное.",
                    "WorksetCheck");

            panel.AddPushButton<Commands.ParamCheckerCommand>("Param\nChecker")
                .WithIcon("ParamChecker")
                .WithInfo("Выгружает из моделей на сервере\nотчет по заполнению параметров.", "ParamChecker");

            panel.AddPushButton<Commands.ToadExporterCommand>("Toad\nExporter")
                .WithIcon("ToadExporter")
                .WithInfo("Выгружает модели с Revit Server\nили из папки в RVT и NWC.", "ToadExporter");

            var stack = panel.AddStackPanel();
            stack.AddPushButton<Commands.ModelTransplanterCommand>("Копирование элементов")
                .WithIcon("ModelTransplanter")
                .WithInfo("Позволяет копировать элементы\nиз модели в модель.", "ModelTransplanter");
            stack.AddPushButton<Commands.ChangeSharedFamiliesCommand>("Общие → необщие")
                .WithIcon("ChangeSharedFamilies")
                .WithInfo("Заменяет общие вложенные семейства\nна необщие. Работает только с\nредактором семейства.",
                    "ChangeSharedFamilies");
        }

        private void CreateGeneralPanel()
        {
            var panel = CreatePanel("Общие", MediaColor.FromRgb(204, 153, 102));

            panel.AddPushButton<Commands.CollisionsCommand>("Коллизии")
                .WithIcon("Collisions")
                .WithInfo("Панель с коллизиями открытой модели\nиз ToadFlow: список, миниатюры, фильтры.\nПовторное нажатие скрывает панель.", "Collisions");

            panel.AddPushButton<Commands.FamiliesCommand>("Семейства")
                .WithIcon("Families")
                .WithInfo("Библиотека семейств ToadFlow: поиск, загрузка\nв проект с выбором типоразмеров, «В проекте»,\nпубликация из редактора семейств.\nПовторное нажатие скрывает панель.", "Families");

            // Маленькие кнопки с короткой подписью; после трёх стопка сама переносится в новый столбец
            var stack = panel.AddStackPanel();
            stack.AddPushButton<Commands.SmartTagsCommand>("Умные выноски")
                .WithIcon("SmartTags")
                .WithInfo("Расставляет марки на видимых элементах\nактивного вида без пересечений текста\nи выносок. Весь запуск отменяется\nодним Ctrl+Z.",
                    "SmartTags");
            stack.AddPushButton<Commands.QuantityCheckCommand>("Запись количества")
                .WithIcon("QuantityCheck")
                .WithInfo("Заполняет параметр количества\nв элементах модели.", "QuantityCheck");
            stack.AddPushButton<Commands.WriteDashCommand>("Запись прочерков")
                .WithIcon("WriteDash")
                .WithInfo("Заполняет прочерк в указанные параметры\nесли в них нет значения.", "WriteDash");
            stack.AddPushButton<Commands.ClassifierCodeCommand>("Код классификатора")
                .WithIcon("ClassifierCode")
                .WithInfo("Заполняет ADSK_Код по классификатору\nи ADSK_Описание по классификатору\nу элементов ИОС.",
                    "ClassifierCode");
            stack.AddPushButton<Commands.LengthCommand>("Длина элементов")
                .WithIcon("Length")
                .WithInfo("Посчитать суммарную длину выбранных элементов.", "Length");
            stack.AddPushButton<Commands.CreateSpacesCommand>("Создание пространств")
                .WithIcon("CreateSpaces")
                .WithInfo("Создает в проекте пространства на основе помещений из раздела АР.", "CreateSpaces");
        }

        private void CreateDisciplinePanels()
        {
            //Лотки
            var panelTray = CreatePanel("Лотки", MediaColor.FromRgb(152, 168, 124));

            var trays = panelTray.AddPullDownButton("Кабельные лотки", "Кабельные лотки");
            trays.WithIcon("CableTrays").WithInfo("Артикулы и наименования лотков,\nраскраска лотков на виде.", "CableTrays");
            trays.AddPushButton<Commands.CableTrayArticulCommand>("Артикулы и наименования").WithIcon("CableTrays")
                .WithInfo("Заполняет код изделия, наименование\nи количество у лотков.", "CableTrays");
            trays.AddPushButton<Commands.CableTrayColorPerforationCommand>("Покрасить по перфорации").WithIcon("CableTrays")
                .WithInfo("Раскрашивает лотки на виде по типу:\nперфорированный, неперфорированный,\nпроволочный.", "CableTrays");
            trays.AddPushButton<Commands.CableTrayColorVariantCommand>("Покрасить по исполнению").WithIcon("CableTrays")
                .WithInfo("Раскрашивает лотки и детали на виде\nпо ADSK_Исполнение.", "CableTrays");
            trays.AddPushButton<Commands.CableTrayColorCoverCommand>("Покрасить крышки").WithIcon("CableTrays")
                .WithInfo("Выделяет на виде красным лотки\nи детали с крышкой.", "CableTrays");

            panelTray.AddPushButton<Commands.CreateCoverCommand>("Разместить\nкрышки")
                .WithIcon("CreateCover")
                .WithInfo("Разместить в модели крышки на кабельные лотки", "CreateCover");

            //Сети связи
            var panelSs = CreatePanel("Сети связи", MediaColor.FromRgb(155, 242, 222));

            panelSs.AddPushButton<Commands.SSPlanCommand>("Структурная\nсхема")
                .WithIcon("SSPlan")
                .WithInfo("Создать структурную схему.", "SSPlan");

            //ОВ
            var panelOv = CreatePanel("ОВ", MediaColor.FromRgb(255, 209, 220));

            var warm = panelOv.AddPullDownButton("Теплопотери", "Теплопотери");
            warm.WithIcon("WarmSync").WithInfo("Обмен температурой и теплопотерями\nмежду пространствами и Excel.", "WarmSync");
            warm.AddPushButton<Commands.RenameSpacesCommand>("Заполнение номеров пространств").WithIcon("WarmSync")
                .WithInfo("Записывает пространствам номера\nпо помещениям из связи АР.", "WarmSync");
            warm.AddPushButton<Commands.WriteSpaceIdToParamCommand>("Запись ID пространств").WithIcon("WarmSync")
                .WithInfo("Записывает ID пространства\nв параметр «ID элемента».", "WarmSync");
            warm.AddPushButton<Commands.ExportSpacesToExcelCommand>("Экспорт пространств в Excel").WithIcon("WarmSync")
                .WithInfo("Выгружает пространства в Excel.", "WarmSync");
            warm.AddPushButton<Commands.WriteFromExcelCommand>("Импорт значений из Excel").WithIcon("WarmSync")
                .WithInfo("Загружает температуру и теплопотери\nиз Excel в пространства.", "WarmSync");

            //АР
            var panelAr = CreatePanel("АР", MediaColor.FromRgb(245, 245, 220));

            panelAr.AddPushButton<Commands.DoorSideCommand>("Открывание\nдверей")
                .WithIcon("DoorSide")
                .WithInfo("Записать сторону открывания двери в параметр Открывание", "DoorSide");
            panelAr.AddPushButton<Commands.ParamToFinishCommand>("Основание\nотделки")
                .WithIcon("ParamToFinish")
                .WithInfo("Позволяет записать основание отделки в выбранный параметр", "ParamToFinish");

            //КР
            var panelKr = CreatePanel("КР", MediaColor.FromRgb(172, 229, 238));

            const string reinforcementPage = "ReinforcementByColor";
            var reinforcement = panelKr.AddPullDownButton("Армирование", "Армирование");
            reinforcement.WithIcon("ReinforcementByColor").WithInfo("Размещает семейства армирования взамен цветовых областей", reinforcementPage);
            reinforcement.AddPushButton<Commands.RSHPLeftRightCommand>("R-SHP-Армирование по Y").WithIcon("ReinforcementByColor")
                .WithInfo("Заменяет выбранные области семейством\nR-SHP, стержни вдоль Y.", reinforcementPage);
            reinforcement.AddPushButton<Commands.RSHPUpDownCommand>("R-SHP-Армирование по X").WithIcon("ReinforcementByColor")
                .WithInfo("Заменяет выбранные области семейством\nR-SHP, стержни вдоль X.", reinforcementPage);
            reinforcement.AddPushButton<Commands.RSUMLeftRightCommand>("R-SUM-Армирование по Y").WithIcon("ReinforcementByColor")
                .WithInfo("Заменяет выбранные области семейством\nR-SUM, стержни вдоль Y.", reinforcementPage);
            reinforcement.AddPushButton<Commands.RSUMUpDownCommand>("R-SUM-Армирование по X").WithIcon("ReinforcementByColor")
                .WithInfo("Заменяет выбранные области семейством\nR-SUM, стержни вдоль X.", reinforcementPage);
        }

        private RibbonPanel CreatePanel(string name, MediaColor titleColor)
        {
            var panel = Application.CreatePanel(name, TabName);
            panel.SetTitleBarBackground(new SolidColorBrush(titleColor));
            return panel;
        }

        internal static string IconPath(string icon, int size) =>
            $"/ToadTools;component/Resources/Icons/{icon}{size}.png";

        internal static ContextualHelp Help(string page) =>
            new(ContextualHelpType.Url, WikiUrl + page);
    }

    internal static class RibbonButtonExtensions
    {
        /// <summary>
        ///     Ставит иконки 16 и 32 px из Resources/Icons по общему имени (например "Length" → Length16.png, Length32.png).
        /// </summary>
        public static T WithIcon<T>(this T button, string icon) where T : RibbonButton
        {
            button.SetImage(Application.IconPath(icon, 16));
            button.SetLargeImage(Application.IconPath(icon, 32));
            return button;
        }

        /// <summary>
        ///     Подсказка и, если есть, страница вики (открывается по F1, пока курсор на кнопке).
        /// </summary>
        public static T WithInfo<T>(this T item, string toolTip, string? helpPage = null) where T : RibbonItem
        {
            item.SetToolTip(toolTip);
            if (helpPage is not null) item.SetContextualHelp(Application.Help(helpPage));
            return item;
        }
    }
}
