namespace QuantityCheck.Dictionaries;

public class CategorySets
{
    public static readonly Dictionary<string, BuiltInCategory[]> Sets =
        new Dictionary<string, BuiltInCategory[]>
        {
            {
                "Штучные", new[]
                {
                    BuiltInCategory.OST_GenericModel,
                    BuiltInCategory.OST_MechanicalEquipment,
                    BuiltInCategory.OST_PipeFitting,
                    BuiltInCategory.OST_PipeAccessory,
                    BuiltInCategory.OST_PlumbingFixtures,
                    BuiltInCategory.OST_DuctFitting,
                    BuiltInCategory.OST_DuctAccessory,
                    BuiltInCategory.OST_ElectricalEquipment,
                    BuiltInCategory.OST_CableTrayFitting,
                    BuiltInCategory.OST_SecurityDevices,
                    BuiltInCategory.OST_CommunicationDevices,
                    BuiltInCategory.OST_FireAlarmDevices,
                    BuiltInCategory.OST_NurseCallDevices,
                    BuiltInCategory.OST_LightingDevices,
                    BuiltInCategory.OST_LightingFixtures,
                    BuiltInCategory.OST_DataDevices,
                    BuiltInCategory.OST_Sprinklers,
                    BuiltInCategory.OST_ElectricalFixtures, 
                    BuiltInCategory.OST_DuctTerminal
                }
            },
            {
                "Длинововые", new[]
                {
                    BuiltInCategory.OST_PipeInsulations,
                    BuiltInCategory.OST_PipeCurves,
                    BuiltInCategory.OST_FlexPipeCurves,
                    BuiltInCategory.OST_DuctInsulations,
                    BuiltInCategory.OST_DuctCurves,
                    BuiltInCategory.OST_FlexDuctCurves,
                    BuiltInCategory.OST_CableTray
                }
            }
        };
}