using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.EndpointReconnection;

/// <summary>
/// Describes one immutable endpoint-reconnection Command and the existing identities it edits.
/// </summary>
public sealed class ConnectorEndpointReconnectionPlan
{
    public ConnectorEndpointReconnectionPlan(
        ICommand command,
        SemanticElementId relationshipId,
        VisualStateId connectorVisualStateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(relationshipId);
        ArgumentNullException.ThrowIfNull(connectorVisualStateId);

        Command = command;
        RelationshipId = relationshipId;
        ConnectorVisualStateId = connectorVisualStateId;
    }

    public ICommand Command { get; }

    public SemanticElementId RelationshipId { get; }

    public VisualStateId ConnectorVisualStateId { get; }
}
