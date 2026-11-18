using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Metadata;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Semantics;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Documents;
using Inceptus.DiagramEngine.Runtime.Layout;
using Inceptus.DiagramEngine.Runtime.Projection;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionDecorationSavedGeometryTests
{
    [Fact]
    public async Task DecorationUpdatesNeverReenterInstrumentedNativeSavedGeometryPreparation()
    {
        var registration = BpmnPluginRegistration.N100;
        var probes = registration.SceneContributors.Select(entry => new PreparationProbe(entry.Contributor)).ToArray();
        var contributors = registration.SceneContributors.Select((entry, index) =>
            new Canvas2DSceneContributorRegistration(entry.Descriptor, probes[index], entry.Stage));
        var configuration = new EditingSessionConfiguration(
            new ProjectionEngine(registration.ProjectionRules, registration.PresentationViewProviders),
            new LayoutEngine(registration.LayoutAlgorithms), BpmnAlgorithmIds.DefaultLayout,
            new RoutingEngine(registration.RoutingAlgorithms), BpmnAlgorithmIds.DefaultRouting,
            new Canvas2DSceneBuilder(contributors: contributors),
            connectorAnchorPolicyProvider: new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies));
        var documentId = new DocumentId("decoration:saved-geometry");
        var owner = new SemanticElementId("decoration:task");
        var initial = new DocumentSnapshot(new SemanticModelSnapshot(documentId, DocumentRevision.Zero,
            [new SemanticElementSnapshot(owner, BpmnSemanticTypes.Task,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Native task"))])]),
            new VisualModelSnapshot(documentId, DocumentRevision.Zero,
                [new(new("decoration:visual"), owner, new(100, 100), new(120, 80), VisualPlacementMode.Pinned)]),
            new DocumentMetadataSnapshot(documentId, DocumentRevision.Zero));
        var document = Assert.IsType<Document>(DocumentReconstructor.Reconstruct(initial,
            configuration.ConnectorAnchorPolicyProvider).Document);
        var (renderer, execution) = await EditingSessionTestHarness.CreateInitializedRendererAsync();
        var attached = await EditingSession.AttachAsync(document, renderer, configuration);
        await using var session = Assert.IsType<EditingSession>(attached.Session);
        Assert.Equal(EditingSessionAttachStatus.Ready, attached.Status);
        var savedGeometryProbes = registration.SceneContributors.Select((entry, index) => (entry, index))
            .Where(pair => pair.entry.Descriptor.PlacementDependency != Canvas2DScenePlacementDependency.BoundedFeedbackOnly)
            .Select(pair => probes[pair.index]).ToArray();
        Assert.NotEmpty(savedGeometryProbes);
        Assert.All(savedGeometryProbes, probe => Assert.True(probe.BaseCalls > 0));
        Assert.All(savedGeometryProbes, probe => Assert.True(probe.PresentationCalls > 0));
        var beforeCounts = probes.Select(probe => (probe.BaseCalls, probe.PresentationCalls, probe.ContributionCalls)).ToArray();
        var before = (await session.CaptureDocumentSnapshotAsync())!;
        Assert.NotNull(before.VisualModel.RoutingScopes);
        var beforeState = session.CaptureState();
        var provider = new SavedGeometryAnnotationProvider();
        foreach (var revision in new long[] { 1, 2, 3 })
        {
            var result = await session.ReplaceDecorationsAsync(revision, new(new("test:geometry-annotation"), provider));
            Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, result.Status);
            Assert.Contains(execution.LastFrame!.Items, item => item.Id ==
                Canvas2DSceneObjectIdentity.ForExtension(new("test:geometry-annotation"), "body").Value);
        }
        Assert.Equal(Canvas2DDecorationUpdateStatus.Presented, (await session.ReplaceDecorationsAsync(4, null)).Status);
        Assert.Equal(beforeCounts, probes.Select(probe => (probe.BaseCalls, probe.PresentationCalls, probe.ContributionCalls)).ToArray());
        Assert.Same(before, await session.CaptureDocumentSnapshotAsync());
        Assert.Same(beforeState.ProjectedGraph, session.CaptureState().ProjectedGraph);
        Assert.Same(beforeState.LayoutResult, session.CaptureState().LayoutResult);
        Assert.Same(beforeState.RoutingResult, session.CaptureState().RoutingResult);
        Assert.Equal(beforeState.HistoryStatus, session.CaptureState().HistoryStatus);
    }

    private sealed class PreparationProbe(ICanvas2DSceneContributor source) : ICanvas2DSceneContributor, ICanvas2DScopeGeometryContributor
    {
        internal int BaseCalls { get; private set; }
        internal int PresentationCalls { get; private set; }
        internal int ContributionCalls { get; private set; }

        public Canvas2DSceneContributionResult Contribute(Canvas2DSceneContributionContext context)
        {
            ContributionCalls++;
            return source.Contribute(context);
        }

        public Canvas2DScopeGeometryBaseResult PrepareBase(Canvas2DScopeGeometryBaseContext context)
        {
            BaseCalls++;
            return ((ICanvas2DScopeGeometryContributor)source).PrepareBase(context);
        }

        public Canvas2DScopeGeometryPresentationResult PreparePresentation(Canvas2DScopeGeometryPresentationContext context)
        {
            PresentationCalls++;
            return ((ICanvas2DScopeGeometryContributor)source).PreparePresentation(context);
        }
    }

    private sealed class SavedGeometryAnnotationProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            var part = context.FindParts(new("decoration:task"), Canvas2DDecorationOwnerKind.Element,
                Canvas2DDecorationPartKind.NodeBody).Single();
            return new([new(Canvas2DSceneObjectIdentity.ForExtension(new("test:geometry-annotation"), "body"),
                Canvas2DSceneLayer.Decoration, 0,
                Canvas2DSceneGeometry.Rectangle(new(part.Item.Bounds.X, part.Item.Bounds.Bottom + 5, 20, 10)),
                part.CreateOrigin("body"))]);
        }
    }
}
