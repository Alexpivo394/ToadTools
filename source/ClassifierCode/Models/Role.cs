namespace ClassifierCode.Models;

/// <summary>Роль элемента внутри раздела (последний уровень классификатора).</summary>
public enum Role
{
    None,
    Equipment,
    PipeGroup,          // "Трубы" — сюда же крепёж, гильзы, окраска
    Pipe,
    PipeFitting,
    Armature,
    Kip,
    Collector,
    Mixing,
    Insulation,
    Sprinkler,
    FireCabinet,
    PumpAutomation,
    Trap,
    SanitaryFixture,
    Funnel,
    HeatingDevice,
    TempHeatingDevice,
    Terminal,
    DuctGroup,          // "Воздуховоды"
    Duct,
    DuctFitting,
    HeatCurtain,
    WindowInlet,
    Grille,
    FireThermalProtection,
    Coolant,
    Nozzle,
    CableSupport,
    Detector,
    PowderModule,
    PowderAutomation,
    // ЭОМ
    Switchgear,         // электрощитовое оборудование (ВРУ, ГРЩ)
    PowerPanel,         // щиты силовые распределительные
    Uerm,
    CableTray,          // кабеленесущие изделия — прямые участки
    CableTrayFitting,   // кабеленесущие изделия — фасонные элементы
    Cable,
    Grounding,
    FireproofBox,
    Busway,
    Lighting,
    WiringDevice,       // электроустановочные изделия
    CablePenetration
}
