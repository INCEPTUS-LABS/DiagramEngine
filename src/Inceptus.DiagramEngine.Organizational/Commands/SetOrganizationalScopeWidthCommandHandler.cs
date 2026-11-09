using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Organizational.Profiles;

namespace Inceptus.DiagramEngine.Organizational.Commands;

internal sealed class SetOrganizationalScopeWidthCommandHandler : ICommandHandler
{
    public ValueTask<CommandHandlerResult> HandleAsync(ICommand command, DocumentSnapshot document,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (command is not SetOrganizationalScopeWidthCommand width || Find(document, width) is null)
            return ValueTask.FromResult(CommandHandlerResult.Failure([new Diagnostic(
                "ORGANIZATIONAL_SCOPE_WIDTH_INVALID", DiagnosticSeverity.Error,
                "Width requires a current saved Organizational stack in the requested scope.", command.TypeId.Value)]));
        return ValueTask.FromResult(CommandHandlerResult.SuccessWithPreparation(document, [], [],
            [new SpatialScopeWidthIntent(width.ScopeId, OrganizationalModelProfile.Id, width.OuterWidth)],
            pipelineInvalidation: CommandPipelineInvalidation.WithoutNodeLayout,
            nodeGeometryImpact: NodeGeometryPipelineImpact.PreserveAll));
    }

    internal static SpatialScopeWidthSnapshot? Find(DocumentSnapshot document, SetOrganizationalScopeWidthCommand command) =>
        document.VisualModel.RoutingScopes?.FirstOrDefault(scope => scope.ScopeId == command.ScopeId)?
            .Geometry.SpatialWidths.FirstOrDefault(width => width.ProfileId == OrganizationalModelProfile.Id);
}
