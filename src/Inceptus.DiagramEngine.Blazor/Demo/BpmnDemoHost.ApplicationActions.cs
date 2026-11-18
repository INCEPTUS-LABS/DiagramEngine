namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    [Microsoft.AspNetCore.Components.Inject] private HttpClient CatalogClient { get; set; } = null!;
    private bool _maintenanceEnabled;
    private async Task SetMaintenanceAsync(bool enabled)
    {
        if (!enabled)
        {
            if (SettingsModeler is { } modeler && modeler.CaptureDecorationAttachment() is { } attachment)
            {
                var clear = await modeler.ClearDecorationsAsync(attachment, checked((modeler.CaptureEditorContext()?.Presentation.DecorationInputRevision ?? 0) + 1));
                if (!clear.Accepted)
                {
                    CompleteStandardFeedback("Maintenance annotations could not be cleared; retry when the presentation is ready.", false, clear.Diagnostics);
                    return;
                }
            }
            _actionExample?.SetVerificationEnabled(false);
            if (_actionExample is { } example) { example.ToolbarEnabled = false; example.CloseDialog(); }
        }
        _maintenanceEnabled = enabled;
    }
    private DemoApplicationActionExample? _actionExample;
    private DemoApplicationActionExample ActionExample => _actionExample ??= new(() => SettingsModeler, CatalogClient, () => _verificationGeneration);
}
