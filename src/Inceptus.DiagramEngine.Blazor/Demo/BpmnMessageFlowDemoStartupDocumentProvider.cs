using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Semantics;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Authors one named Message Flow between the existing Pool sample's Send and Receive Tasks.</summary>
internal sealed class BpmnMessageFlowDemoStartupDocumentProvider : IBpmnModelerStartupDocumentProvider
{
    internal static BpmnMessageFlowDemoStartupDocumentProvider Instance { get; } = new();
    internal static SemanticElementId MessageId { get; } = new("demo:pools:order-message");
    internal static VisualStateId MessageVisualId { get; } = new("demo:pools:visual:order-message");

    internal static async ValueTask<ConnectorRoutingStatePreparationResult> PrepareAsync(DocumentSnapshot sample,
        Canvas2DRenderer renderer, CancellationToken cancellationToken)
    {
        // Author the existing Pool geometry before adding the cross-participant relationship.
        // Message Flow is routed against that saved geometry, not fed to Sequence Flow auto-layout.
        var pool = (await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)).CaptureSnapshot();
        var preparer = DemoBpmnImportPreparation.Create(renderer);
        var seed = await preparer.PrepareAsync(new(pool, pool, [], [], null, false,
            ConnectorRoutingPreparationPurpose.InitialConstruction), cancellationToken);
        if (!seed.Succeeded) return seed;
        var seeded = new DocumentSnapshot(sample.SemanticModel,
            new VisualModelSnapshot(sample.DocumentId, sample.Revision, sample.VisualModel.VisualStates,
                sample.VisualModel.ProfileElementPresentations, seed.RoutingScopes, seed.DocumentRoutingStates),
            sample.Metadata, sample.Publication);
        return await preparer.PrepareAsync(new(seeded, seeded, [], [], null, false,
            ConnectorRoutingPreparationPurpose.RecoverSavedState), cancellationToken);
    }

    public async ValueTask<Document> GetInitialDocumentAsync(CancellationToken cancellationToken = default)
    {
        var source = (await BpmnPoolDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)).CaptureSnapshot();
        var send = new SemanticElementId("demo:pools:send");
        var receive = new SemanticElementId("demo:pools:receive");
        var sourceAnchor = new ConnectorAnchorId("demo:pools:send:message-source");
        var targetAnchor = new ConnectorAnchorId("demo:pools:receive:message-target");
        var semantic = source.SemanticModel;
        var model = new SemanticModelSnapshot(source.DocumentId, source.Revision, semantic.Elements,
            semantic.Relationships.Append(BpmnSemanticFactory.CreateMessageFlow(MessageId, send, receive, "Order message")),
            semantic.NestedScopes, semantic.ScopeMemberships, semantic.ModelProfiles, semantic.ProfileAssignments);
        var visuals = source.VisualModel.VisualStates.Select(visual =>
            visual.SemanticElementId != send && visual.SemanticElementId != receive ? visual :
            new VisualStateSnapshot(visual.Id, visual.SemanticElementId, visual.Position, visual.Size, visual.PlacementMode,
                visual.Route, visual.Properties,
                [visual.SemanticElementId == send
                    ? new(sourceAnchor, ConnectorAnchorSide.Bottom, ConnectorAnchorRole.Source, 0)
                    : new(targetAnchor, ConnectorAnchorSide.Top, ConnectorAnchorRole.Target, 0)]))
            .Append(new(MessageVisualId, MessageId, default, default, VisualPlacementMode.Automatic,
                sourceAnchorId: sourceAnchor, targetAnchorId: targetAnchor));
        var result = DocumentFactory.Create(new DocumentSnapshot(model,
            new VisualModelSnapshot(source.DocumentId, source.Revision, visuals, source.VisualModel.ProfileElementPresentations),
            source.Metadata, source.Publication),
            new ElementConnectorAnchorPolicyRegistry(BpmnPluginRegistration.N100.ConnectorAnchorPolicies));
        return result.Document ?? throw new InvalidOperationException(string.Join("; ", result.Diagnostics.Select(item => item.Message)));
    }
}
