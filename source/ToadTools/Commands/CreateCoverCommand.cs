using System.Diagnostics;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CreateCover.Services;
using CreateCover.ViewModels;
using CreateCover.Views;
using Nice3point.Revit.Toolkit.External;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace ToadTools.Commands;

/// <summary>
///     Places cover families on the cable trays.
/// </summary>
[UsedImplicitly]
[Transaction(TransactionMode.Manual)]
[Regeneration(RegenerationOption.Manual)]
public class CreateCoverCommand : ExternalCommand
{
    private const string FamilyName = "RP_Крышка";

    private const string FamilyPath =
        @"Y:\12-BIM\Стандарт\Общие семейства и мануалы ко всем шаблонам\Семейства ЭОМ, СС\Соединители лотка\RP_Крышка.rfa";

    public override void Execute()
    {
        try
        {
            var doc = Application.ActiveUIDocument.Document;

            var paramService = new GetParamService(doc);
            var familyService = new FamilyService(doc, FamilyName, FamilyPath);
            var getFirstTrayService = new GetFirstTrayService(doc);

            var firstTray = getFirstTrayService.GetFirstTray();
            if (firstTray is null)
            {
                ToadDialogService.Show("Разместить крышки", "В модели нет кабельных лотков — крышки ставить некуда.",
                    DialogButtons.OK, DialogIcon.Warning);
                return;
            }

            // Без семейства крышки ставить нечего; причину FamilyService уже показал.
            var coverSymbol = familyService.GetOrLoadFamilySymbol();
            if (coverSymbol is null) return;

            var paramCreator = new ParamModelCreator(coverSymbol, firstTray, paramService);
            var createService = new CreateCoverService(doc, familyService, FamilyName);

            var viewModel = new CreateCoverViewModel(paramCreator, createService);
            var view = new CreateCoverView(viewModel);

            view.ShowDialog();
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            ToadDialogService.Show("Разместить крышки", $"Не удалось запустить плагин: {e.Message}",
                DialogButtons.OK, DialogIcon.Error);
            Result = Autodesk.Revit.UI.Result.Cancelled;
        }
    }
}
