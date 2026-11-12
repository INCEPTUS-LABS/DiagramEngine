using Inceptus.DiagramEngine.Contracts.Primitives;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Components;

public partial class InceptusBpmnModeler : IBpmnDiagramPresentation
{
    /// <summary>Reports logical selection and primary-part changes, excluding hover and viewport updates.</summary>
    [Parameter]
    public EventCallback<BpmnSelectionChangedEventArgs> SelectionChanged { get; set; }

    public BpmnPresentationSnapshot CapturePresentation() => Facade.CapturePresentation();

    public ValueTask<BpmnPresentationOperationResult> SelectVisualAsync(BpmnPresentationContextStamp context,
        VisualStateId visualStateId, CancellationToken cancellationToken = default) =>
        Facade.SelectVisualAsync(context, visualStateId, cancellationToken);

    public ValueTask<BpmnPresentationOperationResult> NavigateToScopeAsync(BpmnPresentationContextStamp context,
        DocumentScopeId scopeId, CancellationToken cancellationToken = default) =>
        Facade.NavigateToScopeAsync(context, scopeId, cancellationToken);
}
