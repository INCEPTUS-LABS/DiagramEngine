using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ContextMenus;

/// <summary>
/// Describes one transient collapsed-state update. It is not a persistent Document command.
/// </summary>
public sealed record SemanticSceneViewActionPlan
{
    public SemanticSceneViewActionPlan(
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        bool isCollapsed)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ProfileId = profileId;
        SemanticElementId = semanticElementId;
        IsCollapsed = isCollapsed;
    }

    public ModelProfileId ProfileId { get; }

    public SemanticElementId SemanticElementId { get; }

    public bool IsCollapsed { get; }
}
