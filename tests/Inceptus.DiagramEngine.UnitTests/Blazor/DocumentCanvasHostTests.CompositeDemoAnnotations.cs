using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Runtime.Documents;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed partial class DocumentCanvasHostTests
{
    [Fact]
    public async Task DemoCompositeProviderFreezesMixedMessagesAndLaysOutExactPartChildren()
    {
        var svg = await DemoAnnotationProvider.ReadSvgAsync();
        var messages = DemoAnnotationProvider.CreateMixedMessages("Original note").ToList();
        var input = DemoAnnotationProvider.CreateInput(messages, svg);
        messages[0] = messages[0] with { Text = "Mutable list changed" };
        messages.Clear();
        var context = CompositeDemoContext(CompositeDemoPart("owner-a", "part-a", 300, 400),
            CompositeDemoPart("owner-b", "part-b", 700, 800));
        var content = input.Provider.Compose(context, input.Values);

        Assert.Empty(content.Diagnostics);
        Assert.Equal(8, content.Items.Length);
        Assert.Equal(8, content.Items.Select(item => item.Id).Distinct().Count());
        Assert.Equal(2, input.Resources.SvgImages.Count);
        Assert.All(input.Resources.SvgImages.Values, resource => Assert.Equal(svg, resource.SvgContent));
        Assert.DoesNotContain(content.Items, item => item.Geometry.Content == "Mutable list changed");
        foreach (var part in context.Parts)
        {
            var children = content.Items.Where(item => item.Origin.RelatedSceneObjectIds.Contains(part.Item.Id)).ToArray();
            Assert.Equal([Canvas2DSceneGeometryKind.Text, Canvas2DSceneGeometryKind.Image,
                Canvas2DSceneGeometryKind.Text, Canvas2DSceneGeometryKind.Image], children.Select(item => item.Geometry.Kind));
            Assert.All(children, item =>
            {
                Assert.Equal(part.OwnerId, item.Origin.SemanticElementId);
                Assert.Equal(part.Item.Id, Assert.Single(item.Origin.RelatedSceneObjectIds));
                Assert.Equal(Canvas2DSceneLayer.Decoration, item.Layer);
                Assert.Equal(Canvas2DHitTestPolicy.None, item.HitTestPolicy);
                Assert.Equal(Matrix2D.Identity, item.Transform);
                Assert.Equal(item.Geometry.Bounds, item.Clip);
                Assert.Empty(item.Metadata);
                Assert.Empty(item.PersistentAppearance);
            });
            Assert.Equal("Original note", children[0].Geometry.Content);
            Assert.Equal(new RectD(part.Item.Bounds.Left, part.Item.Bounds.Bottom + 5, 180, 24), children[0].Bounds);
            Assert.Equal(new RectD(part.Item.Bounds.Left, part.Item.Bounds.Bottom + 35, 20, 20), children[1].Bounds);
            Assert.Equal(new RectD(part.Item.Bounds.Left + 26, part.Item.Bounds.Bottom + 33, 180, 24), children[2].Bounds);
            Assert.Equal(new RectD(part.Item.Bounds.Left, part.Item.Bounds.Bottom + 63, 20, 20), children[3].Bounds);
            Assert.All(children.Where(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text), item =>
            {
                Assert.Equal("configured-demo-font", item.Style.FontFamily);
                Assert.Equal(12, item.Style.FontSize);
                Assert.Equal(Canvas2DTextBaseline.Middle, item.Geometry.TextBaseline);
            });
        }
    }

    [Fact]
    public async Task DemoCompositeMessagesUpdateArtworkWithoutChangingSiblingValuesOrStableIdentities()
    {
        var svg = await DemoAnnotationProvider.ReadSvgAsync();
        var messages = DemoAnnotationProvider.CreateMixedMessages("Retained note");
        var first = DemoAnnotationProvider.CreateInput(messages, svg);
        var changed = DemoAnnotationProvider.CreateInput(messages.Reverse().Select(message => message.Id == "status-note"
            ? message with { Text = "Updated status", AlternateArtwork = true } : message), svg);
        var context = CompositeDemoContext(CompositeDemoPart("owner", "part", 50, 70));
        var firstItems = first.Provider.Compose(context, first.Values).Items;
        var changedItems = changed.Provider.Compose(context, changed.Values).Items;
        Assert.Equal(firstItems.Select(item => item.Id), changedItems.Select(item => item.Id));
        Assert.Equal(firstItems[0], changedItems[0]);
        Assert.Equal(firstItems[1], changedItems[1]); // Its resource key is stable while submitted bytes change.
        Assert.Equal(firstItems[3], changedItems[3]);
        Assert.Equal("Updated status", changedItems[2].Geometry.Content);
        Assert.Equal(firstItems[2].Bounds, changedItems[2].Bounds);
        Assert.Equal(1, first.Resources.SvgImages.Count(entry =>
            entry.Value.SvgContent != changed.Resources.SvgImages[entry.Key].SvgContent));
        Assert.Contains(changed.Resources.SvgImages.Values, resource => resource.SvgContent.Contains("#c2410c", StringComparison.Ordinal));

        var removed = DemoAnnotationProvider.CreateInput(messages.Where(message => message.Id != "status-note"), svg);
        var remaining = removed.Provider.Compose(context, removed.Values).Items;
        Assert.Equal(2, remaining.Length);
        Assert.Equal(firstItems[0].Id, remaining[0].Id);
        Assert.Equal(firstItems[3].Id, remaining[1].Id);
        Assert.Single(removed.Resources.SvgImages);
    }

    [Theory]
    [InlineData(false, true, 1, 0)]
    [InlineData(true, true, 1, 1)]
    [InlineData(true, false, 0, 1)]
    public async Task DemoCompositeVariantChangesEmitOnlyTheCurrentLogicalChildren(bool image, bool text, int texts, int images)
    {
        var svg = await DemoAnnotationProvider.ReadSvgAsync();
        var context = CompositeDemoContext(CompositeDemoPart("owner", "part", 0, 0));
        var input = DemoAnnotationProvider.CreateInput([new("same-message", text ? "Caption" : null, image, 0)], svg);
        var children = input.Provider.Compose(context, input.Values).Items;
        Assert.Equal(texts, children.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text));
        Assert.Equal(images, children.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Image));
        Assert.Equal(images, input.Resources.SvgImages.Count);
        if (text)
        {
            var label = Assert.Single(children, item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text);
            Assert.Equal(image ? 26 : 0, label.Bounds.Left);
        }
    }

    [Fact]
    public async Task DemoCompositeProviderOmitsAmbiguousOwnersAndNeverChoosesTheirFirstPart()
    {
        var svg = await DemoAnnotationProvider.ReadSvgAsync();
        var input = DemoAnnotationProvider.CreateInput(DemoAnnotationProvider.CreateMixedMessages("Known owner"), svg);
        var context = CompositeDemoContext(CompositeDemoPart("ambiguous", "body-a", 0, 0),
            CompositeDemoPart("ambiguous", "body-b", 200, 0), CompositeDemoPart("unique", "body-c", 400, 0));
        var content = input.Provider.Compose(context, input.Values);
        Assert.Equal(4, content.Items.Length);
        Assert.All(content.Items, item => Assert.Equal("unique", item.Origin.SemanticElementId!.Value));
        Assert.Equal("DEMO_ANNOTATION_AMBIGUOUS", Assert.Single(content.Diagnostics).Code);
        Assert.Empty(input.Provider.Compose(CompositeDemoContext(), input.Values).Items);
        Assert.Throws<ArgumentException>(() => DemoAnnotationProvider.CreateInput(
            [new("duplicate", "First", false, 0), new("duplicate", null, true, 1)], svg));
    }

    [Theory]
    [InlineData(EditingSessionAccessMode.Editable)]
    [InlineData(EditingSessionAccessMode.ReadOnly)]
    public async Task DemoCompositeAnnotationControlsUpdateRemoveAndSwitchLogicalMessages(EditingSessionAccessMode mode)
    {
        await using var fixture = await ReadOnlyDemoFixture.CreateAsync(mode);
        var source = NativeDocumentSerializer.Export(fixture.Renderer.Modeler.GetDocument().Snapshot!);
        var before = Session(fixture.Host).CaptureState();
        await fixture.Renderer.ExerciseCompositeAnnotationControlsAsync(fixture.Execution);
        var after = Session(fixture.Host).CaptureState();
        Assert.Equal(before.HistoryStatus, after.HistoryStatus);
        Assert.Equal(before.DocumentRevision, after.DocumentRevision);
        Assert.Equal(before.EditorState.Viewport, after.EditorState.Viewport);
        Assert.True(source.AsSpan().SequenceEqual(NativeDocumentSerializer.Export(fixture.Renderer.Modeler.GetDocument().Snapshot!).AsSpan()));
        Assert.Empty(fixture.DocumentChanges);
        Assert.Empty(fixture.Renderer.Exceptions);
    }

    private static Canvas2DDecorationPart CompositeDemoPart(string owner, string id, double x, double y)
    {
        var item = new Canvas2DSceneItem(new(id), Canvas2DSceneLayer.Content, 0,
            Canvas2DSceneGeometry.Rectangle(new(0, 0, 120, 80)),
            new(Canvas2DSceneOriginCategory.SemanticElement, new(owner)), transform: Matrix2D.CreateTranslation(x, y));
        return new(new(owner), Canvas2DDecorationOwnerKind.Element, Canvas2DDecorationPartKind.NodeBody, item);
    }

    private static Canvas2DDecorationContext CompositeDemoContext(params Canvas2DDecorationPart[] parts) =>
        new(Guid.NewGuid(), new("composite-demo"), new(0), new ProcessScopePresentationContext(new("scope")),
            1, 1, parts.Select(part => part.Item), parts, "configured-demo-font");

#pragma warning disable BL0006 // Extend the existing renderer to exercise the real compiled Demo controls.
    private sealed partial class ApplicationComponentRenderer
    {
        internal Task ExerciseCompositeAnnotationControlsAsync(RecordingRenderExecution execution) => Dispatcher.InvokeAsync(async () =>
        {
            var nativeIds = execution.LastContent!.Items.Select(item => item.Id).ToArray();
            // Exact length-prefixed extension namespace; native BPMN paint also uses Decoration.
            const string annotationPrefix = "scene:9:extension19:example:annotations";
            Canvas2DRenderItem[] CurrentAnnotations()
            {
                var items = execution.LastContent!.Items;
                // Existing boundary guides can be baked into a decorated full frame as extra items.
                Assert.Equal(nativeIds, items.Where(item => nativeIds.Contains(item.Id, StringComparer.Ordinal))
                    .Select(item => item.Id));
                var annotations = items.Where(item => item.Id.StartsWith(annotationPrefix, StringComparison.Ordinal)).ToArray();
                Assert.All(annotations, item => Assert.Equal((int)Canvas2DSceneLayer.Decoration, item.Layer));
                return annotations;
            }

            var component = (DemoAnnotations)InstantiateComponent(typeof(DemoAnnotations));
            var componentId = AssignRootComponentId(component);
            await RenderRootComponentAsync(componentId, ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(DemoAnnotations.Maintenance)] = true, [nameof(DemoAnnotations.Modeler)] = Modeler }));
            await ClickAnnotationAsync(componentId, "show-mixed");
            AssertAnnotationStatus(componentId, "Presented", 1);
            var initial = CurrentAnnotations();
            Assert.NotEmpty(initial);
            Assert.Equal(0, initial.Length % 4);
            var owners = initial.Length / 4;
            Assert.Equal(owners * 2, initial.Count(item => item.GeometryKind == (int)Canvas2DSceneGeometryKind.Image));
            var unchangedTextIds = initial.Where(item => item.Content == "Review note").Select(item => item.Id).ToArray();
            var selectedTextIds = initial.Where(item => item.Content == "Status ready").Select(item => item.Id).ToArray();
            Assert.Equal(owners, selectedTextIds.Length);
            var select = AnnotationAttributes(componentId, "select", "data-decoration-message", "selected");
            await DispatchEventAsync(Assert.Single(select, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId,
                null, new ChangeEventArgs { Value = "status-note" });
            var input = AnnotationAttributes(componentId, "input");
            await DispatchEventAsync(Assert.Single(input, attribute => attribute.AttributeName == "onchange").AttributeEventHandlerId,
                null, new ChangeEventArgs { Value = "Changed selected" });
            await ClickAnnotationAsync(componentId, "update-one");
            AssertAnnotationStatus(componentId, "Presented", 2);
            var updated = CurrentAnnotations();
            Assert.Equal(initial.Select(item => item.Id), updated.Select(item => item.Id));
            Assert.All(selectedTextIds, id => Assert.Equal("Changed selected", Assert.Single(updated, item => item.Id == id).Content));
            Assert.All(unchangedTextIds, id => Assert.Equal("Review note", Assert.Single(updated, item => item.Id == id).Content));

            await ClickAnnotationAsync(componentId, "switch-variant"); // SVG + text -> SVG only.
            AssertAnnotationStatus(componentId, "Presented", 3);
            Assert.DoesNotContain(CurrentAnnotations(), item => selectedTextIds.Contains(item.Id));
            Assert.Equal(owners * 3, CurrentAnnotations().Length);
            await ClickAnnotationAsync(componentId, "switch-variant"); // SVG only -> text only.
            AssertAnnotationStatus(componentId, "Presented", 4);
            Assert.Equal(owners, CurrentAnnotations().Count(item => item.GeometryKind == (int)Canvas2DSceneGeometryKind.Image));
            await ClickAnnotationAsync(componentId, "switch-variant"); // Text only -> SVG + text.
            AssertAnnotationStatus(componentId, "Presented", 5);
            Assert.Equal(initial.Select(item => item.Id), CurrentAnnotations().Select(item => item.Id));

            await ClickAnnotationAsync(componentId, "remove-one");
            AssertAnnotationStatus(componentId, "Presented", 6);
            Assert.Equal(owners * 2, CurrentAnnotations().Length);
            Assert.DoesNotContain(CurrentAnnotations(), item => selectedTextIds.Contains(item.Id));
            await ClickAnnotationAsync(componentId, "clear");
            AssertAnnotationStatus(componentId, "Presented", 7);
            Assert.Empty(CurrentAnnotations());
            await ClickAnnotationAsync(componentId, "update");
            AssertAnnotationStatus(componentId, "Presented", 8);
            Assert.Equal(owners, CurrentAnnotations().Length);
            Assert.DoesNotContain(CurrentAnnotations(), item => item.GeometryKind == (int)Canvas2DSceneGeometryKind.Image);
            await ClickAnnotationAsync(componentId, "fail");
            AssertAnnotationStatus(componentId, "Failed", 9);
            Assert.Empty(CurrentAnnotations());
        });
    }
#pragma warning restore BL0006
}
