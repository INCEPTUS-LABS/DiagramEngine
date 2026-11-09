using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Toolbox;

/// <summary>
/// Describes one immutable creation Command and the persistent identities it creates.
/// </summary>
public sealed class ToolboxPlacementPlan
{
    public ToolboxPlacementPlan(
        ICommand command,
        SemanticElementId createdSemanticElementId,
        VisualStateId createdVisualStateId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(createdSemanticElementId);
        ArgumentNullException.ThrowIfNull(createdVisualStateId);

        Command = command;
        CreatedSemanticElementId = createdSemanticElementId;
        CreatedVisualStateId = createdVisualStateId;
    }

    public ICommand Command { get; }

    public SemanticElementId CreatedSemanticElementId { get; }

    public VisualStateId CreatedVisualStateId { get; }
}
