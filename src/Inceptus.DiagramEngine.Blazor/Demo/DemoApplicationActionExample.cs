using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Application-owned policy and inspector; the modeler supplies only the action shell and exact target.</summary>
public sealed partial class DemoApplicationActionExample : IDisposable
{
    private static readonly BpmnApplicationActionIcon InspectIcon = new(
        "M 3 2 L 17 2 L 21 6 L 21 22 L 3 22 Z M 7 8 L 17 8 L 17 10 L 7 10 Z", new RectD(0, 0, 24, 24));
    private readonly Func<InceptusBpmnModeler?> _modeler;

    public DemoApplicationActionExample(Func<InceptusBpmnModeler?> modeler, HttpClient? catalogClient = null,
        Func<long>? mountGeneration = null)
    {
        ArgumentNullException.ThrowIfNull(modeler);
        _modeler = modeler;
        _catalogClient = catalogClient;
        _catalogToken = _catalogLifetime.Token;
        _mountGeneration = mountGeneration ?? (() => 1);
        Provider = DiscoverAsync;
        TypedHandler = HandleAsync;
        LegacyHandler = HandleLegacyAsync;
    }

    public bool ToolbarEnabled { get; set; }
    public bool UseTypedHandler { get; set; }
    public bool InspectAvailable { get; private set; } = true;
    public bool AdditionalActions { get; private set; }
    public long Revision { get; private set; }
    public int InvocationCount { get; private set; }
    public bool DialogOpen { get; private set; }
    public string Details { get; private set; } = "Select a target, then choose Inspect details from its toolbar or popup.";
    public string Feedback { get; private set; } = "Popup actions are independent of Smart toolbar activation.";
    public BpmnApplicationActionProvider Provider { get; }
    public BpmnApplicationActionHandler TypedHandler { get; }
    public Func<BpmnApplicationActionInvocationEventArgs, Task> LegacyHandler { get; }
    internal event Action? Changed;

    public void SetAvailability(bool available)
    {
        if (InspectAvailable == available) return;
        InspectAvailable = available;
        AdvanceRevision();
    }

    public void SetAdditionalActions(bool additional)
    {
        if (AdditionalActions == additional) return;
        AdditionalActions = additional;
        AdvanceRevision();
    }

    public async Task ReactivateAsync()
    {
        if (Revision < 0)
        {
            Feedback = "Restore a nonnegative application revision before requesting contextual actions.";
            return;
        }
        if (_modeler() is not { } modeler || modeler.CapturePresentation().Stamp is not { } stamp)
        {
            Feedback = "A coherent modeler presentation is unavailable.";
            return;
        }
        var result = await modeler.RequestApplicationContextualActionsAsync(stamp, Revision);
        Feedback = $"Contextual actions: {result.Status}. " + string.Join("; ", result.Diagnostics.Select(item => item.Code));
    }

    public void CloseDialog()
    {
        DialogOpen = false;
        Changed?.Invoke();
    }

    private ValueTask<IEnumerable<BpmnApplicationAction>> DiscoverAsync(BpmnApplicationActionContext context, CancellationToken token)
    {
        if (UseAsyncCatalog) return new(DiscoverCatalogAsync(context, token));
        token.ThrowIfCancellationRequested();
        if (VerificationEnabled && Registration == DemoApplicationRegistration.EmptyActions)
            return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>([]);
        return ValueTask.FromResult<IEnumerable<BpmnApplicationAction>>(CreateActions(context));
    }

    private List<BpmnApplicationAction> CreateActions(BpmnApplicationActionContext context, DemoApplicationCatalog? catalog = null)
    {
        var available = InspectAvailable && context.ApplicationActionsRevision == Revision;
        available &= catalog?.InspectEnabled ?? true;
        var labels = GetLabels(catalog?.InspectLabel);
        var actions = new List<BpmnApplicationAction>
        {
            new("example:inspect", labels.Inspect, available, available ? null : labels.Unavailable, InspectIcon),
            new("example:linked-record", labels.LinkedRecord, false, labels.NotConnected),
        };
        if (AdditionalActions)
        {
            actions.Add(new("example:owner", labels.Owner, available));
            actions.Add(new("example:scope", labels.Scope, available));
            actions.Add(new("example:source", labels.Source, available));
            actions.Add(new("example:visual", labels.Visual, available));
        }
        return actions;
    }

    private ValueTask<BpmnApplicationActionResult> HandleAsync(BpmnApplicationActionInvocationEventArgs args) =>
        UseAsyncCatalog && Outcome == DemoApplicationOutcome.OpenInspector
            ? new(HandleCatalogAsync(args, typed: true)) : HandleAttemptAsync(args, typed: true);

    private ValueTask<BpmnApplicationActionResult> HandleAttemptAsync(BpmnApplicationActionInvocationEventArgs args, bool typed)
    {
        if (args.CancellationToken.IsCancellationRequested)
            return ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Cancelled));
        if (!InspectAvailable || args.Context.ApplicationActionsRevision != Revision ||
            _modeler()?.CapturePresentation().Stamp != args.Context.Stamp ||
            args.ActionId is not ("example:inspect" or "example:owner" or "example:scope" or "example:source" or "example:visual"))
            return ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Unavailable));
        InvocationCount++;
        if (typed && VerificationEnabled && Outcome != DemoApplicationOutcome.OpenInspector)
            return CompleteVerificationAttempt();
        OpenInspector(args, typed);
        // Completion means the application opened its inspector, not that application data was saved.
        return ValueTask.FromResult(new BpmnApplicationActionResult(BpmnModelerOperationStatus.Succeeded));
    }

    private void OpenInspector(BpmnApplicationActionInvocationEventArgs args, bool typed, string? description = null)
    {
        ConsumerOutcome = typed ? "Application returned Succeeded after opening its inspector." :
            "Legacy callback completed after opening its inspector; it has no typed result.";
        var context = args.Context;
        Details = (description is null ? string.Empty : description + "\n") +
            $"{args.ActionId} · {context.Target.Kind}: {context.Target.SemanticId.Value} · " +
            $"visual: {context.Target.VisualStateId?.Value ?? "semantic-only"} · actual part: {context.Part?.Kind.ToString() ?? "unknown"} · " +
            $"document: {context.Stamp.DocumentId.Value}, revision: {context.Stamp.Revision.Value} · " +
            $"attachment: {context.Stamp.AttachmentVersion} · instance: {context.Stamp.InstanceId} · " +
            $"view: {context.Stamp.PresentationContext} · application revision: {context.ApplicationActionsRevision}";
        DialogOpen = true;
        Changed?.Invoke();
    }

    private Task<BpmnApplicationActionResult> HandleLegacyAsync(BpmnApplicationActionInvocationEventArgs args) =>
        UseAsyncCatalog ? HandleCatalogAsync(args, typed: false) : HandleAttemptAsync(args, typed: false).AsTask();
}
