using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class BpmnModelerFacade : IBpmnDiagramPresentation
{
    public BpmnPresentationSnapshot CapturePresentation() =>
        CaptureHost()?.CapturePresentation() ?? BpmnPresentationSnapshot.Unavailable;

    public ValueTask<BpmnPresentationOperationResult> SelectVisualAsync(BpmnPresentationContextStamp context,
        VisualStateId visualStateId, CancellationToken cancellationToken = default) =>
        CaptureHost()?.SelectVisualAsync(context, visualStateId, cancellationToken) ?? PresentationUnavailable(cancellationToken);

    public ValueTask<BpmnPresentationOperationResult> NavigateToScopeAsync(BpmnPresentationContextStamp context,
        DocumentScopeId scopeId, CancellationToken cancellationToken = default) =>
        CaptureHost()?.NavigateToScopeAsync(context, scopeId, cancellationToken) ?? PresentationUnavailable(cancellationToken);

    private static ValueTask<BpmnPresentationOperationResult> PresentationUnavailable(CancellationToken token) =>
        ValueTask.FromResult(new BpmnPresentationOperationResult(token.IsCancellationRequested
            ? BpmnModelerOperationStatus.Cancelled : BpmnModelerOperationStatus.Unavailable,
            BpmnPresentationSnapshot.Unavailable, UnavailableDiagnostics()));
}
