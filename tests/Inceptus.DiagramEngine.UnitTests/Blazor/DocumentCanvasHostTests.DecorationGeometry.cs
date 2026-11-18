using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Bpmn.Blazor.Composition;
using Inceptus.DiagramEngine.Bpmn.Semantics;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task DecorationsAssociateExactNativePoolLaneParticipantAndTransformedBodies(EditingSessionAccessMode mode)
    {
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1920, 1080, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: A22PoolSnapshot(), accessMode: mode));
        await host.InitializeAsync("decoration-organizational-geometry", "container");
        var before = host.CaptureEditorContext()!;
        var provider = new DecorationGeometryProbe();
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(host.CaptureDecorationAttachment()!, 1,
            new(DecorationGeometryProbe.ProviderId, provider)), 1, true);
        var context = Assert.IsType<Canvas2DDecorationContext>(provider.LastContext);
        foreach (var owner in new[] { "company", "sales", "warehouse", "supplier" })
        {
            var parts = context.FindParts(new(owner), Canvas2DDecorationOwnerKind.Element, Canvas2DDecorationPartKind.SemanticRegion);
            Assert.NotEmpty(parts);
            Assert.All(parts, part =>
            {
                Assert.Null(part.Item.Origin.VisualStateId);
                Assert.Equal(owner, part.Item.Origin.SemanticElementId!.Value);
                AssertGeometryDecoration(execution, part);
            });
        }
        // Sales and warehouse are Lanes of company; supplier is an independent Pool. Their
        // semantic owners remain distinct even though the shared native presenter composes them.
        var taskParts = context.Parts.Where(part => part.Kind == Canvas2DDecorationPartKind.NodeBody).ToArray();
        Assert.NotEmpty(taskParts);
        Assert.Contains(taskParts, part => part.Item.SpatialRegion is not null);
        Assert.Contains(taskParts, part => part.Item.Bounds.TopLeft != before.Document.VisualModel.VisualStates
            .Single(visual => visual.Id == part.Item.Origin.VisualStateId).Position);
        Assert.All(taskParts, part => AssertGeometryDecoration(execution, part));
        Assert.Same(before.Document, host.CaptureEditorContext()!.Document);
        Assert.Same(before.Presentation.CurrentScene, host.CaptureEditorContext()!.Presentation.CurrentScene);
        Assert.Equal(before.Presentation.HistoryStatus, host.CaptureEditorContext()!.Presentation.HistoryStatus);
    }

    [Fact]
    public async Task DecorationsKeepNativeNodeAndRelationshipLabelsSeparateDespiteDuplicateNames()
    {
        var source = A23Snapshot([("source", "target")]);
        var named = new DocumentSnapshot(new SemanticModelSnapshot(source.DocumentId, source.Revision,
            source.SemanticModel.Elements.Select(element => new SemanticElementSnapshot(element.Id, element.TypeId,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Same caption"))])),
            source.SemanticModel.Relationships.Select(relationship => new SemanticRelationshipSnapshot(
                relationship.Id, relationship.TypeId, relationship.SourceId, relationship.TargetId,
                [new(BpmnSemanticProperties.Name, PropertyValue.FromText("Same caption"))]))),
            source.VisualModel, source.Metadata);
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)),
            compositionFactory: new BpmnModelerCompositionFactory(initialDocument: named));
        await host.InitializeAsync("decoration-label-geometry", "container");
        var provider = new DecorationGeometryProbe();
        AssertDecorationPresented(await host.ReplaceDecorationsAsync(host.CaptureDecorationAttachment()!, 1,
            new(DecorationGeometryProbe.ProviderId, provider)), 1, true);
        var context = Assert.IsType<Canvas2DDecorationContext>(provider.LastContext);
        var nodeBody = Assert.Single(context.FindParts(new("source"), Canvas2DDecorationOwnerKind.Element,
            Canvas2DDecorationPartKind.NodeBody));
        var nodeLabels = context.FindParts(new("source"), Canvas2DDecorationOwnerKind.Element,
            Canvas2DDecorationPartKind.NodeLabel);
        var relationshipPath = Assert.Single(context.FindParts(new("flow-0"), Canvas2DDecorationOwnerKind.Relationship,
            Canvas2DDecorationPartKind.ConnectorPath));
        var relationshipLabels = context.FindParts(new("flow-0"), Canvas2DDecorationOwnerKind.Relationship,
            Canvas2DDecorationPartKind.ConnectorLabel);
        Assert.NotEmpty(nodeLabels);
        Assert.NotEmpty(relationshipLabels);
        Assert.All(nodeLabels, part => Assert.NotEqual(nodeBody.Item.Id, part.Item.Id));
        Assert.All(relationshipLabels, part => Assert.NotEqual(relationshipPath.Item.Id, part.Item.Id));
        Assert.Contains(nodeLabels, part => part.Item.Geometry.Content == "Same caption");
        Assert.Contains(relationshipLabels, part => part.Item.Geometry.Content == "Same caption");
        foreach (var part in nodeLabels.Concat(relationshipLabels).Append(nodeBody).Append(relationshipPath))
            AssertGeometryDecoration(execution, part);
        Assert.Empty(context.FindParts(new("source"), Canvas2DDecorationOwnerKind.Relationship,
            Canvas2DDecorationPartKind.ConnectorLabel));
    }

    [Fact]
    public async Task DecorationsBorrowOnlyCurrentNestedScopeNativePartsAfterNavigation()
    {
        var factory = (await BpmnModelerTestComposition.CreateFreshDemoFactoryAsync()).WithAccessMode(EditingSessionAccessMode.ReadOnly);
        var execution = new RecordingRenderExecution();
        await using var host = CreateHost(execution, new RecordingSurfaceObserver(new(1400, 900, 1)), compositionFactory: factory);
        await host.InitializeAsync("decoration-nested-geometry", "container");
        var provider = new DecorationGeometryProbe();
        var attachment = host.CaptureDecorationAttachment()!;
        var updated = await host.ReplaceDecorationsAsync(attachment, 1,
            new(DecorationGeometryProbe.ProviderId, provider));
        Assert.True(updated.Status == Canvas2DDecorationUpdateStatus.Presented,
            string.Join("\n", updated.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message} ({diagnostic.SourceIdentity})")));
        AssertDecorationPresented(updated, 1, true);
        var rootContext = Assert.IsType<Canvas2DDecorationContext>(provider.LastContext);
        var nativeDerivedParts = rootContext.Parts.Where(part =>
            (part.Item.Origin.Categories & Canvas2DSceneOriginCategory.Configuration) != 0).ToArray();
        Assert.NotEmpty(nativeDerivedParts);
        Assert.All(nativeDerivedParts, part => AssertGeometryDecoration(execution, part));
        var navigation = await host.NavigateToScopeAsync(host.CapturePresentation().Stamp!, BpmnDemoPipeline.ProcessOrderScopeId);
        Assert.True(navigation.Succeeded);
        var nestedContext = Assert.IsType<Canvas2DDecorationContext>(provider.LastContext);
        var native = host.CaptureEditorContext()!.Presentation.CurrentScene!;
        Assert.NotEqual(rootContext.PresentationContext, nestedContext.PresentationContext);
        Assert.Equal(1, nestedContext.InputRevision);
        Assert.True(nestedContext.BaseSceneGeneration > rootContext.BaseSceneGeneration);
        Assert.Equal(attachment, host.CaptureDecorationAttachment());
        Assert.NotEmpty(nestedContext.Parts);
        Assert.All(nestedContext.Parts, part =>
        {
            Assert.Same(native.Items.Single(item => item.Id == part.Item.Id), part.Item);
            AssertGeometryDecoration(execution, part);
        });
        var nestedIds = nestedContext.Parts.Select(part => part.Item.Id).ToHashSet();
        foreach (var previous in rootContext.Parts.Where(part => !nestedIds.Contains(part.Item.Id)))
            Assert.DoesNotContain(execution.LastContent!.Items, item => item.Id == DecorationGeometryProbe.DecorationId(previous));
        Assert.True(host.CaptureEditorContext()!.Presentation.DecorationPresentation!.FramePresented);
        Assert.Equal(0, host.CaptureEditorContext()!.Presentation.HistoryStatus.EntryCount);
    }

    private static void AssertGeometryDecoration(RecordingRenderExecution execution, Canvas2DDecorationPart part)
    {
        var marker = Assert.Single(execution.LastContent!.Items, item => item.Id == DecorationGeometryProbe.DecorationId(part));
        Assert.Equal(part.Item.Bounds.Left, marker.GeometryBounds.X);
        Assert.Equal(part.Item.Bounds.Bottom + 3, marker.GeometryBounds.Y);
        Assert.Null(marker.Clip);
    }

    private sealed class DecorationGeometryProbe : ICanvas2DDecorationProvider
    {
        internal static Canvas2DSceneContributorId ProviderId { get; } = new("test:geometry-parts");
        internal Canvas2DDecorationContext? LastContext { get; private set; }
        internal static string DecorationId(Canvas2DDecorationPart part) => Canvas2DSceneObjectIdentity.ForExtension(ProviderId, part.Item.Id.Value).Value;

        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            // Capturing an immutable context is test observation only; no captured value feeds composition.
            LastContext = context;
            return new(context.Parts.Select(part => new Canvas2DSceneItem(
                Canvas2DSceneObjectIdentity.ForExtension(ProviderId, part.Item.Id.Value), Canvas2DSceneLayer.Decoration,
                0, Canvas2DSceneGeometry.Rectangle(new(part.Item.Bounds.Left, part.Item.Bounds.Bottom + 3, 5, 2)),
                part.CreateOrigin(part.Item.Id.Value))));
        }
    }
}
