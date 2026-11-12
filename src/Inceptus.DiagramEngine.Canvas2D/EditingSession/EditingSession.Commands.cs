using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private async ValueTask<HistoryOperationResult> ExecuteCoreAsync(
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            var result = await ExecuteCommandUnderGateAsync(
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteForSelectionCoreAsync(
        VisualStateId expectedSelectedVisualStateId,
        bool requireSoleSelection,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            var selectionMatches = requireSoleSelection
                ? state.EditorState.Selection.Length == 1 &&
                    state.EditorState.Selection[0] == expectedSelectedVisualStateId
                : state.EditorState.Selection.Contains(expectedSelectedVisualStateId);
            if (!selectionMatches)
            {
                return HistoryOperationResult.CreateNotCommitted(
                    state.DocumentId,
                    command.TypeId,
                    HistoryOperationStatus.CommandFailed,
                    state.DocumentRevision,
                    state.HistoryStatus,
                    [Error(
                        EditingSessionDiagnosticCodes.SelectionChanged,
                        requireSoleSelection
                            ? "The Command was not executed because the expected Visual State is no longer the sole selection."
                            : "The Command was not executed because the expected Visual State is no longer selected.",
                        expectedSelectedVisualStateId.Value)]);
            }

            var result = await ExecuteCommandUnderGateAsync(
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteRestorationCoreAsync(
        bool isUndo,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                commandTypeId: null,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsHistoryRestorationUnavailable(out var state))
            {
                return UnavailableHistoryResult(command: null, state);
            }

            if (_accessMode == EditingSessionAccessMode.ReadOnly)
            {
                return ReadOnlyHistoryResult(command: null, state, cancellationToken);
            }

            var result = isUndo
                ? await History.UndoAsync(
                    _commandProcessor,
                    ReplayScopeNavigationUnderCommandGateAsync,
                    cancellationToken)
                    .ConfigureAwait(false)
                : await History.RedoAsync(
                    _commandProcessor,
                    ReplayScopeNavigationUnderCommandGateAsync,
                    cancellationToken)
                    .ConfigureAwait(false);
            RecordExpectedEvent(result);
            if (result.IsApplied)
            {
                NotifyStateChanged();
            }
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    private async ValueTask<HistoryOperationResult> ExecuteForSemanticSceneSelectionCoreAsync(
        SemanticElementId expectedSelectedSemanticElementId,
        ICommand command,
        CancellationToken cancellationToken)
    {
        if (!await TryEnterCommandAsync(cancellationToken).ConfigureAwait(false))
        {
            var cancelledState = CaptureState();
            return HistoryOperationResult.CreateNotCommitted(
                cancelledState.DocumentId,
                command.TypeId,
                HistoryOperationStatus.Cancelled,
                cancelledState.DocumentRevision,
                cancelledState.HistoryStatus,
                [CancelledDiagnostic(cancelledState.DocumentId.Value)]);
        }

        try
        {
            if (IsUnavailable(out var state))
            {
                return UnavailableHistoryResult(command, state);
            }

            if (!state.EditorState.Selection.IsEmpty ||
                state.EditorState.SemanticSceneSelection != expectedSelectedSemanticElementId)
            {
                return HistoryOperationResult.CreateNotCommitted(
                    state.DocumentId,
                    command.TypeId,
                    HistoryOperationStatus.CommandFailed,
                    state.DocumentRevision,
                    state.HistoryStatus,
                    [Error(
                        EditingSessionDiagnosticCodes.SelectionChanged,
                        "The Command was not executed because the expected semantic Scene target is no longer selected.",
                        expectedSelectedSemanticElementId.Value)]);
            }

            var result = await ExecuteCommandUnderGateAsync(
                command,
                cancellationToken).ConfigureAwait(false);
            RecordExpectedEvent(result);
            return result;
        }
        finally
        {
            _commandGate.Release();
        }
    }

    // Every persistent session submission reaches this boundary while holding _commandGate.
    // The mode is fixed at attachment, so no command type or restoration policy can bypass it.
    private ValueTask<HistoryOperationResult> ExecuteCommandUnderGateAsync(
        ICommand command,
        CancellationToken cancellationToken) =>
        _accessMode == EditingSessionAccessMode.ReadOnly
            ? ValueTask.FromResult(ReadOnlyHistoryResult(command, CaptureState(), cancellationToken))
            : History.ExecuteAsync(_commandProcessor, command, cancellationToken);

    private static HistoryOperationResult ReadOnlyHistoryResult(
        ICommand? command,
        EditingSessionState state,
        CancellationToken cancellationToken)
    {
        var cancelled = cancellationToken.IsCancellationRequested;
        return HistoryOperationResult.CreateNotCommitted(
            state.DocumentId,
            command?.TypeId,
            cancelled
                ? HistoryOperationStatus.Cancelled
                : HistoryOperationStatus.CommandFailed,
            state.DocumentRevision,
            state.HistoryStatus,
            cancelled
                ? [CancelledDiagnostic(state.DocumentId.Value)]
                : [Error(
                    EditingSessionDiagnosticCodes.ReadOnly,
                    "The read-only Editing Session cannot execute persistent Commands or restore History.",
                    state.DocumentId.Value)]);
    }

    private async ValueTask<bool> TryEnterCommandAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _commandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private void RecordExpectedEvent(HistoryOperationResult result)
    {
        if (result.CommittedRevision is not { } revision)
        {
            return;
        }

        lock (_sync)
        {
            if (revision > _expectedEventRevision)
            {
                _expectedEventRevision = revision;
            }

            PulseStateUnderLock();
        }
    }
}
