namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>An application-owned action. It is never converted into a native editing command.</summary>
public sealed class BpmnApplicationAction
{
    public BpmnApplicationAction(string id, string label, bool isEnabled = true, string? disabledExplanation = null)
    {
        Id = id;
        Label = label;
        IsEnabled = isEnabled;
        DisabledExplanation = disabledExplanation;
    }

    public string Id { get; }
    public string Label { get; }
    public bool IsEnabled { get; }
    public string? DisabledExplanation { get; }
}

/// <summary>
/// Immutable semantic context shared by pointer and keyboard actions. Part is the actual hit
/// when known; it is not an invented semantic identity or a required pointer location.
/// Part bounds use the existing final Scene document coordinates.
/// </summary>
public sealed class BpmnApplicationActionContext
{
    internal BpmnApplicationActionContext(BpmnPresentationSnapshot presentation,
        BpmnSemanticSelectionTarget target, BpmnPresentationPart? part, long requestVersion)
    {
        Presentation = presentation;
        Target = target;
        Part = part;
        RequestVersion = requestVersion;
    }

    public BpmnPresentationSnapshot Presentation { get; }
    public BpmnPresentationContextStamp Stamp => Presentation.Stamp!;
    public BpmnSemanticSelectionTarget Target { get; }
    public BpmnPresentationPart? Part { get; }
    public long RequestVersion { get; }
}

/// <summary>
/// Discovers application actions asynchronously. The modeler copies and validates the returned
/// sequence. Cancellation retires this request; application code should cooperate with it.
/// </summary>
public delegate ValueTask<IEnumerable<BpmnApplicationAction>> BpmnApplicationActionProvider(
    BpmnApplicationActionContext context, CancellationToken cancellationToken);

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
