using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed partial class DocumentCanvasHost
{
    private static ImmutableArray<PointD> ResolveEditableConnectorRoute(DocumentSnapshot document, VisualStateSnapshot visual)
    {
        if (document.VisualModel.RoutingScopes is not { } scopes) return visual.Route;
        var record = scopes.SelectMany(static scope => scope.Connectors)
            .Concat(document.VisualModel.DocumentRoutingStates.SelectMany(static view => view.Connectors))
            .FirstOrDefault(candidate => candidate.VisualStateId == visual.Id);
        return record?.RoutingType == ConnectorRoutingType.Manual ? record.Path : [];
    }
}
