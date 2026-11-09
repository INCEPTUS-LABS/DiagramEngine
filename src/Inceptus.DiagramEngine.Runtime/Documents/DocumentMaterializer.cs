using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.Runtime.Documents;

internal static class DocumentMaterializer
{
    public static DocumentConstructionResult Materialize(
        DocumentSnapshot candidate,
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null)
    {
        ArgumentNullException.ThrowIfNull(candidate);

        var diagnostics = DocumentInvariantValidator.Validate(
            candidate,
            connectorAnchorPolicyProvider);

        if (!diagnostics.IsEmpty)
        {
            return DocumentConstructionResult.Failure(diagnostics);
        }

        var ownedSnapshot = DocumentSnapshotCloner.Clone(candidate);
        return DocumentConstructionResult.Success(new Document(ownedSnapshot));
    }
}
