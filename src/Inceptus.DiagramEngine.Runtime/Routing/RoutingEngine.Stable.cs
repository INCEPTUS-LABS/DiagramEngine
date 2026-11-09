using System.Diagnostics.CodeAnalysis;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Routing;

namespace Inceptus.DiagramEngine.Runtime.Routing;

public sealed partial class RoutingEngine
{
    /// <summary>Resolves the optional stable-path policy on this engine's registered algorithm.</summary>
    public bool TryGetStablePolicy(AlgorithmId algorithmId,
        [NotNullWhen(true)] out IStableConnectorRoutingPolicy? policy)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        policy = _registry.TryGet(algorithmId, out var registration)
            ? registration!.Algorithm as IStableConnectorRoutingPolicy : null;
        return policy is not null;
    }
}
