using System.Text;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Mechanical;
using Nice3point.Revit.Toolkit;
using ToadTools.UI.Models;
using ToadTools.UI.Services;

namespace WarmSync;

    public class RenameSpaces
    {
        public void Run()
        {
            var uiDoc = RevitContext.ActiveUiDocument!;
            var doc = uiDoc.Document;

            int updatedCount = 0;
            int errorCount = 0;
            List<string> log = new();

            using (Transaction t = new Transaction(doc, "Sync Space Numbers"))
            {
                try
                {
                    t.Start();

                    var linkInstances = new FilteredElementCollector(doc)   
                        .OfClass(typeof(RevitLinkInstance))
                        .Cast<RevitLinkInstance>()
                        .ToList();

                    var spaces = new FilteredElementCollector(doc)
                        .OfCategory(BuiltInCategory.OST_MEPSpaces)
                        .WhereElementIsNotElementType()
                        .Cast<Space>()
                        .ToList();

                    log.Add($"Найдено пространств: {spaces.Count}");

                    foreach (var linkInstance in linkInstances)
                    {
                        var linkDoc = linkInstance.GetLinkDocument();
                        if (linkDoc == null) continue;

                        if (!linkDoc.Title.ToUpper().Contains("_AR_"))
                            continue;

                        log.Add($"Обработка файла: {linkDoc.Title}");

                        var rooms = new FilteredElementCollector(linkDoc)
                            .OfCategory(BuiltInCategory.OST_Rooms)
                            .WhereElementIsNotElementType()
                            .Cast<Room>()
                            .ToList();

                        log.Add($"Найдено помещений: {rooms.Count}");

                        Transform transform = linkInstance.GetTotalTransform();

                        foreach (var room in rooms)
                        {
                            try
                            {
                                string? roomNumber = GetParam(room, "Номер помещения") 
                                                    ?? GetParam(room, "Номер");

                                if (string.IsNullOrWhiteSpace(roomNumber))
                                {
                                    log.Add("❌ Помещение без номера");
                                    continue;
                                }

                                string corpus = CleanDigits(GetParam(room, "Корпус"));
                                string floor = CleanDigits(RemoveFloorLetters(GetParam(room, "Этаж")));
                                string? apartment = GetParam(room, "Номер квартиры на этаже");

                                string newNumber = CreateSpaceNumber(corpus, floor, apartment, roomNumber);

                                Space? found = FindSpace(room, spaces, transform);
                                if (found != null)
                                {
                                    Parameter p = found.LookupParameter("Номер");
                                    if (p != null && !p.IsReadOnly)
                                    {
                                        p.Set(newNumber);
                                        updatedCount++;
                                        log.Add($"✅ {newNumber}");
                                    }
                                }
                                else
                                {
                                    errorCount++;
                                    log.Add($"❌ Нет пространства для помещения {roomNumber}");
                                }
                            }
                            catch (Exception exRoom)
                            {
                                errorCount++;
                                log.Add($"❌ Ошибка помещения: {exRoom.Message}");
                            }
                        }
                    }

                    t.Commit();
                }
                catch (Exception)
                {
                    throw;
                }
            }

            ShowResult(updatedCount, errorCount, log);
        }

        private string? GetParam(Element e, string name)
        {
            var p = e.LookupParameter(name);
            if (p == null || !p.HasValue) return null;

            return p.StorageType == StorageType.String
                ? p.AsString()
                : p.AsValueString();
        }

        private string CleanDigits(string? s)
        {
            if (s is not { Length: > 0 }) return "";
            return string.Concat(s.Where(char.IsDigit));
        }

        private string RemoveFloorLetters(string? s)
        {
            if (s is not { Length: > 0 }) return "";
            return s.Replace("Э", "").Replace("э", "").Trim();
        }

        private string CreateSpaceNumber(string c, string f, string? a, string? r)
        {
            return string.Concat(new[] { c, f, a, r }.Where(x => !string.IsNullOrEmpty(x)));
        }

        private Space? FindSpace(Room room, List<Space> spaces, Transform tr)
        {
            foreach (var s in spaces)
            {
                var bb = s.get_BoundingBox(null);
                if (bb == null) continue;

                XYZ center = (bb.Min + bb.Max) * 0.5;
                XYZ pt = tr.Inverse.OfPoint(center);

                if (room.IsPointInRoom(pt))
                    return s;
            }
            return null;
        }

        private void ShowResult(int updated, int errors, List<string> log)
        {
            StringBuilder sb = new();
            sb.AppendLine($"Обновлено: {updated}");
            sb.AppendLine($"Ошибок: {errors}");
            sb.AppendLine();

            foreach (var msg in log.Skip(Math.Max(0, log.Count - 20)))
                sb.AppendLine(msg);

            if (log.Count > 20)
                sb.AppendLine($"... и ещё {log.Count - 20} сообщений");

            var dial = ToadDialogService.Show(
                "Результат",
                sb.ToString(),
                DialogButtons.OK,
                DialogIcon.Info
            );
        }
    }