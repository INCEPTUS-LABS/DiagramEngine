using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Profiles;

/// <summary>
/// Geometry-free persistent presentation state for a profile semantic element.
/// Derived bounds and transient view preferences are deliberately excluded.
/// </summary>
public sealed record ModelProfileElementPresentationSnapshot
{
    public ModelProfileElementPresentationSnapshot(
        ModelProfileId profileId,
        SemanticElementId semanticElementId,
        int order)
    {
        ArgumentNullException.ThrowIfNull(profileId);
        ArgumentNullException.ThrowIfNull(semanticElementId);
        ArgumentOutOfRangeException.ThrowIfNegative(order);
        ProfileId = profileId;
        SemanticElementId = semanticElementId;
        Order = order;
    }

    public ModelProfileId ProfileId { get; }

    public SemanticElementId SemanticElementId { get; }

    public int Order { get; }
}
