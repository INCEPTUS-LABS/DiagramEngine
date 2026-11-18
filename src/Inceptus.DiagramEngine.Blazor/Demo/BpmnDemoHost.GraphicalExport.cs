using Inceptus.DiagramEngine.Canvas2D.Export;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class BpmnDemoHost
{
    private bool _graphicalExportInProgress;

    private async Task ExportGraphicalAsync(GraphicalExportFormat format)
    {
        CloseDocumentMenu();
        if (_disposed || !CanUseNativeDocumentTransfer || _host is not { } editor) return;
        _graphicalExportInProgress = true;
        DismissStandardFeedback();
        CompleteStandardFeedback($"Preparing {format.ToString().ToUpperInvariant()} image…", true, []);
        try
        {
            await InvokeAsync(StateHasChanged);
            var result = await DemoGraphicalExport.DownloadAsync(editor, JavaScript, format,
                () => !_disposed && ReferenceEquals(editor, _host), _componentLifetime.Token);
            if (_disposed || result.Status == GraphicalExportStatus.Cancelled) return;
            CompleteStandardFeedback(result.Succeeded ? "Image export completed" : "Image export failed", result.Succeeded, result.Diagnostics);
        }
        finally { _graphicalExportInProgress = false; }
    }
}
