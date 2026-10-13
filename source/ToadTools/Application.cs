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
    public class Application : AsyncExternalApplication
    {
        private const string TabName = "ToadTools";
        private const string HelpUrl = "https://bim-baza.yonote.ru/doc/";

        public override async Task OnStartupAsync()
        {
            await Host.StartAsync();
            CreateRibbon();
        }

        public override async Task OnShutdownAsync()
        {
            await Host.StopAsync();
        }

        /// <summary>
        ///     BIM — основные команды крупными кнопками, остальные стопкой маленьких с подписью.
        ///     Общие — маленькие кнопки с короткой подписью, по три в столбик.
        ///     Панели разделов (Лотки, Сети связи, ОВ, АР, КР) — полноразмерные кнопки.
        /// </summary>
        private void CreateRibbon()
        {
            CreateBimPanel();
            CreateGeneralPanel();
            CreateDisciplinePanels();
        }

        private void CreateBimPanel()
        {
            var panel = CreatePanel("BIM", MediaColor.FromRgb(173, 216, 230));

            var worksets = panel.AddSplitButton("Worksets", "Рабочие\nнаборы");
            worksets.AddPushButton<Commands.WorkingSetCommand>("Создание\nнаборов")
                .WithIcon("Create")
                .WithInfo("Создает в модели рабочие наборы\nдля выбранного раздела.",
                    "sozdanie-rabochih-naborov-plaginom-rptools-j3Ycy39HVW");
            worksets.AddPushButton<Commands.WorksetCheckCommand>("Проверка\nнаборов")
                .WithIcon("WorksetsCheck")
                .WithInfo("Позволяет проверить заданные\nмодели на соответствие элементов\nрабочим наборам.",
                    "worksetcheck-rfycq6j0Bd");

            panel.AddPushButton<Commands.ParamCheckerCommand>("Param\nChecker")
                .WithIcon("Export")
                .WithInfo("Выгружает из моделей на сервере\nотчет по заполнению параметров.",
                    "paramchecker-movFIkYSwy");

            var stack = panel.AddStackPanel();
            stack.AddPushButton<Commands.ModelTransplanterCommand>("Копирование элементов")
                .WithIcon("Transplanter")
                .WithInfo("Позволяет копировать элементы\nиз модели в модель.",
                    "kopirovanie-soderzhimogo-modeli-7q8Wf296FB");
            stack.AddPushButton<Commands.ChangeSharedFamiliesCommand>("Общие → необщие")
                .WithIcon("ChangeSharedFamilies")
                .WithInfo("Заменяет общие вложенные семейства\nна необщие. Работает только с\nредактором семейства.",
                    "zamenit-obshie-semejstva-neobshimi-BDbyQvBJH7");
        }

        private void CreateGeneralPanel()
        {
            var panel = CreatePanel("Общие", MediaColor.FromRgb(204, 153, 102));

            // Маленькие кнопки с короткой подписью; после трёх стопка сама переносится в новый столбец
            var stack = panel.AddStackPanel();
            stack.AddPushButton<Commands.SmartTagsCommand>("Умные выноски")
                .WithIcon("SmartTags")
                .WithInfo("Расставляет марки на видимых элементах\nактивного вида без пересечений текста\nи выносок. Весь запуск отменяется\nодним Ctrl+Z.");
            stack.AddPushButton<Commands.QuantityCheckCommand>("Запись количества")
                .WithIcon("QuantityCheck")
                .WithInfo("Заполняет параметр количества\nв элементах модели.",
                    "zapolnenie-parametra-kolichestvo-beinN88LwP");
            stack.AddPushButton<Commands.WriteDashCommand>("Запись прочерков")
                .WithIcon("WriteDash")
                .WithInfo("Заполняет прочерк в указанные параметры\nесли в них нет значения.",
                    "zapisat-procherk-kfwUDj6TeL");
            stack.AddPushButton<Commands.ClassifierCodeCommand>("Код классификатора")
                .WithIcon("ClassifierCode")
                .WithInfo("Заполняет ADSK_Код по классификатору\nи ADSK_Описание по классификатору\nу элементов ИОС.");
            stack.AddPushButton<Commands.LengthCommand>("Длина элементов")
                .WithIcon("Ruler")
                .WithInfo("Посчитать суммарную длину выбранных элементов.");
            stack.AddPushButton<Commands.CreateSpacesCommand>("Создание пространств")
                .WithIcon("Spaces")
                .WithInfo("Создает в проекте пространства на основе помещений из раздела АР.",
                    "sozdanie-prostranstv-Bia5rDOjqY");
        }

        private void CreateDisciplinePanels()
        {
            //Лотки
            var panelTray = CreatePanel("Лотки", MediaColor.FromRgb(152, 168, 124));

            var trays = panelTray.AddPullDownButton("Кабельные лотки", "Кабельные лотки");
            trays.WithIcon("1Tray").SetContextualHelp(Help("kabelnye-lotki-hSP8BFIW8g"));
            trays.AddPushButton<Commands.CableTrayArticulCommand>("Артикулы и наименования").WithIcon("1Tray");
            trays.AddPushButton<Commands.CableTrayColorPerforationCommand>("Покрасить по перфорации").WithIcon("1Tray");
            trays.AddPushButton<Commands.CableTrayColorVariantCommand>("Покрасить по исполнению").WithIcon("1Tray");
            trays.AddPushButton<Commands.CableTrayColorCoverCommand>("Покрасить крышки").WithIcon("1Tray");

            panelTray.AddPushButton<Commands.CreateCoverCommand>("Разместить\nкрышки")
                .WithIcon("CreateCover")
                .WithInfo("Разместить в модели крышки на кабельные лотки", "razmestit-kryshki-pJSWjFkyzL");

            //Сети связи
            var panelSs = CreatePanel("Сети связи", MediaColor.FromRgb(155, 242, 222));

            panelSs.AddPushButton<Commands.SSPlanCommand>("Структурная\nсхема")
                .WithIcon("SSPlan")
                .WithInfo("Создать структурную схему.", "strukturnaya-shema-yO3TQrgqVS");

            //ОВ
            var panelOv = CreatePanel("ОВ", MediaColor.FromRgb(255, 209, 220));

            var warm = panelOv.AddPullDownButton("Теплопотери", "Теплопотери");
            warm.WithIcon("WarmSync").SetContextualHelp(Help("sinhronizaciya-teplopoter-RWMUuxZBdq"));
            warm.AddPushButton<Commands.RenameSpacesCommand>("Заполнение имен пространств").WithIcon("WarmSync");
            warm.AddPushButton<Commands.WriteSpaceIdToParamCommand>("Запись ID пространств").WithIcon("WarmSync");
            warm.AddPushButton<Commands.ExportSpacesToExcelCommand>("Экспорт пространств в Excel").WithIcon("WarmSync");
            warm.AddPushButton<Commands.WriteFromExcelCommand>("Импорт значений из Excel").WithIcon("WarmSync");

            //АР
            var panelAr = CreatePanel("АР", MediaColor.FromRgb(245, 245, 220));

            panelAr.AddPushButton<Commands.DoorSideCommand>("Открывание\nдверей")
                .WithIcon("DoorSide")
                .WithInfo("Записать сторону открывания двери в параметр Открывание", "otkryvanie-dveri-JJyZ1390oD");
            panelAr.AddPushButton<Commands.ParamToFinishCommand>("Основание\nотделки")
                .WithIcon("ParamToFinish")
                .WithInfo("Позволяет записать основание отделки в выбранный параметр", "osnovanie-otdelki-EcPw4yHPWP");

            //КР
            var panelKr = CreatePanel("КР", MediaColor.FromRgb(172, 229, 238));

            var reinforcement = panelKr.AddPullDownButton("Армирование", "Армирование");
            reinforcement.WithIcon("Rei").WithInfo("Размещает семейства армирования взамен цветовых областей");
            reinforcement.AddPushButton<Commands.RSHPLeftRightCommand>("R-SHP-Армирование по Y").WithIcon("Rei");
            reinforcement.AddPushButton<Commands.RSHPUpDownCommand>("R-SHP-Армирование по X").WithIcon("Rei");
            reinforcement.AddPushButton<Commands.RSUMLeftRightCommand>("R-SUM-Армирование по Y").WithIcon("Rei");
            reinforcement.AddPushButton<Commands.RSUMUpDownCommand>("R-SUM-Армирование по X").WithIcon("Rei");
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
            new(ContextualHelpType.Url, HelpUrl + page);
    }

    internal static class RibbonButtonExtensions
    {
        /// <summary>
        ///     Ставит иконки 16 и 32 px из Resources/Icons по общему имени (например "Ruler" → Ruler16.png, Ruler32.png).
        /// </summary>
        public static T WithIcon<T>(this T button, string icon) where T : RibbonButton
        {
            button.SetImage(Application.IconPath(icon, 16));
            button.SetLargeImage(Application.IconPath(icon, 32));
            return button;
        }

        /// <summary>
        ///     Подсказка и, если есть, ссылка на страницу справки (открывается по F1).
        /// </summary>
        public static T WithInfo<T>(this T item, string toolTip, string? helpPage = null) where T : RibbonItem
        {
            item.SetToolTip(toolTip);
            if (helpPage is not null) item.SetContextualHelp(Application.Help(helpPage));
            return item;
        }
    }
}
