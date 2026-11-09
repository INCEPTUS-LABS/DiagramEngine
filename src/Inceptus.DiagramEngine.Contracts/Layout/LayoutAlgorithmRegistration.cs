using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Layout;

/// <summary>
/// Associates one stable algorithm identity with a Layout Algorithm implementation.
/// </summary>
public sealed class LayoutAlgorithmRegistration
{
    public LayoutAlgorithmRegistration(
        AlgorithmId algorithmId,
        ILayoutAlgorithm algorithm)
    {
        ArgumentNullException.ThrowIfNull(algorithmId);
        ArgumentNullException.ThrowIfNull(algorithm);

        AlgorithmId = algorithmId;
        Algorithm = algorithm;
    }

    public AlgorithmId AlgorithmId { get; }

    public ILayoutAlgorithm Algorithm { get; }
}
