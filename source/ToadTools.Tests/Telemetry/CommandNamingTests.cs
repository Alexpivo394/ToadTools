using Shouldly;
using ToadTools.Telemetry;

namespace ToadTools.Tests.Telemetry;

public class CommandNamingTests
{
    [Test]
    public void Label_WithLineBreaks_OneLine()
    {
        var named = CommandNaming.From("ID_OBJECTS_WALL", "Стена:\nАрхитектурная\r\n", "Архитектура");

        named.ShouldBe(new NamedCommand("ID_OBJECTS_WALL", "Стена: Архитектурная", "Revit", "Архитектура"));
    }

    [Test]
    public void ToadToolsTab_NotRecorded() => CommandNaming.From("CustomCtrl_%CustomCtrl_%ToadTools%BIM%ParamChecker", "Param\nChecker", "ToadTools").ShouldBeNull();

    [Test]
    [Arguments("")]
    [Arguments("   ")]
    [Arguments(null)]
    public void EmptyId_NotRecorded(string? id) => CommandNaming.From(id, "Стена", "Архитектура").ShouldBeNull();

    [Test]
    [Arguments("Architecture")]
    [Arguments("Управление")]
    [Arguments("Изменить")]
    public void KnownRevitTab_Revit(string tab) => CommandNaming.From("SOME_COMMAND", "Команда", tab)!.Source.ShouldBe("Revit");

    [Test]
    public void RevitCommandId_Revit_EvenOnUnknownTab() => CommandNaming.From("ID_VIEW_3D", "3D", null)!.Source.ShouldBe("Revit");

    [Test]
    public void ExternalButton_Addin_WithTab()
    {
        var named = CommandNaming.From("CustomCtrl_%CustomCtrl_%pyRevit%Tools%Sync", "Sync", "pyRevit")!;

        named.Source.ShouldBe("Addin");
        named.Tab.ShouldBe("pyRevit");
    }

    [Test]
    public void EmptyLabel_UsesId() => CommandNaming.From("ID_OBJECTS_WALL", "\n", "Архитектура")!.Name.ShouldBe("ID_OBJECTS_WALL");

    [Test]
    public void ToadToolsCommand_NameWithoutSuffix() => CommandNaming.ToadToolsId("ParamCheckerCommand").ShouldBe("ParamChecker");
}
