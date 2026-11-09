using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ContextMenus;

/// <summary>
/// Contains the one canonical persistent command for an exact semantic Scene target.
/// </summary>
public sealed record SemanticSceneCommandActionPlan
{
    public SemanticSceneCommandActionPlan(
        ICommand command,
        SemanticElementId semanticElementId)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        Command = command;
        SemanticElementId = semanticElementId;
    }

    public ICommand Command { get; }

    public SemanticElementId SemanticElementId { get; }
}
