using System;
using System.Collections.Generic;
using System.IO;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;

namespace DoorSide
{
    public class SharedParametersManager
    {
        private readonly Document _doc;

        public SharedParametersManager(Document doc)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
        }

        public void CreateSharedParameter(
            string parameterName,
#if REVIT2022_OR_GREATER
            ForgeTypeId parameterTypeId, // или ParameterType для старых версий 
#else
            ParameterType parameterTypeId,
#endif
            IList<BuiltInCategory> categories,
#if REVIT2024_OR_GREATER
            ForgeTypeId parameterGroup,
#else
            BuiltInParameterGroup parameterGroup,
#endif
            bool isInstance,
            bool isUserModifiable,
            bool isVisible,
            string definitionGroupName = "Custom",
            bool createTempFile = false,
            Guid? guid = null)
        {
            string? tempFilePath = null;

            try
            {
                Application app = _doc.Application;
                DefinitionFile definitionFile = app.OpenSharedParameterFile();

                if (definitionFile == null || createTempFile)
                {
                    tempFilePath = Path.GetTempFileName() + ".txt"; // Важно добавить .txt
                    using (File.Create(tempFilePath)) { }
                    app.SharedParametersFilename = tempFilePath;
                    definitionFile = app.OpenSharedParameterFile();
                }

                DefinitionGroup definitionGroup = GetOrCreateDefinitionGroup(definitionFile, definitionGroupName);

                ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(parameterName, parameterTypeId)
                {
                    UserModifiable = isUserModifiable,
                    Visible = isVisible
                };

                if (guid.HasValue)
                    options.GUID = guid.Value;

                Definition definition = definitionGroup.Definitions.Create(options);

                // ИСПРАВЛЕННАЯ ЧАСТЬ: Правильное создание CategorySet
                CategorySet categorySet = new CategorySet(); // Не через Application!
                
                foreach (BuiltInCategory bic in categories)
                {
                    Category category = _doc.Settings.Categories.get_Item(bic);
                    if (category != null)
                        categorySet.Insert(category);
                }

                Binding binding = isInstance
                    ? (Binding)new InstanceBinding(categorySet)
                    : (Binding)new TypeBinding(categorySet);

                using (Transaction tx = new Transaction(_doc, "Add Shared Parameter"))
                {
                    tx.Start();
                    // Важно: Insert возвращает результат, проверьте его
                    bool result = _doc.ParameterBindings.Insert(definition, binding, parameterGroup);
                    _doc.Regenerate();
                    tx.Commit();
                    
                    if (!result)
                    {
                        // Параметр уже существует или другая ошибка
                    }
                }
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempFilePath))
                {
                    try
                    {
                        File.Delete(tempFilePath);
                        _doc.Application.SharedParametersFilename = null;
                    }
                    catch { /* игнорируем */ }
                }
            }
        }

        public bool DoesParameterExist(string parameterName)
        {
            DefinitionBindingMapIterator iterator = _doc.ParameterBindings.ForwardIterator();

            while (iterator.MoveNext())
            {
                if (iterator.Key is InternalDefinition def && def.Name == parameterName)
                    return true;
            }

            return false;
        }

        public void SetAllowVaryBetweenGroups(string parameterName, bool allow)
        {
            DefinitionBindingMapIterator iterator = _doc.ParameterBindings.ForwardIterator();

            while (iterator.MoveNext())
            {
                if (iterator.Key is InternalDefinition def && def.Name == parameterName)
                {
                    using (Transaction tx = new Transaction(_doc, "Set Allow Vary Between Groups"))
                    {
                        tx.Start();
                        def.SetAllowVaryBetweenGroups(_doc, allow);
                        tx.Commit();
                    }

                    return;
                }
            }
        }

        // ================= PRIVATE HELPERS =================

        private static string CreateTemporarySharedParameterFile(Application app)
        {
            string path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.txt");

            using (File.Create(path)) { }

            app.SharedParametersFilename = path;
            return path;
        }

        private static DefinitionGroup GetOrCreateDefinitionGroup(DefinitionFile definitionFile, string groupName)
        {
            return definitionFile.Groups.get_Item(groupName)
                   ?? definitionFile.Groups.Create(groupName);
        }

        private static Definition CreateDefinition(
            DefinitionGroup group,
            string name,
#if REVIT2022_OR_GREATER
            ForgeTypeId typeId, // или ParameterType для старых версий
#else
            ParameterType typeId,
#endif
            bool isUserModifiable,
            bool isVisible,
            Guid? guid)
        {
            ExternalDefinitionCreationOptions options = new ExternalDefinitionCreationOptions(name, typeId)
            {
                UserModifiable = isUserModifiable,
                Visible = isVisible
            };

            if (guid.HasValue)
                options.GUID = guid.Value;

            return group.Definitions.Create(options);
        }

        private CategorySet BuildCategorySet(IList<BuiltInCategory> categories)
        {
            CategorySet categorySet = _doc.Application.Create.NewCategorySet();

            foreach (BuiltInCategory bic in categories)
            {
                Category category = _doc.Settings.Categories.get_Item(bic);
                categorySet.Insert(category);
            }

            return categorySet;
        }

        private void BindParameterToDocument(Definition definition,
            Binding binding,
#if REVIT2024_OR_GREATER
            ForgeTypeId group
#else
            BuiltInParameterGroup group
#endif
            )
        {
            using (Transaction tx = new Transaction(_doc, "Bind Shared Parameter"))
            {
                tx.Start();
                _doc.ParameterBindings.Insert(definition, binding, group);
                tx.Commit();
            }
        }

        private void CleanupTempFile(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            try
            {
                File.Delete(path);
                _doc.Application.SharedParametersFilename = null;
            }
            catch
            {
                // похуй, временный файл
            }
        }
        
    }
}
