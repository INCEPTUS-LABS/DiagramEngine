using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Organizational.Semantics;

public static class OrganizationalSemanticFactory
{
    public static SemanticElementSnapshot CreatePool(
        SemanticElementId poolId,
        string name = "Pool",
        string description = "")
    {
        ArgumentNullException.ThrowIfNull(poolId);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(description);
        return new SemanticElementSnapshot(
            poolId,
            OrganizationalSemanticTypes.Pool,
            [
                new KeyValuePair<string, PropertyValue>(
                    OrganizationalSemanticProperties.Name,
                    PropertyValue.FromText(name)),
                new KeyValuePair<string, PropertyValue>(
                    OrganizationalSemanticProperties.Description,
                    PropertyValue.FromText(description)),
            ],
            containmentKind: SemanticElementContainmentKind.Scope);
    }
}
