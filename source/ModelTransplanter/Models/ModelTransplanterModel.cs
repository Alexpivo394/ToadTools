using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using ModelTransplanter.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ModelTransplanter.Models
{
    public class ElementTransferService
    {
        private readonly Logger _logger;
        private readonly List<BuiltInCategory> _targetCategories = new List<BuiltInCategory>
        {
            BuiltInCategory.OST_Rooms,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Ceilings,
            BuiltInCategory.OST_CurtainWallPanels,
            BuiltInCategory.OST_Doors,
            BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_GenericModel,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Roofs,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_Parking,
            BuiltInCategory.OST_MechanicalEquipment,
            BuiltInCategory.OST_Furniture,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_PipeInsulations,
            BuiltInCategory.OST_PipeCurves,
            BuiltInCategory.OST_FlexPipeCurves,
            BuiltInCategory.OST_PipeFitting,
            BuiltInCategory.OST_PipeAccessory,
            BuiltInCategory.OST_PlumbingFixtures,
            BuiltInCategory.OST_PipingSystem,
            BuiltInCategory.OST_DuctInsulations,
            BuiltInCategory.OST_DuctCurves,
            BuiltInCategory.OST_FlexDuctCurves,
            BuiltInCategory.OST_DuctFitting,
            BuiltInCategory.OST_DuctAccessory,
            BuiltInCategory.OST_ElectricalEquipment,
            BuiltInCategory.OST_CableTray,
            BuiltInCategory.OST_CableTrayFitting,
            BuiltInCategory.OST_SecurityDevices,
            BuiltInCategory.OST_CommunicationDevices,
            BuiltInCategory.OST_FireAlarmDevices,
            BuiltInCategory.OST_NurseCallDevices,
            BuiltInCategory.OST_LightingDevices,
            BuiltInCategory.OST_LightingFixtures,
            BuiltInCategory.OST_DataDevices,
            BuiltInCategory.OST_Sprinklers,
            BuiltInCategory.OST_Parts,
            BuiltInCategory.OST_ElectricalFixtures,
            BuiltInCategory.OST_DuctTerminal,
            BuiltInCategory.OST_DuctSystem
        };

        public ElementTransferService(Logger logger)
        {
            _logger = logger;
        }

        public void TransferElements(Document sourceDoc, Document targetDoc, IProgress<int> progress)
        {
            try
            {
                _logger.Log("🚀 Начало переноса элементов с учетом общего положения");

                var elementsToCopy = GetValidElementsForTransfer(sourceDoc);
                _logger.Log($"🔍 Найдено элементов для переноса: {elementsToCopy.Count}");

                if (elementsToCopy.Count == 0)
                {
                    _logger.Log("ℹ Нет элементов для переноса");
                    return;
                }

                // Получаем полную трансформацию между системами координат
                Transform transform = GetCoordinateSystemTransform(sourceDoc, targetDoc);

                var options = new CopyPasteOptions();
                options.SetDuplicateTypeNamesHandler(new UseDestinationTypeHandler());

                using (var t = new Transaction(targetDoc, "Перенос элементов"))
                {
                    t.Start();
                    try
                    {
                        ElementTransformUtils.CopyElements(
                            sourceDoc,
                            elementsToCopy.Select(e => e.Id).ToList(),
                            targetDoc,
                            transform,
                            options);

                        t.Commit();
                        _logger.Log($"🎉 Перенесено элементов: {elementsToCopy.Count}");
                    }
                    catch (Exception ex)
                    {
                        t.RollBack();
                        _logger.LogError("❌ Ошибка при копировании", ex);
                        throw;
                    }
                }

                // Регенерация документа после переноса
                using (var tRegen = new Transaction(targetDoc, "Регенерация документа"))
                {
                    tRegen.Start();
                    try
                    {
                        targetDoc.Regenerate();
                        tRegen.Commit();
                        _logger.Log("🔄 Модель успешно регенерирована");
                    }
                    catch (Exception ex)
                    {
                        tRegen.RollBack();
                        _logger.LogError("❌ Ошибка при регенерации", ex);
                        throw;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("Фатальная ошибка", ex);
                throw;
            }
        }

        private Transform GetCoordinateSystemTransform(Document sourceDoc, Document targetDoc)
        {
            // 1. Получаем точки съемки
            BasePoint? sourceSurvey = GetSurveyPoint(sourceDoc);
            BasePoint? targetSurvey = GetSurveyPoint(targetDoc);

            if (sourceSurvey == null || targetSurvey == null)
                throw new InvalidOperationException("Не найдены точки съемки в одном из документов");

            // 2. Получаем трансформации внутреннего начала координат
            Transform sourceInternal = GetInternalOriginTransform(sourceDoc);
            Transform targetInternal = GetInternalOriginTransform(targetDoc);

            // 3. Получаем трансформации истинного севера
            Transform sourceNorth = GetTrueNorthTransform(sourceDoc);
            Transform targetNorth = GetTrueNorthTransform(targetDoc);

            // 4. Комбинируем все трансформации
            Transform combinedTransform = targetInternal.Inverse
                .Multiply(targetNorth)
                .Multiply(sourceNorth.Inverse)
                .Multiply(sourceInternal);

            _logger.Log($"📍 Применяемая трансформация:");
            _logger.Log($"   Смещение: {combinedTransform.Origin}");
            _logger.Log($"   Ось X: {combinedTransform.BasisX}");
            _logger.Log($"   Ось Y: {combinedTransform.BasisY}");
            _logger.Log($"   Ось Z: {combinedTransform.BasisZ}");

            return combinedTransform;
        }

        private Transform GetInternalOriginTransform(Document doc)
        {
            BasePoint survey = GetSurveyPoint(doc)
                               ?? throw new InvalidOperationException($"Не найдена точка съемки в документе {doc.Title}");
            XYZ position = survey.Position;

            _logger.Log($"📌 Точка съемки документа {doc.Title}:");
            _logger.Log($"   Позиция: {position}");
            _logger.Log($"   Смещение от внутреннего начала: {position.Negate()}");

            return Transform.CreateTranslation(position.Negate());
        }

        private Transform GetTrueNorthTransform(Document doc)
        {
            ProjectLocation location = doc.ActiveProjectLocation;
            SiteLocation site = location.GetSiteLocation();

            // Получаем угол через параметр
            Parameter angleParam = site.get_Parameter(BuiltInParameter.BASEPOINT_ANGLETON_PARAM);
            if (angleParam == null || !angleParam.HasValue)
            {
                _logger.Log("⚠ Угол истинного севера не задан, используется 0");
                return Transform.Identity;
            }

            double angle = angleParam.AsDouble();

            _logger.Log($"🧭 Истинный север документа {doc.Title}:");
            _logger.Log($"   Угол поворота: {angle} радиан ({angle * 180 / Math.PI} градусов)");

            return Transform.CreateRotation(XYZ.BasisZ, angle);
        }

        private BasePoint? GetSurveyPoint(Document doc)
        {
            return new FilteredElementCollector(doc)
                .OfClass(typeof(BasePoint))
                .Cast<BasePoint>()
                .FirstOrDefault(bp => bp.IsShared);
        }

        private List<Element> GetValidElementsForTransfer(Document doc)
        {
            var filters = _targetCategories
                .Select(c => new ElementCategoryFilter(c) as ElementFilter)
                .ToList();

            var orFilter = new LogicalOrFilter(filters);

            return new FilteredElementCollector(doc)
                .WhereElementIsNotElementType()
                .WherePasses(orFilter)
                .Where(e => !IsCoordinateElement(e))
                .Where(e => IsElementValidForTransfer(e))
                .ToList();
        }

        private bool IsCoordinateElement(Element element)
        {
            return element is Grid ||
                   element is Level ||
                   element is ReferencePlane ||
#if REVIT2024_OR_GREATER
                   element.Category?.Id.Value == (int)BuiltInCategory.OST_Grids ||
                   element.Category?.Id.Value == (int)BuiltInCategory.OST_Levels;
#else
                    element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Grids ||
                    element.Category?.Id.IntegerValue == (int)BuiltInCategory.OST_Levels;

#endif
        }

        private bool IsElementValidForTransfer(Element element)
        {
            try
            {
                if (element == null || element.Id == null || element.Id == ElementId.InvalidElementId)
                    return false;

                if (element.IsValidObject == false)
                    return false;

                var options = new Options();
                var geometry = element.get_Geometry(options);

                if (geometry == null && !(element is Room))
                    return false;

                return true;
            }
            catch
            {
                return false;
            }
        }

        private class UseDestinationTypeHandler : IDuplicateTypeNamesHandler
        {
            public DuplicateTypeAction OnDuplicateTypeNamesFound(DuplicateTypeNamesHandlerArgs args)
            {
                return DuplicateTypeAction.UseDestinationTypes;
            }
        }

        private class SuppressWarnings : IFailuresPreprocessor
        {
            public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            {
                var failures = failuresAccessor.GetFailureMessages();
                foreach (var f in failures)
                {
                    if (f.GetSeverity() == FailureSeverity.Warning)
                    {
                        failuresAccessor.DeleteWarning(f);
                    }
                }
                return FailureProcessingResult.Continue;
            }
        }
    }
}