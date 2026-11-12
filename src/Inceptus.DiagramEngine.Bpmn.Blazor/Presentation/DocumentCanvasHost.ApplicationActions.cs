using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private BpmnApplicationActionProvider? _applicationActionProvider;
    private bool _applicationActionCallbackAvailable;
    private long _applicationActionRequestVersion;
    private ApplicationActionRequest? _applicationActionRequest;
    // Native and application menus share one dismissal authority.
    private void CloseContextMenuUnderLock()
    {
        RetireApplicationActionsUnderLock();
        _contextMenu = null;
    }

    private void SetContextMenuUnderLock(DocumentCanvasContextMenuState? menu)
    {
        if (_contextMenu?.ApplicationContext?.RequestVersion != menu?.ApplicationContext?.RequestVersion)
            RetireApplicationActionsUnderLock();
        _contextMenu = menu;
    }
    internal bool HasApplicationActions
    {
        get { lock (_sync) return !_disposed && _applicationActionProvider is not null && _applicationActionCallbackAvailable; }
    }

    internal void SetApplicationActions(BpmnApplicationActionProvider? provider, bool callbackAvailable)
    {
        lock (_sync)
        {
            if (_disposed || _applicationActionProvider == provider && _applicationActionCallbackAvailable == callbackAvailable) return;
            RetireApplicationActionsUnderLock();
            _applicationActionProvider = provider;
            _applicationActionCallbackAvailable = callbackAvailable;
            if (_contextMenu?.ApplicationContext is not null) _contextMenu = null;
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
                if (part is not null && _session is { } session)
                {
                    var native = CreateContextMenuIfCurrent(null, target!.VisualStateId, point, _surfaceSize,
                        session, _deletionCatalog, _scopeNavigationCatalog, _backgroundActionCatalog,
                        _semanticSceneViewActionCatalog, _semanticSceneCommandActionCatalog, _documentCreationIdentityProvider,
                        part, target.SemanticId);
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
        RetireApplicationActionsUnderLock();
        var context = new BpmnApplicationActionContext(presentation, target!, part, ++_applicationActionRequestVersion);
        var request = new ApplicationActionRequest(context, _applicationActionProvider!);
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
        if (!presentation.IsAvailable) return false;
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

    private async Task DiscoverApplicationActionsAsync(ApplicationActionRequest request)
    {
        try
        {
            var actions = await ReadApplicationActionsAsync(request).ConfigureAwait(false);
            await EnterModelerOperationAsync(request.Token).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (!TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _)) return;
                    _contextMenu = _contextMenu! with { ApplicationActions = actions, IsApplicationDiscoveryPending = false };
                    if (_surfaceSize is { } size) _contextMenu = _contextMenu.ClampApplicationActions(size);
                    if (actions.IsEmpty && !HasNativeApplicationMenuActions(_contextMenu)) CloseContextMenuUnderLock();
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

    private static async ValueTask<ImmutableArray<BpmnApplicationAction>> ReadApplicationActionsAsync(ApplicationActionRequest request)
    {
        request.Token.ThrowIfCancellationRequested();
        var result = await request.Provider(request.Context, request.Token).AsTask().WaitAsync(request.Token).ConfigureAwait(false);
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
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetimeToken);
        try { await EnterModelerOperationAsync(linked.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (linked.IsCancellationRequested)
        { return PresentationOutcome(BpmnModelerOperationStatus.Cancelled); }
        try
        {
            lock (_sync)
            {
                request = _applicationActionRequest;
                if (request is null || request.Context.RequestVersion != requestVersion || request.InvocationPending ||
                    !TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _) ||
                    _contextMenu!.IsApplicationDiscoveryPending ||
                    !_contextMenu.ApplicationActions.Any(action => action.Id == actionId && action.IsEnabled))
                    return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_ACTION_UNAVAILABLE");
                request.InvocationPending = true;
            }
        }
        finally { await CompleteModelerOperationAsync().ConfigureAwait(false); }
        using var cancellation = cancellationToken.Register(() => CancelApplicationRequest(request));
        try
        {
            // Re-query outside native locks: application-owned enablement may have changed.
            var actions = await ReadApplicationActionsAsync(request).ConfigureAwait(false);
            await EnterModelerOperationAsync(request.Token).ConfigureAwait(false);
            try
            {
                lock (_sync)
                {
                    if (!TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _))
                        return PresentationOutcome(BpmnModelerOperationStatus.Rejected, "BPMN_APPLICATION_ACTION_STALE");
                    _contextMenu = _contextMenu! with { ApplicationActions = actions };
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
                    _ = ModelerNotifications.Enqueue(new BpmnApplicationActionDispatch(this, request.Context.RequestVersion, actionId));
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
                if (ReferenceEquals(request, _applicationActionRequest)) CloseContextMenuUnderLock();
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
        out BpmnApplicationActionInvocationEventArgs? args)
    {
        lock (_sync)
        {
            args = null;
            if (_applicationActionRequest is not { } request || request.Context.RequestVersion != version ||
                !request.InvocationPending || request.Started ||
                !TryCaptureApplicationContextUnderLock(request, requireMenu: true, out var context) ||
                !_contextMenu!.ApplicationActions.Any(action => action.Id == actionId && action.IsEnabled)) return false;
            request.Started = true;
            // Consuming this menu is not cancellation of the action just accepted for invocation.
            _contextMenu = null;
            args = new(actionId, context!, request.Token);
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

    internal void CompleteApplicationActionInvocation(long version, bool failed)
    {
        lock (_sync)
        {
            if (_applicationActionRequest is not { } request || request.Context.RequestVersion != version) return;
            if (failed && !_disposed && TryCaptureApplicationContextUnderLock(request, requireMenu: false, out _))
                ReportApplicationFailureUnderLock("BPMN_APPLICATION_INVOCATION_FAILED");
            RetireApplicationActionsUnderLock();
        }
        _ = NotifyStateChangedSafelyAsync();
    }

    private bool TryCaptureApplicationContextUnderLock(ApplicationActionRequest request, bool requireMenu,
        out BpmnApplicationActionContext? context)
    {
        context = null;
        if (_disposed || !HasApplicationActions || !ReferenceEquals(request, _applicationActionRequest) ||
            request.Cancellation.IsCancellationRequested || requireMenu &&
            _contextMenu?.ApplicationContext?.RequestVersion != request.Context.RequestVersion) return false;
        var current = CapturePresentation();
        if (current.Stamp != request.Context.Stamp) return false;
        var target = current.Selection.SingleOrDefault(item => item.SemanticId == request.Context.Target.SemanticId &&
            item.Kind == request.Context.Target.Kind && item.VisualStateId == request.Context.Target.VisualStateId);
        if (target is null) return false;
        var part = request.Context.Part is { } expected
            ? target.Parts.SingleOrDefault(candidate => candidate.SceneObjectId == expected.SceneObjectId &&
                candidate.Kind == expected.Kind && candidate.SpatialRegionId == expected.SpatialRegionId)
            : null;
        if (request.Context.Part is not null && part is null) return false;
        if (part is null && !HasSingleApplicationPresentationInstance(target)) return false;
        context = new(current, target, part, request.Context.RequestVersion);
        return true;
    }

    private void ObserveApplicationActionsUnderLock(EditingSessionState state)
    {
        if (_applicationActionRequest is not { } request) return;
        var stamp = request.Context.Stamp;
        if (state.IsClosed || state.Status == EditingSessionStatus.RuntimeFaulted ||
            stamp.AttachmentVersion != _documentSessionVersion || stamp.DocumentId != state.DocumentId ||
            stamp.Revision != state.DocumentRevision || stamp.ContextVersion != _presentationContextVersion ||
            stamp.SelectionVersion != _presentationSelectionVersion ||
            state.Status == EditingSessionStatus.Ready && state.IsCurrentScenePresented &&
            !TryCaptureApplicationContextUnderLock(request, requireMenu: !request.Started, out _))
        {
            RetireApplicationActionsUnderLock();
            if (_contextMenu?.ApplicationContext is not null) _contextMenu = null;
        }
    }

    private async Task FailApplicationRequestAsync(ApplicationActionRequest request, string code)
    {
        try { await EnterModelerOperationAsync(request.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (request.Token.IsCancellationRequested) { return; }
        try
        {
            lock (_sync)
            {
                if (!TryCaptureApplicationContextUnderLock(request, requireMenu: true, out _)) return;
                ReportApplicationFailureUnderLock(code);
                _contextMenu = _contextMenu! with { ApplicationActions = [], IsApplicationDiscoveryPending = false };
                request.InvocationPending = false;
                if (!HasNativeApplicationMenuActions(_contextMenu)) CloseContextMenuUnderLock();
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
        var request = _applicationActionRequest;
        _applicationActionRequest = null;
        if (request is not null) CancelApplicationRequest(request);
    }

    private static bool HasNativeApplicationMenuActions(DocumentCanvasContextMenuState menu) =>
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
        internal ApplicationActionRequest(BpmnApplicationActionContext context, BpmnApplicationActionProvider provider)
        {
            Context = context;
            Provider = provider;
            Cancellation = new();
            Token = Cancellation.Token;
        }
        internal BpmnApplicationActionContext Context { get; }
        internal BpmnApplicationActionProvider Provider { get; }
        internal CancellationTokenSource Cancellation { get; }
        internal CancellationToken Token { get; }
        internal int CancellationStarted;
        internal bool InvocationPending { get; set; }
        internal bool Started { get; set; }
    }

    private sealed class InvalidApplicationActionsException : Exception { }
}

internal sealed class BpmnApplicationActionDispatch(DocumentCanvasHost host, long version, string actionId)
{
    internal bool TryBeginInvocation(out BpmnApplicationActionInvocationEventArgs? args) =>
        host.TryBeginApplicationActionInvocation(version, actionId, out args);
    internal void Complete() => host.CompleteApplicationActionInvocation(version, failed: false);
    internal void Fail(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        host.CompleteApplicationActionInvocation(version, failed: true);
    }
}
