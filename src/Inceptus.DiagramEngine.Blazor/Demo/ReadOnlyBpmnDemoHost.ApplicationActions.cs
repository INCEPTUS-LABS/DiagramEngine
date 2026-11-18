namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class ReadOnlyBpmnDemoHost
{
    [Microsoft.AspNetCore.Components.Inject] private HttpClient CatalogClient { get; set; } = null!;
    private void SetMaintenance(bool enabled)
    {
        _maintenanceEnabled = enabled;
        if (!enabled) _actionExample?.SetVerificationEnabled(false);
    }
    private DemoApplicationActionExample? _actionExample;
    private DemoApplicationActionExample ActionExample => _actionExample ??= new(() => Modeler, CatalogClient, () => _verificationGeneration);
}
