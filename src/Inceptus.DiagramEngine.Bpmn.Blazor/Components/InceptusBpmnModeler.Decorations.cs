using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler
{
    /// <summary>Captures the current decoration attachment, or null before readiness or after disposal.</summary>
    public BpmnDecorationAttachment? CaptureDecorationAttachment() => Facade.CaptureDecorationAttachment();

    /// <summary>
    /// Replaces passive, transient decoration input without editing the Document or changing its
    /// viewport. Each accepted revision must increase within this attachment. Concurrent update
    /// requests may return Unavailable before acceptance; retry with a newer revision.
    /// </summary>
    public ValueTask<Canvas2DDecorationUpdateResult> ReplaceDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision, Canvas2DDecorationInput input,
        CancellationToken cancellationToken = default) =>
        Facade.ReplaceDecorationsAsync(attachment, inputRevision, input, cancellationToken);

    /// <summary>
    /// Submits a versioned empty input. Acceptance and renderer acknowledgment are reported
    /// separately; cancellation after acceptance is not a rollback.
    /// </summary>
    public ValueTask<Canvas2DDecorationUpdateResult> ClearDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision,
        CancellationToken cancellationToken = default) =>
        Facade.ClearDecorationsAsync(attachment, inputRevision, cancellationToken);
}
