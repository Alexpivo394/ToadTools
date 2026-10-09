using Microsoft.Extensions.DependencyInjection;

namespace ToadTools;

/// <summary>
///     Registers all application services, view models and views in the DI container.
/// </summary>
/// <remarks>
///     Fully-qualified type names are used on purpose: several tools ship services that share
///     a short name (GetParameterService, RevitParameterService, Logger, ...), so importing
///     their namespaces here would create ambiguous references.
/// </remarks>
internal static class ServiceRegistration
{
    public static IServiceCollection RegisterServices(this IServiceCollection services)
    {
        // Command-only tools (Length, DoorSide, CableTrays, ReinforcementByColor, WarmSync,
        // ChangeSharedFamilies) read the active document via the ExternalCommand base or
        // RevitContext and need no registration. SSPlan, CreateCover and ParamChecker build
        // their view graph inside the command (bespoke runtime construction).

        services.AddParamToFinish();
        services.AddQuantityCheck();
        services.AddWriteDash();
        services.AddCreateSpaces();
        services.AddWorksetCheck();
        services.AddModelTransplanter();
        services.AddWorkingSet();
        services.AddClassifierCode();

        return services;
    }

    private static void AddParamToFinish(this IServiceCollection services)
    {
        services.AddScoped<ParamToFinish.Services.GetParameterService>();
        services.AddScoped<ParamToFinish.Services.IFinishParameterTransferService, ParamToFinish.Services.FinishParameterTransferService>();
        services.AddScoped<ParamToFinish.Services.ParamToFinishSettingsService>();
        services.AddScoped<ParamToFinish.ViewModels.ParamToFinishViewModel>();
        services.AddScoped<ParamToFinish.Views.ParamToFinishView>();
    }

    private static void AddQuantityCheck(this IServiceCollection services)
    {
        services.AddScoped<QuantityCheck.Services.Logger>();
        services.AddScoped<QuantityCheck.ViewModels.QuantityCheckViewModel>();
        services.AddScoped<QuantityCheck.Views.QuantityCheckView>();
    }

    private static void AddWriteDash(this IServiceCollection services)
    {
        services.AddScoped<WriteDash.Services.RevitParameterService>();
        services.AddScoped<WriteDash.Services.ParameterProcessorService>();
        services.AddScoped<WriteDash.ViewModels.WriteDashViewModel>();
        services.AddScoped<WriteDash.Views.WriteDashView>();
    }

    private static void AddCreateSpaces(this IServiceCollection services)
    {
        services.AddScoped<CreateSpaces.Services.RevitLinkProvider>();
        services.AddScoped<CreateSpaces.Services.GetParameterService>();
        services.AddScoped<CreateSpaces.Services.RevitRoomProvider>();
        services.AddScoped<CreateSpaces.Services.LoadParametersService>();
        services.AddScoped<CreateSpaces.Services.ISpaceCreationService, CreateSpaces.Services.SpaceCreationService>();
        services.AddScoped<CreateSpaces.ViewModels.CreateSpacesViewModel>();
        services.AddScoped<CreateSpaces.Views.CreateSpacesView>();
    }

    private static void AddWorksetCheck(this IServiceCollection services)
    {
        services.AddScoped<WorksetCheck.Models.WorksetCheckModel>();
        services.AddScoped<WorksetCheck.ViewModels.WorksetCheckViewModel>();
        services.AddScoped<WorksetCheck.Views.WorksetCheckView>();
    }

    private static void AddModelTransplanter(this IServiceCollection services)
    {
        services.AddScoped<ModelTransplanter.ViewModels.ModelTransplanterViewModel>();
        services.AddScoped<ModelTransplanter.Views.ModelTransplanterView>();
    }

    private static void AddWorkingSet(this IServiceCollection services)
    {
        services.AddScoped<WorkingSet.ViewModels.WorkingSetViewModel>();
        services.AddScoped<WorkingSet.Views.WorkingSetView>();
    }

    private static void AddClassifierCode(this IServiceCollection services)
    {
        services.AddScoped<ClassifierCode.Services.ElementCollector>();
        services.AddScoped<ClassifierCode.Services.ClassifierCodeService>();
        services.AddScoped<ClassifierCode.ViewModels.ClassifierCodeViewModel>();
        services.AddScoped<ClassifierCode.Views.ClassifierCodeView>();
    }
}
