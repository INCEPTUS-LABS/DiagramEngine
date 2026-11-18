using Inceptus.DiagramEngine.Canvas2D.Export;

namespace Inceptus.DiagramEngine.Blazor.Demo;

public partial class ReadOnlyBpmnDemoHost
{
    private bool _graphicalExportInProgress;
    private Task ExportSvgAsync() => ExportGraphicalAsync(GraphicalExportFormat.Svg);
    private Task ExportPngAsync() => ExportGraphicalAsync(GraphicalExportFormat.Png);
    private async Task ExportGraphicalAsync(GraphicalExportFormat format)
    {
        if (!CanOperate || _graphicalExportInProgress || Modeler is not { } modeler) return;
        _graphicalExportInProgress = true;
        Feedback = $"Preparing {format.ToString().ToUpperInvariant()} image…";
        try
        {
            var result = await DemoGraphicalExport.DownloadAsync(modeler, JavaScript, format,
                () => !_disposed && ReferenceEquals(modeler, Modeler), _lifetime.Token);
            if (_disposed || result.Status == GraphicalExportStatus.Cancelled) return;
            Feedback = result.Succeeded ? "Image export completed." : "Image export failed. " +
                string.Join("; ", result.Diagnostics.Select(item => item.Code + ": " + item.Message));
        }
        finally { _graphicalExportInProgress = false; }
    }
}
