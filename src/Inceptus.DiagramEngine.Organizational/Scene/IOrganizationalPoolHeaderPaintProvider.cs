using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Organizational.Scene;

/// <summary>Optional presentation policy for the existing Pool header primitives.</summary>
public interface IOrganizationalPoolHeaderPaintProvider
{
    OrganizationalPoolHeaderPaint Resolve(DocumentSnapshot document, SemanticElementSnapshot pool);
}

public sealed record OrganizationalPoolHeaderPaint(
    string Background, string Border, string Text, string SelectedBackground)
{
    public static OrganizationalPoolHeaderPaint Default { get; } =
        new("#eef2f7", "#64748b", "#0f172a", "#dbeafe");
}
