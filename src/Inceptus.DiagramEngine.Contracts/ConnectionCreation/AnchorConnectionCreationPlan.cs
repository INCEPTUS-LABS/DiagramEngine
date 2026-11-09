using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ConnectionCreation;

/// <summary>
/// Describes one immutable connection-creation Command and its created identities.
/// </summary>
public sealed class AnchorConnectionCreationPlan
{
    public AnchorConnectionCreationPlan(
        ICommand command,
        SemanticElementId createdSemanticRelationshipId,
        VisualStateId createdConnectorVisualStateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(createdSemanticRelationshipId);
        ArgumentNullException.ThrowIfNull(createdConnectorVisualStateId);

        Command = command;
        CreatedSemanticRelationshipId = createdSemanticRelationshipId;
        CreatedConnectorVisualStateId = createdConnectorVisualStateId;
    }

    public ICommand Command { get; }

    public SemanticElementId CreatedSemanticRelationshipId { get; }

    public VisualStateId CreatedConnectorVisualStateId { get; }
}
