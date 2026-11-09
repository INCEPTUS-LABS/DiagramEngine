using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Runtime.Documents;

internal sealed class DocumentState
{
    public DocumentState(DocumentSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Snapshot = snapshot;
    }

    public DocumentSnapshot Snapshot { get; }
}
