using Inceptus.DiagramEngine.Contracts.ConnectionCreation;
using Inceptus.DiagramEngine.Contracts.EditorState;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    private bool RejectsEditingState(EditorStateSnapshot state) =>
        _accessMode == EditingSessionAccessMode.ReadOnly &&
        (state.ActiveGesture is not null || state.ActiveToolId is not null ||
         ConnectionToolState.GetSelection(state) is not null ||
         state.TemporaryFeedback.Any(static feedback => feedback.PlacementPreview is not null ||
             feedback.SpatialResize is not null || feedback.ElementConnection is not null));

    private EditingSessionOperationResult ReadOnlyInteractionResult() =>
        OperationResult(EditingSessionOperationStatus.Rejected,
            [Error(EditingSessionDiagnosticCodes.ReadOnly,
                "The read-only Editing Session cannot acquire editing tools, gestures or previews.",
                _documentId.Value)]);
}
