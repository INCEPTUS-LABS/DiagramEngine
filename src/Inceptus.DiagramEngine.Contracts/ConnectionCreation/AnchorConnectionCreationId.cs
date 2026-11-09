using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.ConnectionCreation;

/// <summary>
/// Identifies one notation-owned anchor-connection creation capability.
/// </summary>
public sealed record AnchorConnectionCreationId
{
    public AnchorConnectionCreationId(string value) =>
        Value = IdentifierValue.Validate(value);

    public string Value { get; }

    public override string ToString() => Value;
}
