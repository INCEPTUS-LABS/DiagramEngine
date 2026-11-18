using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class BpmnModelerFacade
{
    public BpmnDecorationAttachment? CaptureDecorationAttachment() => CaptureHost()?.CaptureDecorationAttachment();

    public ValueTask<Canvas2DDecorationUpdateResult> ReplaceDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision, Canvas2DDecorationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        ArgumentNullException.ThrowIfNull(input);
        return CaptureHost()?.ReplaceDecorationsAsync(attachment, inputRevision, input, cancellationToken)
            ?? DecorationsUnavailable(inputRevision, cancellationToken);
    }

    public ValueTask<Canvas2DDecorationUpdateResult> ClearDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        return CaptureHost()?.ClearDecorationsAsync(attachment, inputRevision, cancellationToken)
            ?? DecorationsUnavailable(inputRevision, cancellationToken);
    }

    private static ValueTask<Canvas2DDecorationUpdateResult> DecorationsUnavailable(long revision, CancellationToken token) =>
        ValueTask.FromResult(new Canvas2DDecorationUpdateResult(token.IsCancellationRequested
            ? Canvas2DDecorationUpdateStatus.Cancelled : Canvas2DDecorationUpdateStatus.Unavailable,
            false, revision, null, token.IsCancellationRequested ? [] : UnavailableDiagnostics()));
}
