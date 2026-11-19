using ClassifierCode.Engine.Profile;

namespace ClassifierCode.Services;

/// <summary>Параметр поля профиля у элемента: по GUID общего параметра, если он задан, иначе по имени.</summary>
public static class OutputParameters
{
    public static Parameter? Find(Element element, OutputField output) =>
        output.Guid is { } guid ? element.get_Parameter(guid) ?? element.LookupParameter(output.Parameter) : element.LookupParameter(output.Parameter);
}
