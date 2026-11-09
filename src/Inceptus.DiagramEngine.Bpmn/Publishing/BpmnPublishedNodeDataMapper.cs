using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Publishing;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Bpmn.Publishing;

/// <summary>
/// Resolves BPMN semantic properties into notation-neutral published node data.
/// </summary>
public sealed class BpmnPublishedNodeDataMapper : IPublishedNodeDataMapper
{
    public PublishedNodeData Map(SemanticElementSnapshot semanticElement)
    {
        ArgumentNullException.ThrowIfNull(semanticElement);

        return semanticElement.Properties.TryGetValue(
                BpmnSemanticProperties.Description,
                out var description) &&
            description.Kind == PropertyValueKind.Text
                ? new PublishedNodeData(description.TextValue)
                : PublishedNodeData.Empty;
    }
}
