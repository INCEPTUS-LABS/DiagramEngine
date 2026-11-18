using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Canvas2D.Export;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Microsoft.JSInterop;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Shared image capture/download; native transfer and save checkpoints are deliberately absent.</summary>
internal static class DemoGraphicalExport
{
    internal static async Task<GraphicalExportResult> DownloadAsync(IBpmnDocumentEditor editor, IJSRuntime javaScript,
        GraphicalExportFormat format, Func<bool> isCurrentHost, CancellationToken cancellationToken)
    {
        var attachment = editor.CaptureEditorContext()?.AttachmentVersion;
        try
        {
            var capture = await editor.CapturePresentationAsync(cancellationToken);
            if (capture?.Succeeded != true)
                return new(GraphicalExportStatus.Failed, null, capture?.Diagnostics ??
                    [new("GRAPHICAL_EXPORT_UNAVAILABLE", DiagnosticSeverity.Error, "The active diagram is not ready for capture.")]);
            var exporter = new Canvas2DGraphicalExporter(javaScript, DemoBpmnImportPreparation.RendererConfiguration);
            var result = await exporter.ExportAsync(capture.Capture!, format, cancellationToken: cancellationToken);
            if (!result.Succeeded) return result;
            // Detached graphics survive edits, but never deliver them to a retired host/attachment.
            if (cancellationToken.IsCancellationRequested || !isCurrentHost() || editor.CaptureEditorContext() is not { } current ||
                current.AttachmentVersion != attachment || current.Document.DocumentId != capture.Capture!.Document.DocumentId)
                return new(GraphicalExportStatus.Cancelled, null, []);
            await BrowserFileDownload.DownloadAsync(javaScript, result.Artifact!.Content.AsMemory(),
                result.Artifact.ContentType, result.Artifact.FileName, cancellationToken);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        { return new(GraphicalExportStatus.Cancelled, null, []); }
#pragma warning disable CA1031 // Keep browser/capture failures in bounded Demo feedback, preserving native state.
        catch (Exception exception) when (exception is not OutOfMemoryException and not StackOverflowException and not AccessViolationException)
        {
            return new(GraphicalExportStatus.Failed, null,
                [new("GRAPHICAL_EXPORT_FAILED", DiagnosticSeverity.Error, "The image could not be generated or downloaded.")]);
        }
#pragma warning restore CA1031
    }
}
