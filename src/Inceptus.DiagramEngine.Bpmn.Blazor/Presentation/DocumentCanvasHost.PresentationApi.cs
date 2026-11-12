using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.HitTesting;
using Inceptus.DiagramEngine.Canvas2D.Interaction;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost : IBpmnDiagramPresentation
{
    private readonly Guid _presentationInstanceId = Guid.NewGuid();
    private long _observedPresentationAttachment = -1;
    private PresentationViewContext? _observedPresentationContext;
    private ImmutableArray<VisualStateId> _observedSelection = [];
    private SemanticElementId? _observedSemanticSelection;
    private SceneObjectId? _primaryPresentationPart;
    private long _presentationContextVersion;
    private long _presentationSelectionVersion;
    private ImmutableArray<VisualStateId> _notifiedSelection = [];
    private SemanticElementId? _notifiedSemanticSelection;
    private SceneObjectId? _notifiedPrimaryPart;

    internal void CancelPendingOperations()
    {
        lock (_sync)
        {
            if (!_disposed)
            {
                if (_contextMenu?.ApplicationContext is not null) CloseContextMenuUnderLock();
                else RetireApplicationActionsUnderLock();
                _lifetime.Cancel();
            }
        }
    }

    public BpmnPresentationSnapshot CapturePresentation()
    {
        lock (_sync)
        {
            if (_disposed || !_initialized || _session is not { } session)
                return BpmnPresentationSnapshot.Unavailable;
            var state = session.CaptureState();
            ObservePresentationUnderLock(state);
            if (!IsPresentationContextAvailable(state) || !state.IsCurrentScenePresented ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                document.DocumentId != state.DocumentId || document.Revision != state.DocumentRevision)
                return BpmnPresentationSnapshot.Unavailable;
            var selection = BpmnPresentationSelection.Resolve(document, state, out var diagnostics);
            var primary = selection.SelectMany(static target => target.Parts)
                .SingleOrDefault(part => part.SceneObjectId == _primaryPresentationPart);
            var scopes = AvailablePresentationScopes(document);
            var path = state.PresentationContext is ProcessScopePresentationContext process
                ? document.SemanticModel.GetAncestors(process.ScopeId).Reverse()
                    .Append(document.SemanticModel.EnumerateScopeForest().Single(scope => scope.Id == process.ScopeId)).ToImmutableArray()
                : [];
            return new(new(_presentationInstanceId, _documentSessionVersion, document.DocumentId,
                    document.Revision, state.PresentationContext, _presentationContextVersion, _presentationSelectionVersion),
                selection, primary, path, scopes, diagnostics);
        }
    }

    private ImmutableArray<DocumentScopeSnapshot> AvailablePresentationScopes(DocumentSnapshot document) =>
        document.SemanticModel.EnumerateScopeForest().Where(scope =>
            document.SemanticModel.IsTopLevelScope(scope.Id) ||
            scope.OwnerSemanticElementId is { } ownerId &&
            document.SemanticModel.TryGetElement(ownerId, out var owner) && owner is not null &&
            _scopeNavigationCatalog.TryGetRegistration(owner.TypeId, out var registration) &&
            registration.Contribution.TryResolveTargetScope(document, owner, out var target) && target == scope.Id)
            .ToImmutableArray();

    // Called on canonical session notifications as well as readback. Observing intermediate
    // transitions prevents an away-and-back selection or navigation from reviving old requests.
    private void ObservePresentationUnderLock(EditingSessionState state)
    {
        if (_observedPresentationAttachment != _documentSessionVersion ||
            _observedPresentationContext != state.PresentationContext)
        {
            _observedPresentationAttachment = _documentSessionVersion;
            _observedPresentationContext = state.PresentationContext;
            _presentationContextVersion++;
            _primaryPresentationPart = null;
        }
        if (!_observedSelection.SequenceEqual(state.EditorState.Selection) ||
            _observedSemanticSelection != state.EditorState.SemanticSceneSelection)
        {
            _observedSelection = state.EditorState.Selection;
            _observedSemanticSelection = state.EditorState.SemanticSceneSelection;
            _primaryPresentationPart = null;
            _presentationSelectionVersion++;
        }
    }

    private void RecordPrimaryPresentationPartUnderLock(Canvas2DInteractionResult result)
    {
        if (!result.Succeeded || _session is not { } session) return;
        var state = session.CaptureState();
        ObservePresentationUnderLock(state);
        var part = result.TargetId;
        if (result.TargetOrigin is not { } origin ||
            !(origin.VisualStateId is { } visual && state.EditorState.Selection.Contains(visual) ||
              origin.VisualStateId is null && origin.SemanticElementId is { } semantic &&
              state.EditorState.SemanticSceneSelection == semantic)) part = null;
        if (_primaryPresentationPart == part) return;
        _primaryPresentationPart = part;
        _presentationSelectionVersion++;
    }

    private void CollectSelectionNotificationUnderLock()
    {
        var presentation = CapturePresentation();
        // A temporarily rebuilding presentation is not a logical selection clearing.
        if (!presentation.IsAvailable && _session?.CaptureState().Status == EditingSessionStatus.Rebuilding) return;
        var selection = presentation.IsAvailable ? _observedSelection : [];
        var semantic = presentation.IsAvailable ? _observedSemanticSelection : null;
        var primary = presentation.PrimaryPart?.SceneObjectId;
        if (_notifiedSelection.SequenceEqual(selection) && _notifiedSemanticSelection == semantic &&
            _notifiedPrimaryPart == primary) return;
        _notifiedSelection = selection;
        _notifiedSemanticSelection = semantic;
        _notifiedPrimaryPart = primary;
        _ = ModelerNotifications?.Enqueue(new BpmnSelectionChangedEventArgs(presentation));
    }

    public ValueTask<BpmnPresentationOperationResult> SelectVisualAsync(BpmnPresentationContextStamp context,
        VisualStateId visualStateId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visualStateId);
        return ExecutePresentationOperationAsync(context, async (session, token) =>
        {
            var state = session.CaptureState();
            if (_interactionController is not { } controller ||
                !session.TryCaptureDocumentSnapshot(out var document) ||
                !document.VisualModel.TryGetVisualState(visualStateId, out var visual) || visual is null)
                return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_PRESENTATION_TARGET_UNAVAILABLE");
            var items = state.CurrentScene!.Items.Where(item => item.Origin.VisualStateId == visualStateId &&
                BpmnPresentationSelection.ResolvePart(document, item) is not null).ToArray();
            if (!BpmnPresentationSelection.HasSinglePresentationInstance(items) ||
                !TryResolveValidationSelectionCssPoint(state, visualStateId, out var cssPoint) ||
                !state.CurrentScene.ViewportTransform.TryInvert(out var inverse))
                return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_PRESENTATION_TARGET_UNAVAILABLE");
            var hit = new Canvas2DSceneHitTestService().HitTest(state.CurrentScene, inverse.TransformPoint(cssPoint));
            if (hit?.Origin.VisualStateId != visualStateId || hit.Origin.SemanticElementId != visual.SemanticElementId ||
                !items.Any(item => item.Id == hit.SceneObjectId))
                return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_PRESENTATION_TARGET_UNAVAILABLE");
            var result = await controller.PointerActivatedAsync(cssPoint, token).ConfigureAwait(false);
            lock (_sync) RecordPrimaryPresentationPartUnderLock(result);
            return new(result.Succeeded ? BpmnModelerOperationStatus.Succeeded :
                result.Status == Canvas2DInteractionStatus.Cancelled ? BpmnModelerOperationStatus.Cancelled :
                BpmnModelerOperationStatus.Rejected, CapturePresentation(), result.Diagnostics);
        }, cancellationToken);
    }

    public ValueTask<BpmnPresentationOperationResult> NavigateToScopeAsync(BpmnPresentationContextStamp context,
        DocumentScopeId scopeId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(scopeId);
        return ExecutePresentationOperationAsync(context, async (session, token) =>
        {
            if (!CapturePresentation().AvailableScopes.Any(scope => scope.Id == scopeId))
                return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_PRESENTATION_SCOPE_UNAVAILABLE");
            var result = await NavigateToScopeUnderGateAsync(session, scopeId, static _ => { }, token).ConfigureAwait(false);
            return new(result?.Succeeded == true ? BpmnModelerOperationStatus.Succeeded :
                result?.Status == EditingSessionOperationStatus.Cancelled ? BpmnModelerOperationStatus.Cancelled :
                BpmnModelerOperationStatus.Rejected, CapturePresentation(), result?.Diagnostics ?? []);
        }, cancellationToken);
    }

    private async ValueTask<BpmnPresentationOperationResult> ExecutePresentationOperationAsync(
        BpmnPresentationContextStamp context,
        Func<EditingSession, CancellationToken, ValueTask<BpmnPresentationOperationResult>> execute,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        try
        {
            var current = CapturePresentation();
            if (!current.IsAvailable || _session is not { } session)
                return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_PRESENTATION_UNAVAILABLE");
            if (current.Stamp != context)
                return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_PRESENTATION_STALE");
            return await execute(session, linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
#pragma warning disable CA1031 // Bounded public operation diagnostics, consistent with the document facade.
        catch (Exception exception) when (IsNonFatal(exception))
        { return PresentationOutcome(BpmnModelerOperationStatus.Failed, "BPMN_PRESENTATION_FAILED"); }
#pragma warning restore CA1031
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    private BpmnPresentationOperationResult PresentationOutcome(BpmnModelerOperationStatus status, string? code = null) =>
        new(status, CapturePresentation(), code is null ? [] : [Error(code, "The presentation request is not applicable to the current attachment and context.")]);
}
