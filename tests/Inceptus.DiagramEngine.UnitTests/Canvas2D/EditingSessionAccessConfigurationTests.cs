using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Commands;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.History;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Routing;
using Inceptus.DiagramEngine.Contracts.Visuals;
using Inceptus.DiagramEngine.Runtime.Layout;
using Inceptus.DiagramEngine.Runtime.Projection;
using Inceptus.DiagramEngine.Runtime.Routing;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class EditingSessionAccessConfigurationTests
{
    [Fact]
    public void ExistingPublicConstructorRetainsItsClrSignatureAndEditableDefault()
    {
        // Optional parameters are part of the CLR signature: appending one would break
        // callers compiled against the existing constructor even if source still builds.
        var constructor = Assert.Single(typeof(EditingSessionConfiguration).GetConstructors());
        Type[] parameterTypes =
        [
            typeof(ProjectionEngine), typeof(LayoutEngine), typeof(AlgorithmId),
            typeof(RoutingEngine), typeof(AlgorithmId), typeof(Canvas2DSceneBuilder),
            typeof(ProjectionContext), typeof(LayoutContext), typeof(RoutingContext),
            typeof(EditorStateSnapshot), typeof(IEnumerable<CommandHandlerRegistration>),
            typeof(IEnumerable<CommandValidatorRegistration>),
            typeof(IEnumerable<CommandHistoryPolicyRegistration>),
            typeof(IEnumerable<IDocumentChangedSubscriber>),
            typeof(IElementConnectorAnchorPolicyProvider), typeof(ModelProfileCatalog),
            typeof(ModelProfileViewStateSnapshot), typeof(IRoutingInputPreparer),
            typeof(IGridSettingsSource),
        ];
        var parameters = constructor.GetParameters();
        Assert.Equal(parameterTypes, parameters.Select(static parameter => parameter.ParameterType));
        Assert.All(parameters.Take(6), static parameter => Assert.False(parameter.HasDefaultValue));
        Assert.All(parameters.Skip(6), static parameter =>
        {
            Assert.True(parameter.HasDefaultValue);
            Assert.Null(parameter.DefaultValue);
        });
        Assert.Equal(EditingSessionAccessMode.Editable,
            EditingSessionTestHarness.Configuration().AccessMode);
    }

    [Fact]
    public void AccessCopyRetainsConfiguredPipelineContributionsAndViewState()
    {
        var registration = BpmnPluginRegistration.N100;
        Assert.NotEmpty(registration.ModelProfileDefinitions);
        var hiddenProfile = registration.ModelProfileDefinitions[0].Id;
        var source = new EditingSessionConfiguration(
            new ProjectionEngine(registration.ProjectionRules, registration.PresentationViewProviders),
            new LayoutEngine(registration.LayoutAlgorithms),
            BpmnAlgorithmIds.DefaultLayout,
            new RoutingEngine(registration.RoutingAlgorithms),
            BpmnAlgorithmIds.DefaultRouting,
            new Canvas2DSceneBuilder(contributors: registration.SceneContributors),
            new ProjectionContext([new("projection-option", PropertyValue.FromText("configured"))]),
            new LayoutContext([new("layout-option", PropertyValue.FromInteger(3))]),
            new RoutingContext([new("routing-option", PropertyValue.FromBoolean(true))]),
            new EditorStateSnapshot(activeToolId: "test:inspection", focusTargetId: "test:focus"),
            registration.CommandHandlers,
            registration.CommandValidators,
            registration.HistoryPolicies,
            [new TestSubscriber()],
            new ElementConnectorAnchorPolicyRegistry(registration.ConnectorAnchorPolicies),
            new ModelProfileCatalog(registration.ModelProfileDefinitions),
            new ModelProfileViewStateSnapshot([hiddenProfile]),
            new TestRoutingInputPreparer(),
            new TestGridSettingsSource());

        var readOnly = source.WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var editableCopy = readOnly.WithAccessMode(EditingSessionAccessMode.Editable);

        Assert.Equal(EditingSessionAccessMode.Editable, source.AccessMode);
        Assert.Equal(EditingSessionAccessMode.ReadOnly, readOnly.AccessMode);
        Assert.Equal(EditingSessionAccessMode.Editable, editableCopy.AccessMode);
        Assert.NotSame(source, readOnly);
        Assert.NotSame(readOnly, editableCopy);
        Assert.NotEmpty(source.CommandHandlers);
        Assert.NotEmpty(source.CommandValidators);
        Assert.NotEmpty(source.HistoryPolicies);

        foreach (var copy in new[] { readOnly, editableCopy })
        {
            Assert.Same(source.ProjectionEngine, copy.ProjectionEngine);
            Assert.Same(source.LayoutEngine, copy.LayoutEngine);
            Assert.Same(source.LayoutAlgorithmId, copy.LayoutAlgorithmId);
            Assert.Same(source.RoutingEngine, copy.RoutingEngine);
            Assert.Same(source.RoutingAlgorithmId, copy.RoutingAlgorithmId);
            Assert.Same(source.SceneBuilder, copy.SceneBuilder);
            Assert.Same(source.ProjectionContext, copy.ProjectionContext);
            Assert.Same(source.LayoutContext, copy.LayoutContext);
            Assert.Same(source.RoutingContext, copy.RoutingContext);
            Assert.Same(source.InitialEditorState, copy.InitialEditorState);
            Assert.Equal(source.CommandHandlers, copy.CommandHandlers);
            Assert.Equal(source.CommandValidators, copy.CommandValidators);
            Assert.Equal(source.HistoryPolicies, copy.HistoryPolicies);
            Assert.Equal(source.DocumentChangedSubscribers, copy.DocumentChangedSubscribers);
            Assert.Same(source.ConnectorAnchorPolicyProvider, copy.ConnectorAnchorPolicyProvider);
            Assert.Same(source.ModelProfileCatalog, copy.ModelProfileCatalog);
            Assert.Same(source.InitialModelProfileViewState, copy.InitialModelProfileViewState);
            Assert.Same(source.RoutingInputPreparer, copy.RoutingInputPreparer);
            Assert.Same(source.GridSettings, copy.GridSettings);
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    [InlineData(int.MaxValue)]
    public void UndefinedAccessModeCannotCreateAnUnprotectedConfiguration(int mode)
    {
        var source = EditingSessionTestHarness.Configuration();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            source.WithAccessMode((EditingSessionAccessMode)mode));

        Assert.Equal(EditingSessionAccessMode.Editable, source.AccessMode);
    }

    private sealed class TestSubscriber : IDocumentChangedSubscriber
    {
        public ValueTask OnDocumentChangedAsync(DocumentChangedEvent change) => ValueTask.CompletedTask;
    }

    private sealed class TestRoutingInputPreparer : IRoutingInputPreparer
    {
        public PreparedRoutingInput? Prepare(DocumentSnapshot document, DocumentScopeId activeScopeId,
            ProjectedGraph graph, LayoutResult layout, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Copying configuration must not prepare routing.");
    }

    private sealed class TestGridSettingsSource : IGridSettingsSource
    {
        public GridSettings Grid { get; } = new() { PointDistance = 35d, ShowGrid = false };
    }
}
