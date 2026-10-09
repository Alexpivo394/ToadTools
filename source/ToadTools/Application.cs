using Nice3point.Revit.Toolkit.External;
using System.Windows.Media;
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
        public override async Task OnStartupAsync()
        {
            await Host.StartAsync();
            CreateRibbon();
        }

        public override async Task OnShutdownAsync()
        {
            await Host.StopAsync();
        }

        private void CreateRibbon()
        {
            //Добавляем панели
            var panelBim = Application.CreatePanel("BIM", "ToadTools");
            var panelBackgroundBrushDustyBlue =
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(173, 216, 230));
            panelBim.SetTitleBarBackground(panelBackgroundBrushDustyBlue);


            var panelTray = Application.CreatePanel("Лотки", "ToadTools");
            var panelBackgroundBrushMutedOlive =
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(152, 168, 124));
            panelTray.SetTitleBarBackground(panelBackgroundBrushMutedOlive);

            var panelGeneral = Application.CreatePanel("Общие", "ToadTools");
            var panelBackgroundBrushSoftTerracotta =
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(204, 153, 102));
            panelGeneral.SetTitleBarBackground(panelBackgroundBrushSoftTerracotta);
            
            var panelSs = Application.CreatePanel("Сети связи", "ToadTools");
            var panelBackgroundBrushPlum =
                new SolidColorBrush(System.Windows.Media.Color.FromRgb(155, 242, 222));
            panelSs.SetTitleBarBackground(panelBackgroundBrushPlum);
            
            var panelOv = Application.CreatePanel("ОВ", "ToadTools");
            var panelBackgroundBrushRose = new SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 209, 220));
            panelOv.SetTitleBarBackground(panelBackgroundBrushRose);
            
            var panelAr = Application.CreatePanel("АР", "ToadTools");
            var panelBackgroundBrushBezheviy = new SolidColorBrush(System.Windows.Media.Color.FromRgb(245, 245, 220));
            panelAr.SetTitleBarBackground(panelBackgroundBrushBezheviy);
            
            var panelKr = Application.CreatePanel("КР", "ToadTools");
            var panelBackgroundBrushSnowBlue = new SolidColorBrush(System.Windows.Media.Color.FromRgb(172, 229, 238));
            panelKr.SetTitleBarBackground(panelBackgroundBrushSnowBlue);

            //Добавляем кнопки на панели
            //BIM
            panelBim.AddPushButton<Commands.ParamCheckerCommand>("ParamChecker")
                .SetImage("/ToadTools;component/Resources/Icons/Export16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/Export32.png")
                .SetToolTip("Выгружает из моделей на сервере\nотчет по заполнению параметров.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/paramchecker-movFIkYSwy"));

            panelBim.AddPushButton<Commands.WorkingSetCommand>("Создание Рабочих\nнаборов")
                .SetImage("/ToadTools;component/Resources/Icons/Create16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/Create32.png")
                .SetToolTip("Создает в модели рабочие наборы\nдля выбранного раздела.")  
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/sozdanie-rabochih-naborov-plaginom-rptools-j3Ycy39HVW"));
            

            panelBim.AddPushButton<Commands.ModelTransplanterCommand>("Копирование элементов")
                .SetImage("/ToadTools;component/Resources/Icons/Transplanter16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/Transplanter32.png")
                .SetToolTip("Позволяет копировать элементы\nиз модели в модель.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/kopirovanie-soderzhimogo-modeli-7q8Wf296FB"));
            
            panelBim.AddPushButton<Commands.WorksetCheckCommand>("Проверка рабочих\nнаборов")
                .SetImage("/ToadTools;component/Resources/Icons/WorksetsCheck16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/WorksetsCheck32.png")
                .SetToolTip("Позволяет проверить заданные\nмодели на соответствие элементов\nрабочим наборам.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/worksetcheck-rfycq6j0Bd"));

            panelBim.AddPushButton<Commands.ChangeSharedFamiliesCommand>("Заменить общие\nсемейства необщими")
                .SetImage("/ToadTools;component/Resources/Icons/ChangeSharedFamilies16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/ChangeSharedFamilies32.png")
                .SetToolTip("Заменяет общие вложенные семейства\nна необщие. Работает только с\nредактором семейства.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/zamenit-obshie-semejstva-neobshimi-BDbyQvBJH7"));

            //Лотки
            var pullButtonTray = panelTray.AddPullDownButton("Кабельные лотки", "Кабельные лотки");

            pullButtonTray.SetImage("/ToadTools;component/Resources/Icons/1Tray16.png");
            pullButtonTray.SetLargeImage("/ToadTools;component/Resources/Icons/1Tray32.png");
            pullButtonTray.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url,
                "https://bim-baza.yonote.ru/doc/kabelnye-lotki-hSP8BFIW8g"));

            pullButtonTray.AddPushButton<Commands.CableTrayArticulCommand>("Артикулы и наименования");
            pullButtonTray.AddPushButton<Commands.CableTrayColorPerforationCommand>("Покрасить по перфорации");
            pullButtonTray.AddPushButton<Commands.CableTrayColorVariantCommand>("Покрасить по исполнению");
            pullButtonTray.AddPushButton<Commands.CableTrayColorCoverCommand>("Покрасить крышки");
            
            panelTray.AddPushButton<Commands.CreateCoverCommand>("Разместить\nкрышки")
                .SetImage("/ToadTools;component/Resources/Icons/CreateCover16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/CreateCover32.png")
                .SetToolTip("Разместить в модели крышки на кабельные лотки")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/razmestit-kryshki-pJSWjFkyzL"));
            
            //Общие
            panelGeneral.AddPushButton<Commands.LengthCommand>("Длина элементов\nмодели")
                .SetImage("/ToadTools;component/Resources/Icons/Ruler16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/Ruler32.png")
                .SetToolTip("Посчитать суммарную длину выбранных элементов.");
            
            panelGeneral.AddPushButton<Commands.CreateSpacesCommand>("Создание\nпространств")
                .SetImage("/ToadTools;component/Resources/Icons/Spaces16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/Spaces32.png")
                .SetToolTip("Создает в проекте пространства на основе помещений из раздела АР.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/sozdanie-prostranstv-Bia5rDOjqY"));
            
            panelGeneral.AddPushButton<Commands.QuantityCheckCommand>("Записать количество")
                .SetImage("/ToadTools;component/Resources/Icons/QuantityCheck16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/QuantityCheck32.png")
                .SetToolTip("Заполняет параметр количества\nв элементах модели.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/zapolnenie-parametra-kolichestvo-beinN88LwP"));
            
            panelGeneral.AddPushButton<Commands.WriteDashCommand>("Записать прочерк")
                .SetImage("/ToadTools;component/Resources/Icons/WriteDash16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/WriteDash32.png")
                .SetToolTip("Заполняет прочерк в указанные параметры\nесли в них нет значения.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/zapisat-procherk-kfwUDj6TeL"));

            panelGeneral.AddPushButton<Commands.ClassifierCodeCommand>("Код по\nклассификатору")
                .SetImage("/ToadTools;component/Resources/Icons/ClassifierCode16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/ClassifierCode32.png")
                .SetToolTip("Заполняет ADSK_Код по классификатору\nи ADSK_Описание по классификатору\nу элементов ИОС.");

            //Сети связи
            panelSs.AddPushButton<Commands.SSPlanCommand>("Структурная\nсхема")
                .SetImage("/ToadTools;component/Resources/Icons/SSPlan16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/SSPlan32.png")
                .SetToolTip("Создать структурную схему.")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/strukturnaya-shema-yO3TQrgqVS"));
            
            //Панель ОВ
            var pullButtonWarm = panelOv.AddPullDownButton("Теплопотери", "Теплопотери");
            
            pullButtonWarm.SetImage("/ToadTools;component/Resources/Icons/WarmSync16.png");
            pullButtonWarm.SetLargeImage("/ToadTools;component/Resources/Icons/WarmSync32.png");
            pullButtonWarm.SetContextualHelp(new ContextualHelp(ContextualHelpType.Url,
                "https://bim-baza.yonote.ru/doc/sinhronizaciya-teplopoter-RWMUuxZBdq"));
            
            pullButtonWarm.AddPushButton<Commands.RenameSpacesCommand>("Заполнение имен пространств");
            pullButtonWarm.AddPushButton<Commands.WriteSpaceIdToParamCommand>("Запись ID пространств");
            pullButtonWarm.AddPushButton<Commands.ExportSpacesToExcelCommand>("Экспорт пространств в Excel");
            pullButtonWarm.AddPushButton<Commands.WriteFromExcelCommand>("Импорт значений из Excel");
            
            //Панель АР
            
            panelAr.AddPushButton<Commands.DoorSideCommand>("Открывание\nдверей")
                .SetImage("/ToadTools;component/Resources/Icons/DoorSide16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/DoorSide32.png")
                .SetToolTip("Записать сторону открывания двери в параметр Открывание")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/otkryvanie-dveri-JJyZ1390oD"));
            
            panelAr.AddPushButton<Commands.ParamToFinishCommand>("Основание\nотделки")
                .SetImage("/ToadTools;component/Resources/Icons/ParamToFinish16.png")
                .SetLargeImage("/ToadTools;component/Resources/Icons/ParamToFinish32.png")
                .SetToolTip("Позволяет записать основание отделки в выбранный параметр")
                .SetContextualHelp(new ContextualHelp(ContextualHelpType.Url, "https://bim-baza.yonote.ru/doc/osnovanie-otdelki-EcPw4yHPWP"));

            //Панель КР

            var pullButtonRei = panelKr.AddPullDownButton("Армирование", "Армирование");
            
            pullButtonRei.SetImage("/ToadTools;component/Resources/Icons/Rei16.png");
            pullButtonRei.SetLargeImage("/ToadTools;component/Resources/Icons/Rei32.png");
            pullButtonRei.SetToolTip("Размещает семейства армирования взамен цветовых областей");

            pullButtonRei.AddPushButton<Commands.RSHPLeftRightCommand>("R-SHP-Армирование по Y");
            pullButtonRei.AddPushButton<Commands.RSHPUpDownCommand>("R-SHP-Армирование по X");
            pullButtonRei.AddPushButton<Commands.RSUMLeftRightCommand>("R-SUM-Армирование по Y");
            pullButtonRei.AddPushButton<Commands.RSUMUpDownCommand>("R-SUM-Армирование по X");


        }        

    }
}