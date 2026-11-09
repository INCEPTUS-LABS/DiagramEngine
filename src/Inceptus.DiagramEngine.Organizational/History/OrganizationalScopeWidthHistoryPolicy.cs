using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Organizational.Commands;
using Inceptus.DiagramEngine.Organizational.Profiles;

namespace Inceptus.DiagramEngine.Organizational.History;

internal sealed class OrganizationalScopeWidthHistoryPolicy : ICommandHistoryPolicy
{
    public CommandHistoryPreparationResult Prepare(ICommand command, DocumentSnapshot before, DocumentSnapshot committed)
    {
        if (command is not SetOrganizationalScopeWidthCommand width ||
            SetOrganizationalScopeWidthCommandHandler.Find(before, width) is not { } previous ||
            SetOrganizationalScopeWidthCommandHandler.Find(committed, width) is not { } current)
            return CommandHistoryPreparationResult.Failure([new Diagnostic("ORGANIZATIONAL_SCOPE_WIDTH_HISTORY_INVALID",
                DiagnosticSeverity.Error, "Width History requires the same existing scope before and after commit.")]);
        return previous.OuterWidth == current.OuterWidth
            ? CommandHistoryPreparationResult.PreserveExistingHistory()
            : CommandHistoryPreparationResult.Undoable(
                new Factory(width.ScopeId, previous.OuterWidth), new Factory(width.ScopeId, current.OuterWidth), [], [],
                [new SpatialScopeWidthHistoryDelta(width.ScopeId, OrganizationalModelProfile.Id, previous.OuterWidth, current.OuterWidth)]);
    }

    private sealed class Factory(DocumentScopeId scopeId, double width) : IHistoryCommandFactory
    {
        public ICommand Create(DocumentId documentId, DocumentRevision expectedRevision) =>
            new SetOrganizationalScopeWidthCommand(documentId, expectedRevision, scopeId, width);
    }
}
