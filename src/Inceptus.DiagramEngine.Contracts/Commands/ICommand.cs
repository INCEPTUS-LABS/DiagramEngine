using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Contracts.Commands;

public interface ICommand
{
    CommandTypeId TypeId { get; }

    DocumentId TargetDocumentId { get; }

    DocumentRevision ExpectedRevision { get; }

    CommandCategory Category { get; }

    AuthoritativeDocumentComponent AffectedComponents { get; }
}
