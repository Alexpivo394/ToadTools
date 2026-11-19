using System.Text;
using Autodesk.Revit.DB.Mechanical;
using ClassifierCode.Engine.Facts;

namespace ClassifierCode.Services;

/// <summary>
///     Элемент Revit → факты для движка. Параметры читаются лениво и кэшируются, родитель и изолируемый элемент —
///     по запросу. Тексты собираются ровно так, как их собирал прежний ElementClassifier.
/// </summary>
public sealed class FactsBuilder(Document doc)
{
    private readonly Dictionary<ElementId, ElementFacts> _cache = new();
    private readonly WorksetTable? _worksets = doc.IsWorkshared ? doc.GetWorksetTable() : null;

    public Document Document => doc;

    public IElementFacts Get(Element element)
    {
        if (!_cache.TryGetValue(element.Id, out var facts))
        {
            facts = new ElementFacts(this, element);
            _cache[element.Id] = facts;
        }

        return facts;
    }

    public IElementFacts? Get(ElementId id) => doc.GetElement(id) is { } element ? Get(element) : null;

    private static readonly HashSet<BuiltInCategory> AirCategories =
    [
        BuiltInCategory.OST_DuctCurves, BuiltInCategory.OST_FlexDuctCurves, BuiltInCategory.OST_DuctFitting, BuiltInCategory.OST_DuctAccessory,
        BuiltInCategory.OST_DuctTerminal, BuiltInCategory.OST_DuctInsulations, BuiltInCategory.OST_DuctLinings
    ];

    private static readonly HashSet<BuiltInCategory> PipeCategories =
    [
        BuiltInCategory.OST_PipeCurves, BuiltInCategory.OST_FlexPipeCurves, BuiltInCategory.OST_PipeFitting, BuiltInCategory.OST_PipeAccessory,
        BuiltInCategory.OST_PipeInsulations, BuiltInCategory.OST_Sprinklers, BuiltInCategory.OST_PlumbingFixtures
    ];

    /// <summary>Строка параметра, как прежний GetString: текст для строковых, AsValueString для остальных.</summary>
    public static string GetString(Parameter? parameter)
    {
        if (parameter == null || !parameter.HasValue) return string.Empty;
        var value = parameter.StorageType == StorageType.String ? parameter.AsString() : parameter.AsValueString();
        return value ?? string.Empty;
    }

    private sealed class ElementFacts : IElementFacts
    {
        private readonly FactsBuilder _owner;
        private readonly Element _element;
        private readonly Element? _type;
        private readonly Dictionary<string, string> _instance = new();
        private readonly Dictionary<string, string> _typeParameters = new();
        private string? _systemText;
        private NetworkDomain? _domain;
        private List<MEPSystemType>? _systemTypes;

        public ElementFacts(FactsBuilder owner, Element element)
        {
            _owner = owner;
            _element = element;
            _type = owner.Document.GetElement(element.GetTypeId());
            var category = RevitCompat.CategoryOf(element);
            Category = category != BuiltInCategory.INVALID ? category.ToString() : string.Empty;
        }

        public long Id => RevitCompat.IdValue(_element.Id);
        public string Category { get; }
        public bool HasType => _type is ElementType;
        public string InstanceName => _element.Name ?? string.Empty;
        public string FamilyName => (_type as ElementType)?.FamilyName ?? string.Empty;
        public string TypeName => _type is ElementType type ? type.Name ?? string.Empty : string.Empty;

        public string InstanceParameter(string name) => Read(_instance, _element, name);
        public string TypeParameter(string name) => _type == null ? string.Empty : Read(_typeParameters, _type, name);

        private static string Read(Dictionary<string, string> cache, Element element, string name)
        {
            if (!cache.TryGetValue(name, out var value))
            {
                value = GetString(element.LookupParameter(name));
                cache[name] = value;
            }

            return value;
        }

        public IEnumerable<string> InstanceStringValues() => StringValues(_element);
        public IEnumerable<string> TypeStringValues() => _type == null ? [] : StringValues(_type);

        private static IEnumerable<string> StringValues(Element element)
        {
            foreach (Parameter parameter in element.Parameters)
            {
                if (parameter.StorageType != StorageType.String) continue;
                var value = parameter.AsString();
                if (!string.IsNullOrEmpty(value)) yield return value!;
            }
        }

        public string InstanceComments => GetString(_element.get_Parameter(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS));
        public string TypeComments => _type == null ? string.Empty : GetString(_type.get_Parameter(BuiltInParameter.ALL_MODEL_TYPE_COMMENTS));

        public string? Workset => _owner._worksets?.GetWorkset(_element.WorksetId)?.Name;
        public string DocumentTitle => _owner.Document.Title;

        public IElementFacts? Parent => _element is FamilyInstance { SuperComponent: { } parent } ? _owner.Get(parent) : null;

        public IElementFacts? Host => _element is InsulationLiningBase insulation ? _owner.Get(insulation.HostElementId) : null;

        public NetworkDomain Domain => _domain ??= ComputeDomain();

        public string SystemText
        {
            get
            {
                if (_systemText == null) BuildSystemText();
                return _systemText!;
            }
        }

        /// <summary>Как прежний GetDomain: по категории, иначе по подключённым системам.</summary>
        private NetworkDomain ComputeDomain()
        {
            var category = RevitCompat.CategoryOf(_element);
            if (AirCategories.Contains(category)) return NetworkDomain.Air;
            if (PipeCategories.Contains(category)) return NetworkDomain.Pipe;

            if (_systemText == null) BuildSystemText();
            if (_systemTypes!.Count == 0) return NetworkDomain.Other;
            return _systemTypes.Any(t => t is MechanicalSystemType) ? NetworkDomain.Air : NetworkDomain.Pipe;
        }

        /// <summary>Встроенная часть прежнего GetSystemText: системы, их типы, системные параметры. ADSK-параметры добавляет профиль.</summary>
        private void BuildSystemText()
        {
            var sb = new StringBuilder();
            var types = new List<MEPSystemType>();
            var seen = new HashSet<ElementId>();
            var doc = _owner.Document;

            foreach (var system in GetSystems(_element))
            {
                sb.Append(system.Name).Append(" | ");
                AddSystemType(doc.GetElement(system.GetTypeId()) as MEPSystemType, sb, types, seen);
            }

            AddSystemType(TypeFromParameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM), sb, types, seen);
            AddSystemType(TypeFromParameter(BuiltInParameter.RBS_DUCT_SYSTEM_TYPE_PARAM), sb, types, seen);

            sb.Append(GetString(_element.get_Parameter(BuiltInParameter.RBS_SYSTEM_NAME_PARAM))).Append(" | ");
            sb.Append(GetString(_element.get_Parameter(BuiltInParameter.RBS_DUCT_PIPE_SYSTEM_ABBREVIATION_PARAM))).Append(" | ");
            sb.Append(GetString(_element.get_Parameter(BuiltInParameter.RBS_SYSTEM_CLASSIFICATION_PARAM)));

            _systemText = sb.ToString();
            _systemTypes = types;
        }

        private MEPSystemType? TypeFromParameter(BuiltInParameter parameter)
        {
            var p = _element.get_Parameter(parameter);
            if (p == null || p.StorageType != StorageType.ElementId) return null;
            return _owner.Document.GetElement(p.AsElementId()) as MEPSystemType;
        }

        private static void AddSystemType(MEPSystemType? type, StringBuilder sb, List<MEPSystemType> types, HashSet<ElementId> seen)
        {
            if (type == null || !seen.Add(type.Id)) return;
            types.Add(type);
            sb.Append(type.Name).Append(" | ").Append(type.Abbreviation).Append(" | ");
        }

        private static List<MEPSystem> GetSystems(Element element)
        {
            var systems = new List<MEPSystem>();
            if (element is MEPCurve curve)
            {
                if (curve.MEPSystem != null) systems.Add(curve.MEPSystem);
                return systems;
            }

            if (element is not FamilyInstance { MEPModel.ConnectorManager: { } connectors }) return systems;

            foreach (Connector connector in connectors.Connectors)
            {
                if (connector.Domain != Autodesk.Revit.DB.Domain.DomainPiping && connector.Domain != Autodesk.Revit.DB.Domain.DomainHvac) continue;
                try
                {
                    var system = connector.MEPSystem;
                    if (system != null && !systems.Exists(s => s.Id == system.Id)) systems.Add(system);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException)
                {
                    // У некоторых коннекторов система недоступна — пропускаем.
                }
            }

            return systems;
        }
    }
}
