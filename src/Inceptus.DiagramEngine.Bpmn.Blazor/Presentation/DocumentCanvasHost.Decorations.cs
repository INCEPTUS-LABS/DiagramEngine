using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Canvas2D;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    // One admitted decoration operation can wait for the canonical host gate. Additional
    // callers receive an explicit pre-acceptance outcome instead of creating an unbounded queue.
    private int _decorationOperationPending;

    internal BpmnDecorationAttachment? CaptureDecorationAttachment()
    {
        lock (_sync)
        {
            if (_disposed || !_initialized || _session is not { } session)
                return null;
            var state = session.CaptureState();
            if (state.Status != EditingSessionStatus.Ready || state.CurrentScene is null)
                return null;
            return new(_presentationInstanceId, _documentSessionVersion, state.DocumentId,
                session.DecorationAttachmentId);
        }
    }

    internal ValueTask<Canvas2DDecorationUpdateResult> ReplaceDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision, Canvas2DDecorationInput input,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        return UpdateDecorationsAsync(attachment, inputRevision, input, cancellationToken);
    }

    internal ValueTask<Canvas2DDecorationUpdateResult> ClearDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision,
        CancellationToken cancellationToken = default) =>
        UpdateDecorationsAsync(attachment, inputRevision, null, cancellationToken);

    private async ValueTask<Canvas2DDecorationUpdateResult> UpdateDecorationsAsync(
        BpmnDecorationAttachment attachment, long inputRevision, Canvas2DDecorationInput? input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(attachment);
        if (cancellationToken.IsCancellationRequested)
            return DecorationRequestOutcome(Canvas2DDecorationUpdateStatus.Cancelled, inputRevision);
        if (Interlocked.CompareExchange(ref _decorationOperationPending, 1, 0) != 0)
            return DecorationRequestOutcome(Canvas2DDecorationUpdateStatus.Unavailable, inputRevision,
                "BPMN_DECORATION_BUSY");
        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
            try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (linked.IsCancellationRequested)
            { return DecorationRequestOutcome(Canvas2DDecorationUpdateStatus.Cancelled, inputRevision); }
            try
            {
                EditingSession session;
                lock (_sync)
                {
                    if (_disposed || !_initialized || _session is not { } current)
                        return DecorationRequestOutcome(Canvas2DDecorationUpdateStatus.Unavailable, inputRevision,
                            "BPMN_DECORATION_UNAVAILABLE");
                    if (attachment.InstanceId != _presentationInstanceId ||
                        attachment.AttachmentVersion != _documentSessionVersion ||
                        attachment.SessionAttachmentId != current.DecorationAttachmentId ||
                        attachment.DocumentId != current.CaptureState().DocumentId)
                        return DecorationRequestOutcome(Canvas2DDecorationUpdateStatus.Rejected, inputRevision,
                            "BPMN_DECORATION_ATTACHMENT_STALE");
                    session = current;
                }

                // The session owns acceptance, native currency and renderer acknowledgment.
                // Never reinterpret a post-acceptance result as cancellation or rollback.
                var result = await session.ReplaceDecorationsAsync(inputRevision, input, linked.Token, _lifetimeToken).ConfigureAwait(false);
                _lifecycleObservation?.Evidence.Record("SessionDecorationReturned", $"input={inputRevision}; accepted={result.Accepted}; status={result.Status}");
                return result;
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
        }
        finally { Volatile.Write(ref _decorationOperationPending, 0); }
    }

    private static Canvas2DDecorationUpdateResult DecorationRequestOutcome(
        Canvas2DDecorationUpdateStatus status, long revision, string? code = null) =>
        new(status, false, revision, null, code is null ? [] :
            [Error(code, "The decoration request is not applicable to the current modeler attachment.")]);
}
