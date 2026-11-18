using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private ApplicationActionRequest? _applicationToolbarRequest;
    private DocumentCanvasApplicationToolbarState? _applicationToolbar;
    private bool _applicationToolbarDismissed;
    private bool _applicationToolbarConsumed;
    private long _toolbarObservedAttachment = -1;
    private PresentationViewContext? _toolbarObservedContext;
    private ImmutableArray<(SemanticElementId Id, BpmnSemanticTargetKind Kind)> _toolbarObservedOwners = [];

    internal DocumentCanvasApplicationToolbarState? ApplicationToolbar
    {
        get
        {
            lock (_sync)
            {
                ReconcileApplicationToolbarUnderLock();
                return _applicationToolbar;
            }
        }
    }

    internal void DismissApplicationContextualActions()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _applicationToolbarDismissed = true;
            RetireToolbarApplicationActionsUnderLock();
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    internal async ValueTask<BpmnPresentationOperationResult> RequestApplicationContextualActionsAsync(
        BpmnPresentationContextStamp expectedContext, long expectedApplicationActionsRevision,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(expectedContext);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedApplicationActionsRevision);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        try
        {
            lock (_sync)
            {
                linked.Token.ThrowIfCancellationRequested();
                var presentation = CapturePresentation();
                if (!presentation.IsAvailable || _session is not { } session)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_PRESENTATION_UNAVAILABLE");
                if (_applicationRegistrationError is { } error)
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, error);
                if (!_applicationToolbarEnabled)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_TOOLBAR_DISABLED");
                if (presentation.Stamp != expectedContext || expectedApplicationActionsRevision != _applicationActionsRevision)
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_CONTEXT_STALE");
                if (!HasApplicationActions)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_ACTIONS_UNAVAILABLE");
                var state = session.CaptureState();
                ObserveApplicationToolbarSelectionUnderLock(state);
                if (state.EditorState.Selection.IsEmpty && state.EditorState.SemanticSceneSelection is null)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_TARGET_UNAVAILABLE");
                if (CaptureApplicationLogicalOwnersUnderLock(state).Length > 1)
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_MULTIPLE_TARGETS");
                if (!TryResolveApplicationTarget(presentation, out var target, out var part))
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_TARGET_AMBIGUOUS");
                if (_contextMenu is not null || _propertiesFormOpen || _modelViewPropertiesFormOpen ||
                    state.EditorState.ActiveGesture is not null)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable, "BPMN_APPLICATION_TOOLBAR_SUSPENDED");
                if (!TryResolveApplicationToolbarPlacementUnderLock(target!, part, [], out _, out var ambiguous))
                    return PresentationOutcome(ambiguous ? BpmnModelerOperationStatus.Rejected : BpmnModelerOperationStatus.Unavailable,
                        ambiguous ? "BPMN_APPLICATION_TARGET_AMBIGUOUS" : "BPMN_APPLICATION_TOOLBAR_SUSPENDED");
                _applicationToolbarDismissed = false;
                _applicationToolbarConsumed = false;
                RetireToolbarApplicationActionsUnderLock();
                ReconcileApplicationToolbarUnderLock();
                return PresentationOutcome(BpmnModelerOperationStatus.Succeeded);
            }
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    private void ObserveApplicationToolbarSelectionUnderLock(EditingSessionState state)
    {
        var owners = CaptureApplicationLogicalOwnersUnderLock(state);
        if (_toolbarObservedAttachment == _documentSessionVersion && _toolbarObservedContext == state.PresentationContext &&
            _toolbarObservedOwners.SequenceEqual(owners)) return;
        _toolbarObservedAttachment = _documentSessionVersion;
        _toolbarObservedContext = state.PresentationContext;
        _toolbarObservedOwners = owners;
        _applicationToolbarDismissed = false;
        _applicationToolbarConsumed = false;
        RetireToolbarApplicationActionsUnderLock();
    }

    private ImmutableArray<(SemanticElementId Id, BpmnSemanticTargetKind Kind)> CaptureApplicationLogicalOwnersUnderLock(EditingSessionState state)
    {
        if (_session is not { } session || !session.TryCaptureDocumentSnapshot(out var document)) return _toolbarObservedOwners;
        var owners = state.EditorState.Selection.Select(id => document.VisualModel.TryGetVisualState(id, out var visual)
            ? visual?.SemanticElementId : null).OfType<SemanticElementId>();
        if (state.EditorState.SemanticSceneSelection is { } semantic) owners = owners.Append(semantic);
        return owners.Distinct().OrderBy(static id => id.Value, StringComparer.Ordinal).Select(id =>
            (id, document.SemanticModel.TryGetRelationship(id, out _) ? BpmnSemanticTargetKind.Relationship : BpmnSemanticTargetKind.Element))
            .ToImmutableArray();
    }

    private void ReconcileApplicationToolbarUnderLock()
    {
        _applicationToolbar = null;
        if (_disposed || _session is not { } session) return;
        var state = session.CaptureState();
        ObserveApplicationToolbarSelectionUnderLock(state);
        if (!_applicationToolbarEnabled || !HasApplicationActions)
        {
            RetireToolbarApplicationActionsUnderLock();
            return;
        }
        if (_applicationToolbarDismissed || _applicationToolbarConsumed || _contextMenu is not null ||
            _propertiesFormOpen || _modelViewPropertiesFormOpen || state.EditorState.ActiveGesture is not null ||
            !state.IsCurrentScenePresented || state.CurrentScene is null) return;
        var presentation = CapturePresentation();
        if (!TryResolveApplicationTarget(presentation, out var target, out var part))
        {
            RetireToolbarApplicationActionsUnderLock();
            return;
        }
        if (_applicationToolbarRequest is { } previous && previous.Context.Stamp != presentation.Stamp)
            RetireToolbarApplicationActionsUnderLock();
        var request = _applicationToolbarRequest;
        if (!TryResolveApplicationToolbarPlacementUnderLock(target!, part, request?.Actions ?? [], out var placement, out _)) return;
        if (request is null)
        {
            var context = new BpmnApplicationActionContext(presentation, target!, part,
                ++_applicationActionRequestVersion, _applicationActionsRevision);
            request = new(context, _applicationActionProvider!, true, _applicationTypedHandlerAvailable, state.CurrentScene, _applicationActionObservation);
            _applicationToolbarRequest = request;
            _ = StartApplicationDiscoveryAsync(request);
        }
        if (!request.DiscoveryPending && request.Actions.IsEmpty || request.Started) return;
        _applicationToolbar = new(new(presentation, target!, part, request.Context.RequestVersion, _applicationActionsRevision),
            request.Actions, request.DiscoveryPending, placement!);
    }

    private void RetireToolbarApplicationActionsUnderLock()
    {
        var request = _applicationToolbarRequest;
        _applicationToolbarRequest = null;
        _applicationToolbar = null;
        request?.Observation?.Record(BpmnActionObservationStage.RequestRetired, detail: "Toolbar retired");
        if (request is not null) CancelApplicationRequest(request);
    }

    private static bool SameApplicationOwner(BpmnSemanticSelectionTarget left, BpmnSemanticSelectionTarget right) =>
        left.SemanticId == right.SemanticId && left.Kind == right.Kind && left.VisualStateId == right.VisualStateId;

    private bool TryResolveApplicationOwnerUnderLock(BpmnSemanticSelectionTarget expected, BpmnPresentationPart? expectedPart,
        out BpmnSemanticSelectionTarget? target, out BpmnPresentationPart? part)
    {
        target = null;
        part = null;
        if (_session is not { } session || session.CaptureState() is not { IsCurrentScenePresented: true, CurrentScene: { } scene } ||
            !session.TryCaptureDocumentSnapshot(out var document)) return false;
        var element = document.SemanticModel.TryGetElement(expected.SemanticId, out _);
        var relationship = document.SemanticModel.TryGetRelationship(expected.SemanticId, out _);
        if (element == relationship || expected.Kind != (element ? BpmnSemanticTargetKind.Element : BpmnSemanticTargetKind.Relationship)) return false;
        var parts = scene.Items.Where(item => item.Origin.SemanticElementId == expected.SemanticId &&
            item.Origin.VisualStateId == expected.VisualStateId).Select(item => BpmnPresentationSelection.ResolvePart(document, item))
            .OfType<BpmnPresentationPart>().ToImmutableArray();
        if (parts.IsEmpty) return false;
        target = new(expected.SemanticId, expected.Kind, expected.VisualStateId, parts);
        if (expectedPart is not null)
        {
            var matches = parts.Where(candidate => candidate.SceneObjectId == expectedPart.SceneObjectId &&
                candidate.Kind == expectedPart.Kind && candidate.SpatialRegionId == expectedPart.SpatialRegionId).ToArray();
            if (matches.Length != 1) return false;
            part = matches[0];
        }
        return part is not null || HasSingleApplicationPresentationInstance(target);
    }

    private ApplicationActionRequest? PrepareApplicationActionsForHitUnderLock(PointD cssPoint, SceneObjectId hitId)
    {
        if (!HasApplicationActions || _session is not { } session ||
            session.CaptureState() is not { IsCurrentScenePresented: true, CurrentScene: { } scene } ||
            !session.TryCaptureDocumentSnapshot(out var document)) return null;
        var hits = scene.Items.Where(item => item.Id == hitId).ToArray();
        if (hits.Length != 1 || hits[0].Origin.SemanticElementId is not { } ownerId ||
            BpmnPresentationSelection.ResolvePart(document, hits[0]) is not { } part) return null;
        var element = document.SemanticModel.TryGetElement(ownerId, out _);
        var relationship = document.SemanticModel.TryGetRelationship(ownerId, out _);
        if (element == relationship) return null;
        var expected = new BpmnSemanticSelectionTarget(ownerId,
            element ? BpmnSemanticTargetKind.Element : BpmnSemanticTargetKind.Relationship, hits[0].Origin.VisualStateId, []);
        if (!TryResolveApplicationOwnerUnderLock(expected, part, out var target, out var currentPart)) return null;
        return PrepareApplicationActionsUnderLock(cssPoint, CapturePresentation(), target, currentPart);
    }
}
