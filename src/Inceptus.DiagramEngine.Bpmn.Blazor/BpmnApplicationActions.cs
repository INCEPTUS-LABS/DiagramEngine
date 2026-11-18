namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>An application-owned action. It is never converted into a native editing command.</summary>
public sealed class BpmnApplicationAction
{
    public BpmnApplicationAction(string id, string label, bool isEnabled = true, string? disabledExplanation = null)
        : this(id, label, isEnabled, disabledExplanation, null)
    {
    }

    public BpmnApplicationAction(string id, string label, bool isEnabled, string? disabledExplanation,
        BpmnApplicationActionIcon? icon)
    {
        Id = id;
        Label = label;
        IsEnabled = isEnabled;
        DisabledExplanation = disabledExplanation;
        Icon = icon;
    }

    public string Id { get; }
    public string Label { get; }
    public bool IsEnabled { get; }
    public string? DisabledExplanation { get; }
    public BpmnApplicationActionIcon? Icon { get; }
}

/// <summary>
/// Immutable semantic context shared by pointer and keyboard actions. Part is the actual hit
/// when known; it is not an invented semantic identity or a required pointer location.
/// Part bounds use the existing final Scene document coordinates. Target identifies this
/// request's exact owner; a popup target need not belong to Presentation.Selection.
/// </summary>
public sealed class BpmnApplicationActionContext
{
    internal BpmnApplicationActionContext(BpmnPresentationSnapshot presentation,
        BpmnSemanticSelectionTarget target, BpmnPresentationPart? part, long requestVersion)
        : this(presentation, target, part, requestVersion, 0)
    {
    }

    internal BpmnApplicationActionContext(BpmnPresentationSnapshot presentation,
        BpmnSemanticSelectionTarget target, BpmnPresentationPart? part, long requestVersion,
        long applicationActionsRevision)
    {
        Presentation = presentation;
        Target = target;
        Part = part;
        RequestVersion = requestVersion;
        ApplicationActionsRevision = applicationActionsRevision;
    }

    public BpmnPresentationSnapshot Presentation { get; }
    public BpmnPresentationContextStamp Stamp => Presentation.Stamp!;
    public BpmnSemanticSelectionTarget Target { get; }
    public BpmnPresentationPart? Part { get; }
    public long RequestVersion { get; }
    public long ApplicationActionsRevision { get; }
}

/// <summary>
/// Discovers application actions asynchronously. The modeler copies and validates the returned
/// sequence. Cancellation retires this request; application code should cooperate with it.
/// </summary>
public delegate ValueTask<IEnumerable<BpmnApplicationAction>> BpmnApplicationActionProvider(
    BpmnApplicationActionContext context, CancellationToken cancellationToken);

/// <summary>
/// Handles one revalidated application action attempt. Success completes this attempt,
/// such as opening an application editor; it does not certify a later configuration save.
/// </summary>
public delegate ValueTask<BpmnApplicationActionResult> BpmnApplicationActionHandler(
    BpmnApplicationActionInvocationEventArgs invocation);

/// <summary>
/// A revalidated invocation. Application side effects belong to the consumer and cannot be
/// reversed by DiagramEngine after the callback starts. Later modeler calls still need fresh stamps.
/// </summary>
public sealed class BpmnApplicationActionInvocationEventArgs : EventArgs
{
    internal BpmnApplicationActionInvocationEventArgs(string actionId,
        BpmnApplicationActionContext context, CancellationToken cancellationToken)
    {
        ActionId = actionId;
        Context = context;
        CancellationToken = cancellationToken;
    }

    public string ActionId { get; }
    public BpmnApplicationActionContext Context { get; }
    public CancellationToken CancellationToken { get; }
}
