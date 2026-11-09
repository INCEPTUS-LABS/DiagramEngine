using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.TestSupport;

internal static class BpmnModelerTestComposition
{
    // Cross-Pool routing regressions explicitly model Lanes of one Participant.
    internal static async Task AssignSharedParticipantAsync(
        Func<Inceptus.DiagramEngine.Contracts.Commands.ICommand, Task> execute,
        Func<DocumentSnapshot> capture,
        params Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId[] pools)
    {
        var collaboration = new Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId("test:lane-collaboration");
        var participant = new Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId("test:lane-participant");
        var snapshot = capture();
        if (!snapshot.SemanticModel.ModelProfiles.IsAvailable(Inceptus.DiagramEngine.Bpmn.Profiles.BpmnModelProfiles.OrganizationalId))
            await execute(new Inceptus.DiagramEngine.Contracts.Commands.SetModelProfileAvailabilityCommand(snapshot.DocumentId, snapshot.Revision,
                [new(Inceptus.DiagramEngine.Bpmn.Profiles.BpmnModelProfiles.OrganizationalId, true)]));
        snapshot = capture();
        await execute(new Inceptus.DiagramEngine.Bpmn.Commands.CreateBpmnCollaborationCommand(snapshot.DocumentId, snapshot.Revision, collaboration, "Test collaboration"));
        snapshot = capture();
        await execute(new Inceptus.DiagramEngine.Bpmn.Commands.CreateBpmnParticipantCommand(snapshot.DocumentId, snapshot.Revision, participant, collaboration, name: "Shared participant"));
        foreach (var pool in pools)
        {
            snapshot = capture();
            await execute(new Inceptus.DiagramEngine.Bpmn.Commands.SetBpmnPoolParticipantCommand(snapshot.DocumentId, snapshot.Revision, pool, participant));
        }
    }

    internal static IDocumentCanvasCompositionFactory DemoFactory { get; } =
        new FreshDemoTestCompositionFactory();

    internal static IDocumentCanvasCompositionFactory NeutralFactory { get; } =
        CreateNeutralFactory();

    internal static ValueTask<DocumentCanvasComposition> CreateDemoAsync(
        CancellationToken cancellationToken = default) =>
        DemoFactory.CreateAsync(cancellationToken);

    internal static async ValueTask<DocumentSnapshot> CreateFreshDemoSnapshotAsync(
        CancellationToken cancellationToken = default)
    {
        var saved = (await BpmnDemoStartupDocumentProvider.Instance.GetInitialDocumentAsync(cancellationToken)
            .ConfigureAwait(false)).CaptureSnapshot();
        // These runtime tests use artificial text metrics. Native compatibility tests
        // separately retain and validate the geometry prepared for their renderer.
        return new DocumentSnapshot(saved.SemanticModel,
            new VisualModelSnapshot(saved.DocumentId, saved.Revision, saved.VisualModel.VisualStates,
                saved.VisualModel.ProfileElementPresentations), saved.Metadata, saved.Publication);
    }

    internal static async ValueTask<BpmnModelerCompositionFactory> CreateFreshDemoFactoryAsync(
        CancellationToken cancellationToken = default) =>
        new(initialDocument: await CreateFreshDemoSnapshotAsync(cancellationToken).ConfigureAwait(false));

    internal static IDocumentCanvasCompositionFactory CreateNeutralFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider = null) =>
        new NeutralTestCompositionFactory(connectorAnchorPolicyProvider);

    private sealed class FreshDemoTestCompositionFactory : IDocumentCanvasCompositionFactory
    {
        public async ValueTask<DocumentCanvasComposition> CreateAsync(CancellationToken cancellationToken = default)
        {
            var factory = await CreateFreshDemoFactoryAsync(cancellationToken).ConfigureAwait(false);
            return await factory.CreateAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private sealed class NeutralTestCompositionFactory(
        IElementConnectorAnchorPolicyProvider? connectorAnchorPolicyProvider) :
        IDocumentCanvasCompositionFactory
    {
        public ValueTask<DocumentCanvasComposition> CreateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var neutral = NeutralDemoPipeline.CreateComposition(
                connectorAnchorPolicyProvider);
            return ValueTask.FromResult(new DocumentCanvasComposition(
                neutral.Document,
                neutral.Configuration,
                NeutralDemoPropertiesSchemas.Catalog,
                new NeutralDemoPipelineCountersAdapter(neutral.Counters)));
        }
    }
}

internal sealed class NeutralDemoPipelineCountersAdapter(
    NeutralDemoPipelineCounters counters) : IDocumentCanvasPipelineCounters
{
    internal NeutralDemoPipelineCounters Counters { get; } = counters;

    public int ProjectionRuleInvocationCount => Counters.ProjectionRuleInvocationCount;

    public int LayoutInvocationCount => Counters.LayoutInvocationCount;

    public int RoutingInvocationCount => Counters.RoutingInvocationCount;

    public int SceneContributionInvocationCount => Counters.SceneContributionInvocationCount;
}
