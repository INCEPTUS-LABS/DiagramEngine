using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.ContextMenus;

namespace Inceptus.DiagramEngine.Bpmn.ContextMenus;

public static class BpmnCanvasBackgroundActions
{
    public static CanvasBackgroundActionId AddProcessId { get; } =
        new("bpmn:background-action/add-process");

    public static CanvasBackgroundActionDefinition AddProcess { get; } =
        new(
            AddProcessId,
            groupLabel: "Add",
            displayName: "Process",
            request =>
            {
                var scopeId = request.IdentityProvider.CreateDocumentScopeId();
                return new CanvasBackgroundActionPlan(
                    new CreateTopLevelDocumentScopeCommand(
                        request.Document.DocumentId,
                        request.Document.Revision,
                        scopeId),
                    scopeId);
            },
            order: 100);

    public static ImmutableArray<CanvasBackgroundActionDefinition> Definitions { get; } =
        [AddProcess];
}
