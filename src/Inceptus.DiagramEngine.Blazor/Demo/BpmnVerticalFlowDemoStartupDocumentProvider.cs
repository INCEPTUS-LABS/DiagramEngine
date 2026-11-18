using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Semantics;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Authors a vertical Sequence Flow between the existing Pool sample's aligned Prepare and Pack Tasks.</summary>
internal sealed class BpmnVerticalFlowDemoStartupDocumentProvider : IBpmnModelerStartupDocumentProvider
{
    internal static BpmnVerticalFlowDemoStartupDocumentProvider Instance { get; } = new();
    internal static SemanticElementId VerticalId { get; } = new("demo:pools:vertical-flow");
    internal static VisualStateId VerticalVisualId { get; } = new("demo:pools:visual:vertical-flow");

    internal static async ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(DocumentSnapshot sample,
        Canvas2DRenderer renderer, CancellationToken cancellationToken)
    {
        // Retain the Pool sample geometry before authoring the relationship across its two Lanes.
        var pool = (await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)).CaptureSnapshot();
        var preparer = DemoBpmnImportPreparation.Create(renderer);
        var seed = await preparer.PrepareAsync(new(pool, pool, [], [], null, false,
            ConnectorRoutingPreparationPurpose.InitialConstruction), cancellationToken);
        if (!seed.Succeeded) return seed;
        var seeded = new DocumentSnapshot(sample.SemanticModel,
            new VisualModelSnapshot(sample.DocumentId, sample.Revision, sample.VisualModel.VisualStates,
                sample.VisualModel.ProfileElementPresentations, seed.RoutingScopes, seed.DocumentRoutingStates),
            sample.Metadata, sample.Publication);
        return await preparer.PrepareAsync(new(seeded, seeded, [ConnectorRoutingIntent.Initialize(VerticalVisualId, ConnectorRoutingType.Straight)], [], null, false,
            ConnectorRoutingPreparationPurpose.RecoverSavedState), cancellationToken);
    }

    public async ValueTask<Document> GetInitialDocumentAsync(CancellationToken cancellationToken = default)
    {
        var source = (await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)).CaptureSnapshot();
        var prepare = new SemanticElementId("demo:pools:prepare");
        var pack = new SemanticElementId("demo:pools:pack");
        var sourceAnchor = new ConnectorAnchorId("demo:pools:prepare:vertical-source");
        var targetAnchor = new ConnectorAnchorId("demo:pools:pack:vertical-target");
        var semantic = source.SemanticModel;
        var model = new SemanticModelSnapshot(source.DocumentId, source.Revision, semantic.Elements,
            semantic.Relationships.Append(BpmnSemanticFactory.CreateSequenceFlow(VerticalId, prepare, pack, "Vertical flow")),
            semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments);
        var visuals = source.VisualModel.VisualStates.Select(visual =>
            visual.SemanticElementId != prepare && visual.SemanticElementId != pack ? visual :
            new VisualStateSnapshot(visual.Id, visual.SemanticElementId, visual.Position, visual.Size, visual.PlacementMode,
                visual.Route, visual.Properties,
                [visual.SemanticElementId == prepare
                    ? new(sourceAnchor, ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0)
                    : new(targetAnchor, ConnectorAnchorSide.Top, ConnectorAnchorRole.Target, 0)]))
            .Append(new(VerticalVisualId, VerticalId, default, default, VisualPlacementMode.Automatic,
                sourceAnchorId: sourceAnchor, targetAnchorId: targetAnchor));
        var result = DocumentFactory.Create(new DocumentSnapshot(model,
            new VisualModelSnapshot(source.DocumentId, source.Revision, visuals, source.VisualModel.ProfileElementPresentations),
            source.Metadata, source.Publication),
            new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        return result.Document ?? throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    }
}
