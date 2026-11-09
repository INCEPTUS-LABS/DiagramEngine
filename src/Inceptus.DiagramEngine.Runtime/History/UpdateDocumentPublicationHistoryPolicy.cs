using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Runtime.History;

internal sealed class UpdateDocumentPublicationHistoryPolicy : ICommandHistoryPolicy
{
    internal static CommandHistoryPolicyRegistration Registration { get; } =
        new(
            UpdateDocumentPublicationCommand.KnownTypeId,
            new UpdateDocumentPublicationHistoryPolicy());

    public CommandHistoryPreparationResult Prepare(
        ICommand command,
        DocumentSnapshot before,
        DocumentSnapshot committed)
    {
        if (command is not UpdateDocumentPublicationCommand update ||
            !update.TryCreatePublication(out var requested) ||
            !Equals(requested, committed.Publication) ||
            Equals(before.Publication, committed.Publication))
        {
            return CommandHistoryPreparationResult.Failure(
            [
                new Diagnostic(
                    HistoryDiagnosticCodes.InvalidPreparation,
                    DiagnosticSeverity.Error,
                    "Publication History requires one exact complete Publication replacement.",
                    command.TypeId.Value),
            ]);
        }

        return CommandHistoryPreparationResult.Undoable(
            new Factory(before.Publication),
            new Factory(committed.Publication));
    }

    private sealed class Factory(DocumentPublicationSnapshot? publication) :
        IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new UpdateDocumentPublicationCommand(
                documentId,
                expectedRevision,
                publication);
    }
}
