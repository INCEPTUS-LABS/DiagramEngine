using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Routing;

namespace Inceptus.DiagramEngine.Canvas2D.Publishing;

/// <summary>
/// One immutable, read-only Document and final presentation tuple captured from a current
/// Editing Session generation. The Scene is detached and is never installed into the editor.
/// </summary>
public sealed record EditingSessionPresentationCapture
{
    public EditingSessionPresentationCapture(DocumentSnapshot Document, DocumentScopeId ActiveScopeId,
        EditingSessionGeneration Generation, ProjectedGraph ProjectedGraph, LayoutResult LayoutResult,
        RoutingResult RoutingResult, Canvas2DScene Scene)
        : this(Document, new ProcessScopePresentationContext(ActiveScopeId), Generation,
            ProjectedGraph, LayoutResult, RoutingResult, Scene)
    { }

    public EditingSessionPresentationCapture(DocumentSnapshot Document, PresentationViewContext PresentationContext,
        EditingSessionGeneration Generation, ProjectedGraph ProjectedGraph, LayoutResult LayoutResult,
        RoutingResult RoutingResult, Canvas2DScene Scene)
    {
        this.Document = Document;
        this.PresentationContext = PresentationContext;
        this.Generation = Generation;
        this.ProjectedGraph = ProjectedGraph;
        this.LayoutResult = LayoutResult;
        this.RoutingResult = RoutingResult;
        this.Scene = Scene;
    }

    public DocumentSnapshot Document { get; init; }
    public PresentationViewContext PresentationContext { get; init; }
    public DocumentScopeId ActiveScopeId
    {
        get => PresentationContext is ProcessScopePresentationContext process ? process.ScopeId
            : throw new InvalidOperationException("A document presentation has no active process scope.");
        init => PresentationContext = new ProcessScopePresentationContext(value);
    }
    public EditingSessionGeneration Generation { get; init; }
    public ProjectedGraph ProjectedGraph { get; init; }
    public LayoutResult LayoutResult { get; init; }
    public RoutingResult RoutingResult { get; init; }
    public Canvas2DScene Scene { get; init; }

    public void Deconstruct(out DocumentSnapshot Document, out DocumentScopeId ActiveScopeId,
        out EditingSessionGeneration Generation, out ProjectedGraph ProjectedGraph, out LayoutResult LayoutResult,
        out RoutingResult RoutingResult, out Canvas2DScene Scene)
    {
        Document = this.Document;
        ActiveScopeId = this.ActiveScopeId;
        Generation = this.Generation;
        ProjectedGraph = this.ProjectedGraph;
        LayoutResult = this.LayoutResult;
        RoutingResult = this.RoutingResult;
        Scene = this.Scene;
    }
}

public enum EditingSessionPresentationCaptureStatus
{
    Succeeded,
    Unavailable,
    Failed,
    Cancelled,
}

public sealed record EditingSessionPresentationCaptureResult
{
    private EditingSessionPresentationCaptureResult(
        EditingSessionPresentationCaptureStatus status,
        EditingSessionPresentationCapture? capture,
        IEnumerable<Diagnostic>? diagnostics)
    {
        Status = status;
        Capture = capture;
        Diagnostics = diagnostics?.ToImmutableArray() ?? [];
    }

    public EditingSessionPresentationCaptureStatus Status { get; }

    public EditingSessionPresentationCapture? Capture { get; }

    public ImmutableArray<Diagnostic> Diagnostics { get; }

    public bool Succeeded =>
        Status == EditingSessionPresentationCaptureStatus.Succeeded && Capture is not null;

    internal static EditingSessionPresentationCaptureResult Success(
        EditingSessionPresentationCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        return new(EditingSessionPresentationCaptureStatus.Succeeded, capture, null);
    }

    internal static EditingSessionPresentationCaptureResult Failure(
        EditingSessionPresentationCaptureStatus status,
        IEnumerable<Diagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        if (status == EditingSessionPresentationCaptureStatus.Succeeded)
        {
            throw new ArgumentOutOfRangeException(nameof(status));
        }

        return new(status, null, diagnostics);
    }
}
