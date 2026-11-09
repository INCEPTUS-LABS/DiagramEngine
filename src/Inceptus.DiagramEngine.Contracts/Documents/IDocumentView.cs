using Inceptus.DiagramEngine.Contracts.Metadata;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Semantics;
using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.Contracts.Documents;

public interface IDocumentView
{
    DocumentId DocumentId { get; }

    DocumentRevision Revision { get; }

    ISemanticModelView SemanticModel { get; }

    IVisualModelView VisualModel { get; }

    IDocumentMetadataView Metadata { get; }

    DocumentPublicationSnapshot? Publication { get; }
}
