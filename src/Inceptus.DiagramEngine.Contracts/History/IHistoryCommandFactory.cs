using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.History;

/// <summary>
/// Holds immutable restoration data and constructs a Command for the current revision.
/// </summary>
public interface IHistoryCommandFactory
{
    ICommand Create(DocumentId documentId, DocumentRevision expectedRevision);
}
