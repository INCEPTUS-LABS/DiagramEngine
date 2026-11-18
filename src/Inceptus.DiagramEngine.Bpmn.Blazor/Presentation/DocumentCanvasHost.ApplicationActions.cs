using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private BpmnApplicationActionProvider? _applicationActionProvider;
    private bool _applicationActionCallbackAvailable;
    private bool _applicationTypedHandlerAvailable;
    private bool _applicationToolbarEnabled;
    private long _applicationActionsRevision;
    private long _applicationRevisionHighWater;
    private object? _applicationAuthorityIdentity;
    private string? _applicationRegistrationError;
    private long _applicationActionRequestVersion;
    private ApplicationActionRequest? _applicationActionRequest;
    // Native and application menus share one dismissal authority.
    private void CloseContextMenuUnderLock()
    {
        RetirePopupApplicationActionsUnderLock();
        _contextMenu = null;
    }

    private void SetContextMenuUnderLock(DocumentCanvasContextMenuState? menu)
    {
        if (_contextMenu?.ApplicationContext?.RequestVersion != menu?.ApplicationContext?.RequestVersion)
            RetirePopupApplicationActionsUnderLock();
        _contextMenu = menu;
    }
    internal bool HasApplicationActions
    {
        get
        {
            lock (_sync) return !_disposed && !_lifetimeToken.IsCancellationRequested && _applicationRegistrationError is null &&
                _applicationActionProvider is not null && (_applicationActionCallbackAvailable || _applicationTypedHandlerAvailable);
        }
    }

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable) =>
        SetApplicationActions(provider, callbackAvailable, false, false, 0);

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable,
        bool typedHandlerAvailable, bool toolbarEnabled, long revision, object? authorityIdentity = null)
    {
        lock (_sync)
        {
            if (_disposed || _lifetimeToken.IsCancellationRequested || _applicationActionProvider == provider &&
                _applicationActionCallbackAvailable == callbackAvailable &&
                _applicationTypedHandlerAvailable == typedHandlerAvailable && _applicationToolbarEnabled == toolbarEnabled &&
                _applicationActionsRevision == revision && Equals(_applicationAuthorityIdentity, authorityIdentity)) return;
            var popup = _contextMenu;
            var oldContext = popup?.ApplicationContext;
            var policyChanged = _applicationActionProvider != provider ||
                _applicationActionCallbackAvailable != callbackAvailable || _applicationTypedHandlerAvailable != typedHandlerAvailable ||
                _applicationActionsRevision != revision || !Equals(_applicationAuthorityIdentity, authorityIdentity);
            if (policyChanged) RetireApplicationActionsUnderLock();
            else if (!toolbarEnabled) RetireToolbarApplicationActionsUnderLock();
            _applicationActionProvider = provider;
            _applicationActionCallbackAvailable = callbackAvailable;
            _applicationTypedHandlerAvailable = typedHandlerAvailable;
            _applicationToolbarEnabled = toolbarEnabled;
            _applicationActionsRevision = revision;
            _applicationAuthorityIdentity = authorityIdentity;
            _applicationRegistrationError = callbackAvailable && typedHandlerAvailable ? "BPMN_APPLICATION_HANDLER_CONFLICT" :
                revision < 0 || revision < _applicationRevisionHighWater ? "BPMN_APPLICATION_REVISION_INVALID" : null;
            if (_applicationRegistrationError is not null)
            {
                RetireApplicationActionsUnderLock();
                if (_contextMenu is { } invalidMenu)
                    _contextMenu = invalidMenu with { ApplicationContext = null, ApplicationActions = [], IsApplicationDiscoveryPending = false };
                if (_contextMenu is { } empty && !HasNativeApplicationMenuActions(empty)) _contextMenu = null;
                ReportApplicationFailureUnderLock(_applicationRegistrationError);
            }
            else
            {
                _applicationRevisionHighWater = revision;
                if (policyChanged && oldContext is not null && popup is not null)
                {
                    _contextMenu = popup with { ApplicationContext = null, ApplicationActions = [], IsApplicationDiscoveryPending = false };
                    var presentation = CapturePresentation();
                    if (HasApplicationActions && presentation.Stamp == oldContext.Stamp &&
                        TryResolveApplicationOwnerUnderLock(oldContext.Target, oldContext.Part, out var target, out var part))
                    {
                        var request = PrepareApplicationActionsUnderLock(popup.CssPosition, presentation, target, part);
                        if (request is not null) _ = StartApplicationDiscoveryAsync(request);
                    }
                    else if (!HasNativeApplicationMenuActions(_contextMenu)) _contextMenu = null;
                }
                ReconcileApplicationToolbarUnderLock();
            }
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    internal async ValueTask<BpmnPresentationOperationResult> OpenApplicationContextMenuFromSelectionAsync(
        CancellationToken cancellationToken = default)
    {
        ApplicationActionRequest? discovery = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        try
        {
            lock (_sync)
            {
                if (!HasApplicationActions || _propertiesFormOpen || _modelViewPropertiesFormOpen ||
                    _session?.CaptureState() is not { IsCurrentScenePresented: true, CurrentScene: { } scene } state ||
                    state.EditorState.ActiveGesture is not null)
                    return PresentationOutcome(BpmnModelerOperationStatus.Unavailable);
                var presentation = CapturePresentation();
                if (!TryResolveApplicationTarget(presentation, out var target, out var part))
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_TARGET_UNAVAILABLE");
                var bounds = part?.Bounds ?? new RectD(target!.Parts.Min(static item => item.Bounds.Left),
                    target.Parts.Min(static item => item.Bounds.Top),
                    target.Parts.Max(static item => item.Bounds.Right) - target.Parts.Min(static item => item.Bounds.Left),
                    target.Parts.Max(static item => item.Bounds.Bottom) - target.Parts.Min(static item => item.Bounds.Top));
                var point = scene.ViewportTransform.TransformPoint(new PointD(bounds.Left, bounds.Bottom));
                CloseContextMenuUnderLock();
                // Keyboard selection has no pointer hit. Native policy may still use its
                // unique canonical representation without changing the public hit context.
                var nativePart = part ?? ResolveKeyboardNativeMenuPart(target!);
                if (nativePart is not null && _session is { } session)
                {
                    var native = CreateContextMenuIfCurrent(null, target!.VisualStateId, point, _surfaceSize,
                        session, _deletionCatalog, _scopeNavigationCatalog, _backgroundActionCatalog,
                        _semanticSceneViewActionCatalog, _semanticSceneCommandActionCatalog, _documentCreationIdentityProvider,
                        nativePart, target.SemanticId);
                    SetContextMenuUnderLock(CompleteNativeContextMenuUnderLock(native, session));
                }
                discovery = PrepareApplicationActionsUnderLock(point, presentation, target!, part);
                return PresentationOutcome(discovery is null ? BpmnModelerOperationStatus.Unavailable : BpmnModelerOperationStatus.Succeeded);
            }
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            if (discovery is not null) _ = DiscoverApplicationActionsAsync(discovery);
        }
    }

    private ApplicationActionRequest? PrepareApplicationActionsUnderLock(PointD cssPoint,
        BpmnPresentationSnapshot? presentation = null, BpmnSemanticSelectionTarget? target = null,
        BpmnPresentationPart? part = null)
    {
        if (!HasApplicationActions || _surfaceSize is not { } size || _session is not { } session) return null;
        presentation ??= CapturePresentation();
        if (target is null && !TryResolveApplicationTarget(presentation, out target, out part)) return null;
        var state = session.CaptureState();
        if (!presentation.IsAvailable || !state.IsCurrentScenePresented || state.CurrentScene is null) return null;
        RetirePopupApplicationActionsUnderLock();
        if (_applicationToolbarRequest?.InvocationPending == true) RetireToolbarApplicationActionsUnderLock();
        var context = new BpmnApplicationActionContext(presentation, target!, part, ++_applicationActionRequestVersion, _applicationActionsRevision);
        var request = new ApplicationActionRequest(context, _applicationActionProvider!, false, _applicationTypedHandlerAvailable, state.CurrentScene, _applicationActionObservation);
        _applicationActionRequest = request;
        var menu = _contextMenu ?? new DocumentCanvasContextMenuState(
            DocumentCanvasContextMenuKind.Element, cssPoint, target!.VisualStateId,
            target.VisualStateId is null ? target.SemanticId : null, part?.SceneObjectId,
            null, null, null, null, state.DocumentRevision, state.Generation, state.CurrentScene,
            presentation.ActiveScopeId, null, [], [], [], presentation.ActiveScopeId,
            part is null ? null : state.CurrentScene.Items.SingleOrDefault(item => item.Id == part.SceneObjectId)?.SpatialRegion,
            PresentationContext: state.PresentationContext);
        _contextMenu = menu with
        {
            ApplicationContext = context,
            ApplicationActions = [],
            IsApplicationDiscoveryPending = true,
        };
        _contextMenu = _contextMenu.ClampApplicationActions(size);
        return request;
    }

    private bool TryResolveApplicationTarget(BpmnPresentationSnapshot presentation,
        out BpmnSemanticSelectionTarget? target, out BpmnPresentationPart? part)
    {
        part = presentation.PrimaryPart;
        target = null;
        if (!presentation.IsAvailable || presentation.Selection.Select(static item => (item.SemanticId, item.Kind)).Distinct().Count() != 1) return false;
        if (part is not null)
        {
            var partId = part.SceneObjectId;
            var matches = presentation.Selection.Where(item => item.Parts.Any(candidate => candidate.SceneObjectId == partId)).ToArray();
            if (matches.Length != 1) return false;
            target = matches[0];
            return true;
        }
        if (presentation.Selection.Length != 1) return false;
        target = presentation.Selection[0];
        // An unknown primary part stays unknown; do not invent a label/body authority.
        return HasSingleApplicationPresentationInstance(target);
    }

    private bool HasSingleApplicationPresentationInstance(BpmnSemanticSelectionTarget target) =>
        _session?.CaptureState().CurrentScene is { } scene && !target.Parts.IsEmpty &&
        BpmnPresentationSelection.HasSinglePresentationInstance(scene.Items.Where(item =>
            target.Parts.Any(part => part.SceneObjectId == item.Id)));

    private static BpmnPresentationPart? ResolveKeyboardNativeMenuPart(BpmnSemanticSelectionTarget target)
    {
        var kind = target.VisualStateId is null ? BpmnPresentationPartKind.SemanticRegion :
            target.Kind == BpmnSemanticTargetKind.Relationship ? BpmnPresentationPartKind.ConnectorPath :
            BpmnPresentationPartKind.NodeBody;
        var candidates = target.Parts.Where(part => part.Kind == kind).Take(2).ToArray();
        return candidates.Length == 1 ? candidates[0] : null;
    }

    private async Task DiscoverApplicationActionsAsync(ApplicationActionRequest request)
    {
        try
        {
            var actions = await ReadApplicationActionsAsync(request, BpmnActionObservationPhase.Discovery).ConfigureAwait(false);
            await EnterModelerOperationAsync(request.Token).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (!CanInstallApplicationDiscoveryUnderLock(request))
                    {
                        request.Observation?.Record(BpmnActionObservationStage.DiscoveryInstallRejected);
                        return;
                    }
                    request.Actions = actions;
                    request.DiscoveryPending = false;
                    request.Observation?.Record(BpmnActionObservationStage.DiscoveryInstalled, actions: actions);
                    if (request.IsToolbar) ReconcileApplicationToolbarUnderLock();
                    else
                    {
                        _contextMenu = _contextMenu! with { ApplicationActions = actions, IsApplicationDiscoveryPending = false };
                        if (_surfaceSize is { } size) _contextMenu = _contextMenu.ClampApplicationActions(size);
                        if (actions.IsEmpty && !HasNativeApplicationMenuActions(_contextMenu)) CloseContextMenuUnderLock();
                    }
                }
            }
            finally { await CompleteModelerOperationAsync().ConfigureAwait(false); }
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested) { }
#pragma warning disable CA1031 // Application discovery faults are bounded host feedback, never native edits.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            await FailApplicationRequestAsync(request, exception is InvalidApplicationActionsException
                ? "BPMN_APPLICATION_ACTIONS_INVALID" : "BPMN_APPLICATION_DISCOVERY_FAILED").ConfigureAwait(false);
        }
#pragma warning restore CA1031
    }

    private static async ValueTask<ImmutableArray<BpmnApplicationAction>> ReadApplicationActionsAsync(ApplicationActionRequest request,
        BpmnActionObservationPhase phase, BpmnApplicationActionContext? currentContext = null)
    {
        IEnumerable<BpmnApplicationAction> result;
        try
        {
            request.Token.ThrowIfCancellationRequested();
            request.Observation?.Record(BpmnActionObservationStage.ProviderCallStarted, phase);
            var work = request.Provider(currentContext ?? request.Context, request.Token).AsTask();
            request.Observation?.Record(BpmnActionObservationStage.HostProviderTaskCaptured, phase, detail: work.Status.ToString());
            result = await work.WaitAsync(request.Token).ConfigureAwait(false);
            request.Observation?.Record(BpmnActionObservationStage.ProviderResultReceived, phase);
        }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested)
        {
            request.Observation?.Record(BpmnActionObservationStage.ProviderWaitCancelled, phase);
            throw;
        }
#pragma warning disable CA1031 // Observe only; preserve the existing failure path and exception.
        catch (Exception)
        {
            request.Observation?.Record(BpmnActionObservationStage.ProviderFailed, phase);
            throw;
        }
#pragma warning restore CA1031
        request.Token.ThrowIfCancellationRequested();
        if (result is null) throw new InvalidApplicationActionsException();
        var actions = ImmutableArray.CreateBuilder<BpmnApplicationAction>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var action in result)
        {
            request.Token.ThrowIfCancellationRequested();
            if (action is null || string.IsNullOrWhiteSpace(action.Id) ||
                action.Id != action.Id.Trim() || action.Id.Any(char.IsControl) ||
                string.IsNullOrWhiteSpace(action.Label) || !ids.Add(action.Id))
                throw new InvalidApplicationActionsException();
            actions.Add(action);
        }
        return actions.ToImmutable();
    }

    internal async ValueTask<BpmnPresentationOperationResult> ExecuteApplicationActionAsync(
        long requestVersion, string actionId, CancellationToken cancellationToken = default)
    {
        ApplicationActionRequest? request;
        BpmnApplicationActionContext? availabilityContext = null;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        try
        {
            lock (_sync)
            {
                request = FindApplicationRequestUnderLock(requestVersion);
                if (request is null || request.Context.RequestVersion != requestVersion || request.InvocationPending ||
                    !TryCaptureApplicationContextUnderLock(request, requireMenu: true, out availabilityContext) ||
                    request.DiscoveryPending ||
                    !request.Actions.Any(action => action.Id == actionId && action.IsEnabled) ||
                    CompetingApplicationInvocationUnderLock(request))
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_ACTION_UNAVAILABLE");
                request.InvocationPending = true;
                request.Observation?.Record(BpmnActionObservationStage.InvocationAdmitted, actionId: actionId);
            }
        }
        finally { await CompleteModelerOperationAsync().ConfigureAwait(false); }
        using var cancellation = cancellationToken.Register(() => CancelApplicationRequest(request));
        try
        {
            // Re-query outside native locks against the currently revalidated immutable target.
            var actions = await ReadApplicationActionsAsync(request, BpmnActionObservationPhase.Recheck, availabilityContext).ConfigureAwait(false);
            await EnterModelerOperationAsync(request.Token).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (!TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _))
                    {
                        request.Observation?.Record(BpmnActionObservationStage.RecheckRejected, actionId: actionId);
                        return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_ACTION_STALE");
                    }
                    request.Actions = actions;
                    request.Observation?.Record(BpmnActionObservationStage.RecheckInstalled, actionId: actionId, actions: actions);
                    if (!request.IsToolbar) _contextMenu = _contextMenu! with { ApplicationActions = actions };
                    if (!actions.Any(action => action.Id == actionId && action.IsEnabled))
                    {
                        request.InvocationPending = false;
                        return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_ACTION_DISABLED");
                    }
                    if (ModelerNotifications is null)
                    {
                        request.InvocationPending = false;
                        return PresentationOutcome(BpmnModelerOperationStatus.Unavailable);
                    }
                    _ = ModelerNotifications.Enqueue(new BpmnApplicationActionDispatch(this, request.Context.RequestVersion, actionId, request.IsTyped, request.Observation));
                }
            }
            finally
            {
                await CompleteModelerOperationAsync().ConfigureAwait(false);
                await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            }
            return PresentationOutcome(BpmnModelerOperationStatus.Succeeded);
        }
        catch (OperationCanceledException) when (request.Cancellation.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
            lock (_sync)
            {
                RetireApplicationRequestUnderLock(request);
            }
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
            return PresentationOutcome(BpmnModelerOperationStatus.Cancelled);
        }
#pragma warning disable CA1031 // Application discovery faults are isolated from persistent operations.
        catch (Exception exception) when (IsNonFatal(exception))
        {
            await FailApplicationRequestAsync(request, exception is InvalidApplicationActionsException
                ? "BPMN_APPLICATION_ACTIONS_INVALID" : "BPMN_APPLICATION_DISCOVERY_FAILED").ConfigureAwait(false);
            return PresentationOutcome(BpmnModelerOperationStatus.Failed);
        }
#pragma warning restore CA1031
    }

    internal bool TryBeginApplicationActionInvocation(long version, string actionId,
        out BpmnApplicationActionInvocationEventArgs? args, BpmnActionObservationRequest? observation = null)
    {
        lock (_sync)
        {
            args = null;
            if (FindApplicationRequestUnderLock(version) is not { } request ||
                !request.InvocationPending || request.Started ||
                !TryCaptureApplicationContextUnderLock(request, requireMenu: true, out var context) ||
                !request.Actions.Any(action => action.Id == actionId && action.IsEnabled) ||
                CompetingApplicationInvocationUnderLock(request))
            {
                observation?.Record(BpmnActionObservationStage.EntryRejected, actionId: actionId);
                return false;
            }
            request.Started = true;
            // Consume both surfaces without cancelling the accepted invocation.
            if (request.IsToolbar) RetirePopupApplicationActionsUnderLock();
            else RetireToolbarApplicationActionsUnderLock();
            _contextMenu = null;
            _applicationToolbar = null;
            _applicationToolbarConsumed = true;
            args = new(actionId, context!, request.Token);
            observation?.Record(BpmnActionObservationStage.EntryAccepted, actionId: actionId);
        }
        _ = NotifyApplicationMenuConsumedAsync();
        return true;
    }

    private async Task NotifyApplicationMenuConsumedAsync()
    {
        // Do not interpose a reentrant view notification between dispatch validation and invocation.
        await Task.Yield();
        await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
    }

    internal void CompleteApplicationActionInvocation(long version, bool failed, BpmnActionObservationRequest? observation = null)
    {
        lock (_sync)
        {
            if (FindApplicationRequestUnderLock(version) is not { } request)
            {
                observation?.Record(BpmnActionObservationStage.CompletionIgnored, detail: "Request retired");
                return;
            }
            if (failed && !_disposed && TryCaptureApplicationContextUnderLock(request, requireMenu: false, out _))
                ReportApplicationFailureUnderLock("BPMN_APPLICATION_INVOCATION_FAILED");
            observation?.Record(BpmnActionObservationStage.CompletionProcessed, detail: failed ? "Legacy failure" : "Legacy completion");
            RetireApplicationRequestUnderLock(request);
            // Completion permits a fresh selected-target request; old request identities
            // remain retired, and reconciliation never clears an explicit dismissal.
            _applicationToolbarConsumed = false;
            ReconcileApplicationToolbarUnderLock();
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    internal void CompleteTypedApplicationActionInvocation(long version, BpmnApplicationActionResult? result, BpmnActionObservationRequest? observation = null)
    {
        lock (_sync)
        {
            if (FindApplicationRequestUnderLock(version) is not { } request)
            {
                observation?.Record(BpmnActionObservationStage.CompletionIgnored, detail: "Request retired");
                return;
            }
            if (TryCaptureApplicationContextUnderLock(request, requireMenu: false, out _))
            {
                result ??= new(BpmnModelerOperationStatus.Failed);
                if (result.Status is BpmnModelerOperationStatus.Failed or BpmnModelerOperationStatus.Unavailable)
                {
                    var diagnostics = result.Diagnostics.IsEmpty
                        ? ImmutableArray.Create(Error(result.Status == BpmnModelerOperationStatus.Unavailable
                            ? "BPMN_APPLICATION_ACTION_UNAVAILABLE" : "BPMN_APPLICATION_INVOCATION_FAILED",
                            "The application action could not be supplied or invoked.")) : result.Diagnostics;
                    ReplaceInteractionDiagnosticsUnderLock(diagnostics);
                    _ = ModelerNotifications?.Enqueue(new BpmnModelerOperationFailedEventArgs(
                        BpmnModelerOperation.ApplicationAction, result.Status, diagnostics));
                }
                else if (result.Status == BpmnModelerOperationStatus.Succeeded && !result.Diagnostics.IsEmpty)
                    ReplaceInteractionDiagnosticsUnderLock(result.Diagnostics);
                observation?.Record(BpmnActionObservationStage.CompletionApplied, detail: result.Status.ToString());
            }
            else observation?.Record(BpmnActionObservationStage.CompletionIgnored, detail: "Target stale");
            RetireApplicationRequestUnderLock(request);
            // Completion permits a fresh selected-target request; old request identities
            // remain retired, and reconciliation never clears an explicit dismissal.
            _applicationToolbarConsumed = false;
            ReconcileApplicationToolbarUnderLock();
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    private bool TryCaptureApplicationContextUnderLock(ApplicationActionRequest request, bool requireMenu,
        out BpmnApplicationActionContext? context)
    {
        context = null;
        if (_disposed || !HasApplicationActions || !IsCurrentApplicationRequestUnderLock(request) ||
            request.Token.IsCancellationRequested || request.Context.ApplicationActionsRevision != _applicationActionsRevision ||
            requireMenu && (request.IsToolbar ? !_applicationToolbarEnabled || _applicationToolbarDismissed ||
                _contextMenu is not null || _applicationToolbarConsumed :
                _contextMenu?.ApplicationContext?.RequestVersion != request.Context.RequestVersion)) return false;
        var current = CapturePresentation();
        if (current.Stamp != request.Context.Stamp ||
            !TryResolveApplicationOwnerUnderLock(request.Context.Target, request.Context.Part, out var target, out var part)) return false;
        if (request.IsToolbar)
        {
            if (current.Selection.Select(static item => (item.SemanticId, item.Kind)).Distinct().Count() != 1 ||
                !current.Selection.Any(selected => SameApplicationOwner(selected, target!)) ||
                !TryResolveApplicationToolbarPlacementUnderLock(target!, part, request.Actions, out _, out _)) return false;
        }
        else if (_session?.CaptureState().CurrentScene is not { } scene ||
            !Equals(scene.ViewportTransform, request.SourceScene.ViewportTransform) ||
            !target!.Parts.SequenceEqual(request.Context.Target.Parts) ||
            !SameApplicationTargetGeometry(request.SourceScene, scene, target.Parts)) return false;
        context = new(current, target!, part, request.Context.RequestVersion, _applicationActionsRevision);
        return true;
    }

    internal bool IsApplicationActionRequestActionable(long version)
    {
        lock (_sync) return FindApplicationRequestUnderLock(version) is { DiscoveryPending: false } request &&
            TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _);
    }

    private bool CanInstallApplicationDiscoveryUnderLock(ApplicationActionRequest request)
    {
        if (_disposed || !HasApplicationActions || !IsCurrentApplicationRequestUnderLock(request) ||
            request.Token.IsCancellationRequested || request.Context.ApplicationActionsRevision != _applicationActionsRevision ||
            !request.IsToolbar && _contextMenu?.ApplicationContext?.RequestVersion != request.Context.RequestVersion ||
            request.IsToolbar && (!_applicationToolbarEnabled || _applicationToolbarDismissed)) return false;
        if (_session?.CaptureState() is not { IsClosed: false } state || state.Status == EditingSessionStatus.RuntimeFaulted) return false;
        var stamp = request.Context.Stamp;
        return stamp.AttachmentVersion == _documentSessionVersion && stamp.DocumentId == state.DocumentId &&
            stamp.Revision == state.DocumentRevision && stamp.ContextVersion == _presentationContextVersion &&
            stamp.SelectionVersion == _presentationSelectionVersion;
    }

    private static bool SameApplicationTargetGeometry(Canvas2DScene before, Canvas2DScene after,
        ImmutableArray<BpmnPresentationPart> parts) => parts.All(part =>
        before.Items.SingleOrDefault(item => item.Id == part.SceneObjectId) is { } oldPart &&
        after.Items.SingleOrDefault(item => item.Id == part.SceneObjectId) is { } newPart &&
        ApplicationPartGeometryEquals(oldPart, newPart));

    internal static bool ApplicationPartGeometryEquals(Canvas2DSceneItem before, Canvas2DSceneItem after) =>
        before.Id == after.Id && before.Geometry.Equals(after.Geometry) && before.Bounds == after.Bounds &&
        before.Transform == after.Transform && before.Clip == after.Clip && before.IsVisible == after.IsVisible &&
        Equals(before.ProcessPlacement, after.ProcessPlacement) && Equals(before.SpatialRegion, after.SpatialRegion) &&
        Equals(before.ConnectorPresentationMapping, after.ConnectorPresentationMapping) &&
        Equals(before.DerivedRegionMapping, after.DerivedRegionMapping);

    private void ObserveApplicationActionsUnderLock(EditingSessionState state)
    {
        foreach (var request in new[] { _applicationActionRequest, _applicationToolbarRequest })
        {
            if (request is null) continue;
            var stamp = request.Context.Stamp;
            if (state.IsClosed || state.Status == EditingSessionStatus.RuntimeFaulted ||
                stamp.AttachmentVersion != _documentSessionVersion || stamp.DocumentId != state.DocumentId ||
                stamp.Revision != state.DocumentRevision || stamp.ContextVersion != _presentationContextVersion ||
                stamp.SelectionVersion != _presentationSelectionVersion ||
                state.Status == EditingSessionStatus.Ready && state.IsCurrentScenePresented &&
                !CanRetainApplicationRequestUnderLock(request))
                RetireApplicationRequestUnderLock(request);
        }
        ReconcileApplicationToolbarUnderLock();
    }

    private bool CanRetainApplicationRequestUnderLock(ApplicationActionRequest request)
    {
        // A popup temporarily owns interaction; it does not dismiss the selected-target toolbar.
        if (request.IsToolbar && _contextMenu is not null)
            return CapturePresentation().Stamp == request.Context.Stamp;
        return TryCaptureApplicationContextUnderLock(request, requireMenu: !request.Started, out _);
    }

    private async Task FailApplicationRequestAsync(ApplicationActionRequest request, string code)
    {
        try { await EnterModelerOperationAsync(request.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { return; }
        try
        {
            lock (_sync)
            {
                if (!CanInstallApplicationDiscoveryUnderLock(request))
                {
                    request.Observation?.Record(BpmnActionObservationStage.FailureInstallRejected, detail: code);
                    return;
                }
                ReportApplicationFailureUnderLock(code);
                request.Observation?.Record(BpmnActionObservationStage.FailureInstalled, detail: code);
                request.Actions = [];
                request.DiscoveryPending = false;
                request.InvocationPending = false;
                if (request.IsToolbar) _applicationToolbar = null;
                else
                {
                    _contextMenu = _contextMenu! with { ApplicationActions = [], IsApplicationDiscoveryPending = false };
                    if (!HasNativeApplicationMenuActions(_contextMenu)) CloseContextMenuUnderLock();
                }
            }
        }
        finally
        {
            await CompleteModelerOperationAsync().ConfigureAwait(false);
            await NotifyStateChangedSafelyAsync().ConfigureAwait(false);
        }
    }

    private void ReportApplicationFailureUnderLock(string code)
    {
        var diagnostics = ImmutableArray.Create(Error(code, "The application action could not be supplied or invoked."));
        ReplaceInteractionDiagnosticsUnderLock(diagnostics);
        _ = ModelerNotifications?.Enqueue(new BpmnModelerOperationFailedEventArgs(
            BpmnModelerOperation.ApplicationAction, BpmnModelerOperationStatus.Failed, diagnostics));
    }

    private void RetireApplicationActionsUnderLock()
    {
        RetirePopupApplicationActionsUnderLock();
        RetireToolbarApplicationActionsUnderLock();
    }

    private void RetirePopupApplicationActionsUnderLock()
    {
        var request = _applicationActionRequest;
        _applicationActionRequest = null;
        request?.Observation?.Record(BpmnActionObservationStage.RequestRetired, detail: "Popup retired");
        if (request is not null) CancelApplicationRequest(request);
    }

    private void RetireApplicationRequestUnderLock(ApplicationActionRequest request)
    {
        if (ReferenceEquals(request, _applicationToolbarRequest)) RetireToolbarApplicationActionsUnderLock();
        if (ReferenceEquals(request, _applicationActionRequest))
        {
            RetirePopupApplicationActionsUnderLock();
            if (_contextMenu?.ApplicationContext?.RequestVersion == request.Context.RequestVersion) _contextMenu = null;
        }
    }

    private ApplicationActionRequest? FindApplicationRequestUnderLock(long version) =>
        _applicationActionRequest?.Context.RequestVersion == version ? _applicationActionRequest :
        _applicationToolbarRequest?.Context.RequestVersion == version ? _applicationToolbarRequest : null;

    private bool IsCurrentApplicationRequestUnderLock(ApplicationActionRequest request) =>
        ReferenceEquals(request, _applicationActionRequest) || ReferenceEquals(request, _applicationToolbarRequest);

    private bool CompetingApplicationInvocationUnderLock(ApplicationActionRequest request) =>
        new[] { _applicationActionRequest, _applicationToolbarRequest }.Any(other =>
            other is not null && !ReferenceEquals(other, request) && other.InvocationPending);

    private async Task StartApplicationDiscoveryAsync(ApplicationActionRequest request)
    {
        // Provider entry must never occur inside reconciliation's native lock.
        await Task.Yield();
        await DiscoverApplicationActionsAsync(request).ConfigureAwait(false);
    }

    internal static bool HasNativeApplicationMenuActions(DocumentCanvasContextMenuState menu) =>
        menu.PropertiesAvailable || menu.ScopeNavigationAction is not null || menu.ConnectorRouteAction is not null ||
        menu.ConnectorAnchorAction is not null || menu.NodeLabelAction is not null || menu.ConnectorLabelAction is not null ||
        menu.DeletionAction is not null || !menu.SemanticViewActions.IsEmpty || !menu.SemanticCommandActions.IsEmpty;

    private static void CancelApplicationRequest(ApplicationActionRequest request)
    {
        if (Interlocked.Exchange(ref request.CancellationStarted, 1) == 0)
            _ = CancelAndDisposeApplicationRequestAsync(request);
    }

    private static async Task CancelAndDisposeApplicationRequestAsync(ApplicationActionRequest request)
    {
        // CancelAsync schedules application registrations outside the host's lock, including on disposal.
#pragma warning disable CA1031 // External cancellation registrations cannot break host cleanup.
        try { await request.Cancellation.CancelAsync().ConfigureAwait(false); }
        catch (Exception exception) when (IsNonFatal(exception)) { }
        finally { request.Cancellation.Dispose(); }
#pragma warning restore CA1031
    }

    private sealed class ApplicationActionRequest
    {
        internal ApplicationActionRequest(BpmnApplicationActionContext context, BpmnApplicationActionProvider provider,
            bool isToolbar, bool isTyped, Canvas2DScene sourceScene, BpmnApplicationActionObservation? observation = null)
        {
            Context = context;
            Provider = provider;
            IsToolbar = isToolbar;
            IsTyped = isTyped;
            SourceScene = sourceScene;
            Cancellation = new();
            Token = Cancellation.Token;
            Observation = observation?.BeginRequest(context, isToolbar, isTyped);
        }
        internal BpmnApplicationActionContext Context { get; }
        internal BpmnActionObservationRequest? Observation { get; }
        internal BpmnApplicationActionProvider Provider { get; }
        internal CancellationTokenSource Cancellation { get; }
        internal CancellationToken Token { get; }
        internal bool IsToolbar { get; }
        internal bool IsTyped { get; }
        internal Canvas2DScene SourceScene { get; }
        internal ImmutableArray<BpmnApplicationAction> Actions { get; set; } = [];
        internal bool DiscoveryPending { get; set; } = true;
        internal int CancellationStarted;
        internal bool InvocationPending { get; set; }
        internal bool Started { get; set; }
    }

    private sealed class InvalidApplicationActionsException : Exception { }
}

internal sealed class BpmnApplicationActionDispatch(DocumentCanvasHost host, long version, string actionId, bool isTyped = false,
    BpmnActionObservationRequest? observation = null)
{
    internal bool IsTyped => isTyped;
    internal void Observe(BpmnActionObservationStage stage) => observation?.Record(stage, actionId: actionId);
    internal bool TryBeginInvocation(out BpmnApplicationActionInvocationEventArgs? args)
    {
        return host.TryBeginApplicationActionInvocation(version, actionId, out args, observation);
    }
    internal void Complete()
    {
        Observe(BpmnActionObservationStage.CompletionReceived);
        host.CompleteApplicationActionInvocation(version, failed: false, observation);
    }
    internal void Complete(BpmnApplicationActionResult result)
    {
        Observe(BpmnActionObservationStage.CompletionReceived);
        host.CompleteTypedApplicationActionInvocation(version, result, observation);
    }
    internal void FailTyped(Exception exception, CancellationToken token) => Complete(
        new(exception is OperationCanceledException && token.IsCancellationRequested ?
            BpmnModelerOperationStatus.Cancelled : BpmnModelerOperationStatus.Failed));
    internal void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Observe(BpmnActionObservationStage.CompletionReceived);
        host.CompleteApplicationActionInvocation(version, failed: true, observation);
    }
}
