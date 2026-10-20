using System.Diagnostics;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Wpf.Ui.Controls;
using CreateCover.Models;
using ToadTools.UI.Models;
using ToadTools.UI.Progress;
using ToadTools.UI.Services;

namespace CreateCover.Services;

public class CreateCoverService
{
    private Document _doc;
    private FamilyService _familyService;
    private string _familyName;

    public CreateCoverService(Document doc, FamilyService familyService, string familyName)
    {
        _doc = doc;
        _familyService = familyService;
        _familyName = familyName;
    }

    public void Create(List<ParamModel> paramModels)
    {
        var coverParamName = "ADSK_Крышка";
        var articulParamName = "ADSK_Исполнение";

        var trays = new FilteredElementCollector(_doc).OfCategory(BuiltInCategory.OST_CableTray)
            .WhereElementIsNotElementType()
            .Cast<CableTray>()
            .ToList();

        var coverSymbol = _familyService.GetOrLoadFamilySymbol();

        if (coverSymbol == null)
        {
            ToadDialogService.Show("Разместить крышки", $"Не удалось найти или загрузить семейство {_familyName}",
                DialogButtons.OK, DialogIcon.Error);
            return;
        }

        var collector = new FilteredElementCollector(_doc).OfClass(typeof(FamilyInstance))
            .WhereElementIsNotElementType();

        // Удаление старых крышек и создание новых — одна группа: «Прервать» откатывает и то и другое.
        using (var progress = ToadProgress.Show("Разместить крышки",
                   new ProgressOptions { Icon = SymbolRegular.LayerDiagonal24 }))
        using (var trGr = new TransactionGroup(_doc, "Добавление крышек"))
        {
            trGr.Start();
            progress.Stage("Удаление старых крышек");

            using (var tx = new Transaction(_doc, "Удалить крышки"))
            {
                tx.Start();

                var idsToDelete = new List<ElementId>();

                foreach (var elem in collector)
                {
                    if (elem is FamilyInstance fi)
                    {
                        var family = fi.Symbol.Family;
                        if (family.Name == _familyName)
                        {
                            idsToDelete.Add(fi.Id);
                        }
                    }
                }

                if (idsToDelete.Count > 0)
                {
                    _doc.Delete(idsToDelete);
                }

                tx.Commit();
            }

            if (coverSymbol != null && !coverSymbol.IsActive)
            {
                coverSymbol.Activate();
            }

            foreach (var tray in progress.Track(trays, "Создание крышек"))
            {
                try
                {
                    var coverParam = tray.LookupParameter(coverParamName);
                    if (coverParam == null || coverParam.AsInteger() != 1) continue;

                    var width = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsDouble();
                    var height = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsDouble();

                    if (tray.Location is not LocationCurve loc)
                    {
                        continue;
                    }

                    var length = loc.Curve.Length;

                    var start = loc.Curve.GetEndPoint(0);
                    var end = loc.Curve.GetEndPoint(1);
                    var midpoint = (start + end) / 2;

                    FamilyInstance instance = _doc.RunInTransaction("Создание крышек",
                        () => _doc.Create.NewFamilyInstance(midpoint, coverSymbol, StructuralType.NonStructural));

                    var line = loc?.Curve as Line;

                    if (line != null)
                    {
                        var trayDirection = line.Direction.Normalize();

                        RotateCupOnTrayDirection(_doc, tray, instance, trayDirection);
                        RotateCupByTrayNormal(_doc, tray, instance, trayDirection);
                    }

                    _doc.RunInTransaction("Установка размера крышек", () =>
                    {
                        SetParam(instance, "ADSK_Размер_Ширина", width);
                        SetParam(instance, "ADSK_Размер_Высота", height);
                        SetParam(instance, "ADSK_Размер_Длина", length);
                    });

                    var ispol = tray.LookupParameter(articulParamName).AsInteger();

                    var widthStr = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_WIDTH_PARAM).AsValueString();
                    var heightStr = tray.get_Parameter(BuiltInParameter.RBS_CABLETRAY_HEIGHT_PARAM).AsValueString();

                    var artikul = "";
                    var shirina50 = "50";
                    var shirina80 = "80";
                    var shirina100 = "100";
                    var shirina150 = "150";
                    var shirina200 = "200";
                    var shirina300 = "300";
                    var shirina400 = "400";
                    var shirina500 = "500";
                    var shirina600 = "600";
                    var name = "имя не найдено";

                    if (ispol == 1)
                    {
                        if (widthStr == shirina50) artikul = "35520";
                        if (widthStr == shirina80) artikul = "35521";
                        if (widthStr == shirina100) artikul = "35522";
                        if (widthStr == shirina150) artikul = "35523";
                        if (widthStr == shirina200) artikul = "35524";
                        if (widthStr == shirina300) artikul = "35525";
                        if (widthStr == shirina400) artikul = "35526";
                        if (widthStr == shirina500) artikul = "35527";
                        if (widthStr == shirina600) artikul = "35528";
                    }
                    else if (ispol == 2)
                    {
                        if (widthStr == shirina50) artikul = "35520HDZ";
                        if (widthStr == shirina80) artikul = "35521HDZ";
                        if (widthStr == shirina100) artikul = "35522HDZ";
                        if (widthStr == shirina150) artikul = "35523HDZ";
                        if (widthStr == shirina200) artikul = "35524HDZ";
                        if (widthStr == shirina300) artikul = "35525HDZ";
                        if (widthStr == shirina400) artikul = "35526HDZ";
                        if (widthStr == shirina500) artikul = "35527HDZ";
                        if (widthStr == shirina600) artikul = "35528HDZ";
                    }
                    else if (ispol == 3)
                    {
                        if (widthStr == shirina50) artikul = "35520ZL";
                        if (widthStr == shirina80) artikul = "35521ZL";
                        if (widthStr == shirina100) artikul = "35522ZL";
                        if (widthStr == shirina150) artikul = "35523ZL";
                        if (widthStr == shirina200) artikul = "35524ZL";
                        if (widthStr == shirina300) artikul = "35525ZL";
                        if (widthStr == shirina400) artikul = "35526ZL";
                        if (widthStr == shirina500) artikul = "35527ZL";
                        if (widthStr == shirina600) artikul = "35528ZL";
                    }
                    else
                    {
                        artikul = "Исполнение не найдено";
                    }

                    if (artikul != "Исполнение не найдено")
                    {
                        name = "Крышка на прямой элемент " + widthStr + "x" + heightStr;
                        if (ispol == 1) name = name + ", оцинковка горячим способом по методу Сендзимира";
                        if (ispol == 2)
                        {
                            name = name + ", горячего цинкования";
                        }

                        if (ispol == 3)
                        {
                            name = name + ", из нержавеющей стали";
                        }
                    }
                    else
                    {
                        name = "Исполнение не найдено";
                    }

                    if (artikul == "")
                    {
                        artikul = "Уберите обозначение единиц у размера кабельного лотка в единицах проекта";
                    }

                    _doc.RunInTransaction("Заполнение артикула", () =>
                    {
                        instance.LookupParameter("ADSK_Код изделия").Set(artikul);
                        instance.LookupParameter("ADSK_Наименование").Set(name);
                        instance.LookupParameter(articulParamName).Set(ispol);
                    });

                    _doc.RunInTransaction("Перенос пользовательских параметров", () =>
                    {
                        foreach (var model in paramModels)
                        {
                            Copy(tray, model.SelectedTrayParam, instance, model.SelectedCoverParam);
                        }
                    });
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }

            trGr.Commit();
            
        }
        
        var dial = ToadDialogService.Show(
            "Успех!",
            "Крышки созданы.",
            DialogButtons.OK,
            DialogIcon.Info
        );
    }

    private void SetParam(FamilyInstance instance, string paramName, double valueInternal)
    {
        var p = instance.LookupParameter(paramName);
        if (p != null && !p.IsReadOnly)
        {
            p.Set(valueInternal);
        }
    }

    private void RotateCupByTrayNormal(Document doc, CableTray tray, FamilyInstance famInstance, XYZ trayDirection)
    {
        var locPoint = famInstance.Location as LocationPoint;
        var instanceDirection2 = famInstance.HandOrientation;
        var facingOrientation = famInstance.FacingOrientation;

        var cupNormal = instanceDirection2.CrossProduct(facingOrientation).Normalize();
        var trayNormal = tray.CurveNormal;

        var normal2 = cupNormal.CrossProduct(trayNormal).Normalize();
        if (normal2.IsAlmostEqualTo(XYZ.Zero))
        {
            normal2 = trayDirection.Normalize();
        }

        var angle2 = trayNormal.AngleOnPlaneTo(cupNormal, trayDirection);

        double sign = Math.Sign(trayDirection.DotProduct(cupNormal.CrossProduct(trayNormal)));
        if (sign == 0) sign = 1;

        var axe2 = Line.CreateBound(locPoint?.Point, locPoint?.Point + normal2);

        doc.RunInTransaction("Поворот1",
            () => ElementTransformUtils.RotateElement(doc, famInstance.Id, axe2, -angle2 * sign));
    }

    private void RotateCupOnTrayDirection(Document doc, CableTray tray, FamilyInstance famInstance, XYZ trayDirection)
    {
        var locPoint = famInstance.Location as LocationPoint;

        var instanceDirection = famInstance.HandOrientation;

        // нормаль к плоскости в которой лежат направления короба и крышки
        var normal = trayDirection.CrossProduct(instanceDirection).Normalize();
        if (normal.IsAlmostEqualTo(XYZ.Zero))
        {
            normal = instanceDirection.Z == 1 ? XYZ.BasisX : XYZ.BasisZ;
        }

        // угол между коробом и крышкой
        var angle = trayDirection.AngleOnPlaneTo(instanceDirection, normal);

        var axe = Line.CreateBound(locPoint?.Point, locPoint?.Point + normal);

        doc.RunInTransaction("Поворот2", () => ElementTransformUtils.RotateElement(doc, famInstance.Id, axe, -angle));
    }

    public static void Copy(Element from, ParameterDescriptor? fromParam, Element to, ParameterDescriptor? toParam)
    {
        var src = from.LookupParameter(fromParam?.Name);
        var dst = to.LookupParameter(toParam?.Name);

        if (src == null || dst == null || dst.IsReadOnly) return;

        if (src.StorageType != dst.StorageType) return;

        switch (src.StorageType)
        {
            case StorageType.Double:
                dst.Set(src.AsDouble());
                break;

            case StorageType.Integer:
                dst.Set(src.AsInteger());
                break;

            case StorageType.String:
                dst.Set(src.AsString());
                break;

            case StorageType.ElementId:
                dst.Set(src.AsElementId());
                break;
        }
    }
}