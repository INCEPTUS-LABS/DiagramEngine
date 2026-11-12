using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>Immutable inspection and guarded navigation, independent of document editing.</summary>
public interface IBpmnDiagramPresentation
{
    BpmnPresentationSnapshot CapturePresentation();

    ValueTask<BpmnPresentationOperationResult> SelectVisualAsync(
        BpmnPresentationContextStamp context, VisualStateId visualStateId,
        CancellationToken cancellationToken = default);

    ValueTask<BpmnPresentationOperationResult> NavigateToScopeAsync(
        BpmnPresentationContextStamp context, DocumentScopeId scopeId,
        CancellationToken cancellationToken = default);
}
