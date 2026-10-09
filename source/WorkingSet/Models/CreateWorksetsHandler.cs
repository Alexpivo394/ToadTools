using Autodesk.Revit.UI;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace WorkingSet.Models
{
    public class CreateWorksetsHandler : IExternalEventHandler
    {
        
        public List<string> Worksets { get; set; } = new();

        public void Execute(UIApplication app)
        {
            var doc = app.ActiveUIDocument.Document;

            using (var transaction = new Transaction(doc, "Create Worksets"))
            {
                transaction.Start();

                foreach (var worksetName in Worksets)
                {
                    if (WorksetTable.IsWorksetNameUnique(doc, worksetName))
                    {
                        Workset.Create(doc, worksetName);
                    }
                }

                transaction.Commit();
            }
        }

        public string GetName() => "Create Worksets Handler";
    }
}
