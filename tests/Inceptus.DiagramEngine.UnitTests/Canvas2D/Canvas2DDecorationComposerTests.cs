using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Metadata;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Properties;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class Canvas2DDecorationComposerTests
{
    private static readonly Canvas2DSceneContributorId ProviderId = new("test:passive-annotations");

    [Fact]
    public void InputCopiesValuesAndOutputCopiesItemsWithBoundedDiagnostics()
    {
        var values = new Dictionary<string, PropertyValue> { ["caption"] = PropertyValue.FromText("first") };
        var input = new Canvas2DDecorationInput(ProviderId, new EmptyProvider(), values);
        values["caption"] = PropertyValue.FromText("later");
        Assert.Equal("first", input.Values["caption"].TextValue);
        var diagnostics = new List<Diagnostic> { new("TEST", DiagnosticSeverity.Warning, "test") };
        var output = new Canvas2DDecorationContent(diagnostics: diagnostics);
        diagnostics.Clear();
        Assert.Single(output.Diagnostics);
        Assert.Throws<ArgumentException>(() => new Canvas2DDecorationContent(diagnostics:
            Enumerable.Repeat(new Diagnostic("TEST", DiagnosticSeverity.Warning, "test"), 33)));
    }

    [Theory]
    [InlineData(10000, true)]
    [InlineData(10001, false)]
    public void PrimitiveBudgetCountsBothMessageChildrenAndRejectsOversizedOutputAtomically(int count, bool succeeds)
    {
        var (scene, document) = CreateNative();
        var result = Canvas2DDecorationComposer.Compose(scene, document, Guid.NewGuid(), 1, 1,
            new(ProviderId, new ManyMessageChildrenProvider(count)),
            new("configured-font", ["configured-font"], ["example:shared-image"]));
        Assert.Equal(succeeds, result.Succeeded);
        if (succeeds)
        {
            var items = result.Scene.Items.Where(item => item.Layer == Canvas2DSceneLayer.Decoration).ToArray();
            Assert.Equal(10000, items.Length); // 5,000 logical messages each have two primitives.
            Assert.Equal(5000, items.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Image));
            Assert.Equal(5000, items.Count(item => item.Geometry.Kind == Canvas2DSceneGeometryKind.Text));
            Assert.Equal(10000, items.Select(item => item.Id).Distinct().Count());
            Assert.Empty(result.Diagnostics);
        }
        else
        {
            Assert.Same(scene, result.Scene);
            Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "CANVAS2D_DECORATION_FAILED");
            Assert.DoesNotContain(result.Scene.Items, item => item.Origin.StableSourceKey?.StartsWith("message:", StringComparison.Ordinal) == true);
        }
        foreach (var native in scene.Items)
            Assert.Same(native, result.Scene.Items.Single(item => item.Id == native.Id));
    }

    [Fact]
    public void EmptyInputPreservesNativeSceneContentAndReuseIdentity()
    {
        var (scene, document) = CreateNative();
        var result = Compose(scene, document, new EmptyProvider());
        Assert.True(result.Succeeded);
        Assert.Same(scene, result.Scene);
        Assert.Equal(scene.Contributors, result.Scene.Contributors);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void FinalCoordinatesOwnerKindsExactPartsAndCurrencyReachProvider()
    {
        var (scene, document) = CreateNative();
        var attachment = Guid.NewGuid();
        var provider = new InspectingProvider();
        var result = Canvas2DDecorationComposer.Compose(scene, document, attachment, 7, 11,
            new(ProviderId, provider), new(null, [], []));
        Assert.True(result.Succeeded);
        var context = Assert.IsType<Canvas2DDecorationContext>(provider.Context);
        Assert.Equal(attachment, context.AttachmentId);
        Assert.Equal(7, context.BaseSceneGeneration);
        Assert.Equal(11, context.InputRevision);
        Assert.Equal(document.DocumentId, context.DocumentId);
        Assert.Equal(document.Revision, context.SourceRevision);
        Assert.Equal(scene.Items, context.BaseItems);
        Assert.Equal(2, context.Parts.Count(part => part.Kind == Canvas2DDecorationPartKind.NodeBody));
        var connector = Assert.Single(context.Parts.Where(part => part.Kind == Canvas2DDecorationPartKind.ConnectorPath));
        Assert.Equal(Canvas2DDecorationOwnerKind.Relationship, connector.OwnerKind);
        foreach (var part in context.Parts)
            Assert.Same(scene.Items.Single(item => item.Id == part.Item.Id), part.Item);
        var owner = context.Parts.First(part => part.Kind == Canvas2DDecorationPartKind.NodeBody);
        Assert.Single(context.FindParts(owner.OwnerId, Canvas2DDecorationOwnerKind.Element, Canvas2DDecorationPartKind.NodeBody));
        Assert.Empty(context.FindParts(owner.OwnerId, Canvas2DDecorationOwnerKind.Relationship, Canvas2DDecorationPartKind.NodeBody));
        Assert.Empty(context.FindParts(new("missing"), Canvas2DDecorationOwnerKind.Element, Canvas2DDecorationPartKind.NodeBody));
    }

    [Fact]
    public void CompositionIsAdditiveOutsideBodyAndNeverAccumulatesOrMutatesNativeScene()
    {
        var (scene, document) = CreateNative();
        var originalItems = scene.Items;
        var result = Compose(scene, document, new AnnotationProvider());
        var repeated = Compose(scene, document, new AnnotationProvider());
        Assert.True(result.Succeeded);
        Assert.True(repeated.Succeeded);
        Assert.Equal(originalItems.Length + 1, result.Scene.ItemCount);
        Assert.True(result.Scene.Items.AsSpan().SequenceEqual(repeated.Scene.Items.AsSpan()));
        Assert.Equal(originalItems, scene.Items);
        Assert.Equal(scene.Contributors, result.Scene.Contributors);
        Assert.Equal(scene.Viewport, result.Scene.Viewport);
        Assert.Null(result.Scene.BoundedPresentation);
        Assert.Null(result.Scene.PanReuseSource);
        var decoration = Assert.Single(result.Scene.Items.Except(originalItems));
        Assert.Null(decoration.Clip);
        Assert.Equal(100000, decoration.Bounds.X);
        Assert.Equal(Canvas2DHitTestMode.None, decoration.HitTestPolicy.Mode);
        foreach (var native in originalItems)
            Assert.Same(native, result.Scene.Items.Single(item => item.Id == native.Id));
    }

    [Fact]
    public void PartLookupPreservesAmbiguityAndOmitsHiddenOrFullyClippedRepresentations()
    {
        var (scene, document) = CreateNative();
        var source = scene.Items.Single(item => item.Origin.SemanticElementId?.Value == "test:semantic:a");
        var processPlacement = new Inceptus.DiagramEngine.Contracts.Presentation.PresentationScopePlacement(
            document.SemanticModel.RootScopeId, new(500, 600));
        Canvas2DSceneItem Copy(string key, bool visible, RectD? clip = null) => new(processPlacement, new(key), source.Layer, source.ZIndex,
            source.Geometry, source.Origin, Matrix2D.CreateTranslation(500, 600), clip, source.Style,
            visible, source.HitTestPolicy, metadata: source.Metadata);
        var second = Copy("another-representation", true);
        var hidden = Copy("hidden-representation", false);
        var clipped = Copy("clipped-representation", true, new(10000, 10000, 1, 1));
        var native = scene.WithDecorations(scene.Items.Add(second).Add(hidden).Add(clipped));
        var provider = new InspectingProvider();
        Assert.True(Compose(native, document, provider).Succeeded);
        var context = Assert.IsType<Canvas2DDecorationContext>(provider.Context);
        var matches = context.FindParts(source.Origin.SemanticElementId!, Canvas2DDecorationOwnerKind.Element,
            Canvas2DDecorationPartKind.NodeBody);
        Assert.Equal(2, matches.Length);
        Assert.Contains(matches, part => part.Item.Id == source.Id);
        Assert.Contains(matches, part => part.Item.Id == second.Id && part.Item.Bounds.X == 500 && part.Item.Bounds.Y == 600);
        Assert.Same(processPlacement, matches.Single(part => part.Item.Id == second.Id).Item.ProcessPlacement);
        Assert.DoesNotContain(context.Parts, part => part.Item.Id == hidden.Id || part.Item.Id == clipped.Id);
    }

    [Theory]
    [InlineData(0, 20)]
    [InlineData(20, 0)]
    [InlineData(0, 0)]
    public void EmptyClipsOmitPartsWithoutHidingHorizontalOrVerticalPaths(double clipWidth, double clipHeight)
    {
        var (scene, document) = CreateNative();
        var source = scene.Items.Single(item => item.Origin.SemanticElementId?.Value == "test:semantic:a");
        var clipped = new Canvas2DSceneItem(new("empty-clip"), source.Layer, source.ZIndex,
            source.Geometry, source.Origin, clip: new(source.Bounds.X, source.Bounds.Y, clipWidth, clipHeight));
        var horizontal = new Canvas2DSceneItem(new("horizontal-path"), Canvas2DSceneLayer.Content, 0,
            Canvas2DSceneGeometry.Path([new(5, 10), new(15, 10)]), source.Origin, clip: new(0, 0, 20, 20));
        var vertical = new Canvas2DSceneItem(new("vertical-path"), Canvas2DSceneLayer.Content, 0,
            Canvas2DSceneGeometry.Path([new(10, 5), new(10, 15)]), source.Origin, clip: new(0, 0, 20, 20));
        var native = scene.WithDecorations(scene.Items.Add(clipped).Add(horizontal).Add(vertical));
        var provider = new InspectingProvider();

        Assert.True(Compose(native, document, provider).Succeeded);

        var context = Assert.IsType<Canvas2DDecorationContext>(provider.Context);
        Assert.Contains(context.BaseItems, item => ReferenceEquals(item, clipped));
        Assert.DoesNotContain(context.Parts, part => part.Item.Id == clipped.Id);
        Assert.Equal(0, horizontal.Bounds.Height);
        Assert.Equal(0, vertical.Bounds.Width);
        Assert.Contains(context.Parts, part => ReferenceEquals(part.Item, horizontal));
        Assert.Contains(context.Parts, part => ReferenceEquals(part.Item, vertical));
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("native-id")]
    [InlineData("wrong-namespace")]
    [InlineData("unknown-origin")]
    [InlineData("forged-configuration-origin")]
    [InlineData("interactive")]
    [InlineData("metadata")]
    [InlineData("appearance")]
    [InlineData("overlay")]
    [InlineData("invalid-image")]
    [InlineData("invalid-font")]
    [InlineData("invalid-bounds")]
    [InlineData("invalid-clip")]
    public void InvalidProviderOutputFallsBackToNativeBaseWithBoundedFailure(string mode)
    {
        var (scene, document) = CreateNative();
        var result = Compose(scene, document, new AnnotationProvider(mode));
        Assert.False(result.Succeeded);
        Assert.Same(scene, result.Scene);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Severity == DiagnosticSeverity.Error);
        Assert.InRange(result.Diagnostics.Length, 1, Canvas2DDecorationContent.MaximumDiagnostics);
    }

    [Theory]
    [InlineData("invalid-font")]
    [InlineData("invalid-image")]
    public void ResourceReferencesAreValidatedAgainstTheFrozenNativeResourceIdentities(string mode)
    {
        var (scene, document) = CreateNative();
        var fonts = new List<string> { "configured-font" };
        var images = new List<string> { "missing-resource" };
        var resources = new Canvas2DDecorationResources("configured-font", fonts, images);
        fonts.Clear();
        images.Clear();
        var result = Canvas2DDecorationComposer.Compose(scene, document, Guid.NewGuid(), 1, 1,
            new(ProviderId, new AnnotationProvider(mode)), resources);
        Assert.True(result.Succeeded);
        Assert.Equal(scene.ItemCount + 1, result.Scene.ItemCount);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void ProviderFailureAndErrorsCannotRestorePreviousValues()
    {
        var (scene, document) = CreateNative();
        var first = Compose(scene, document, new AnnotationProvider());
        Assert.True(first.Succeeded);
        var failure = Compose(scene, document, new ThrowingProvider());
        Assert.False(failure.Succeeded);
        Assert.Same(scene, failure.Scene);
        Assert.DoesNotContain(failure.Scene.Items, item => item.Id == Canvas2DSceneObjectIdentity.ForExtension(ProviderId, "annotation"));
    }

    [Fact]
    public void SemanticOnlyPartsRetainExactOwnerAndOriginWithoutInventedProjection()
    {
        var (scene, document) = CreateNative();
        var owner = document.SemanticModel.Elements[0].Id;
        var region = new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForExtension(new("native:region"), "region"),
            Canvas2DSceneLayer.Content, 0, Canvas2DSceneGeometry.Rectangle(new(50, 60, 500, 300)),
            new(Canvas2DSceneOriginCategory.SemanticElement | Canvas2DSceneOriginCategory.RegisteredExtension,
                owner, stableSourceKey: "region"), metadata: [Canvas2DSemanticSceneInteractionMetadata.Enabled]);
        var native = scene.WithDecorations(scene.Items.Add(region));
        var result = Compose(native, document, new SemanticProvider());
        Assert.True(result.Succeeded);
        var decoration = result.Scene.Items.Single(item => item.Id == Canvas2DSceneObjectIdentity.ForExtension(ProviderId, "region-annotation"));
        Assert.Null(decoration.Origin.VisualStateId);
        Assert.Null(decoration.Origin.ProjectedObjectId);
        Assert.Equal(owner, decoration.Origin.SemanticElementId);
        Assert.Equal(region.Id, Assert.Single(decoration.Origin.RelatedSceneObjectIds));
    }

    [Fact]
    public void FeedbackIsAboveDecorationsAndNativeOrderingRemainsStableWithinItsLanes()
    {
        var (scene, document) = CreateNative();
        var feedback = new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState("selection"),
            Canvas2DSceneLayer.Connector, 10, Canvas2DSceneGeometry.Rectangle(new(1, 2, 3, 4)),
            new(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: "selection"));
        var native = scene.WithDecorations(scene.Items.Insert(1, feedback));
        var result = Compose(native, document, new AnnotationProvider());
        Assert.True(result.Succeeded);
        Assert.Same(feedback, result.Scene.Items[^1]);
        Assert.Equal(scene.Items, result.Scene.Items.Where(item => item != feedback && item.Layer != Canvas2DSceneLayer.Decoration));
    }

    [Fact]
    public void CombinedFrameBakesBoundaryGuidesWithoutReusingStaleContentIndices()
    {
        var (scene, document) = CreateNative();
        var feedback = new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState("feedback"),
            Canvas2DSceneLayer.Connector, 10, Canvas2DSceneGeometry.Rectangle(new(1, 2, 3, 4)),
            new(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: "feedback"));
        var guide = new Canvas2DSceneItem(Canvas2DSceneObjectIdentity.ForEditorState("document-boundary:x-axis"),
            Canvas2DSceneLayer.Background, 0, Canvas2DSceneGeometry.Path([new(0, 0), new(100, 0)]),
            new(Canvas2DSceneOriginCategory.EditorState, stableSourceKey: "document-boundary:x-axis"));
        var native = scene.WithDecorations(scene.Items.Insert(1, feedback), new([guide], 2, 0));
        var result = Compose(native, document, new AnnotationProvider());
        Assert.True(result.Succeeded);
        Assert.Empty(result.Scene.BoundaryGuides.Items);
        Assert.Same(guide, result.Scene.Items[1]);
        Assert.Same(scene.Items[1], result.Scene.Items[2]);
        Assert.Same(feedback, result.Scene.Items[^1]);
        Assert.Equal(2, native.BoundaryGuides.HorizontalContentIndex);
    }

    private static Canvas2DDecorationComposition Compose(Canvas2DScene scene, DocumentSnapshot document,
        ICanvas2DDecorationProvider provider) => Canvas2DDecorationComposer.Compose(scene, document,
            Guid.NewGuid(), 1, 1, new(ProviderId, provider), new(null, [], []));

    private static (Canvas2DScene Scene, DocumentSnapshot Document) CreateNative()
    {
        var data = Canvas2DSceneTestData.Create();
        var scene = Assert.IsType<Canvas2DScene>(new Canvas2DSceneBuilder().Build(
            data.Graph, data.Layout, data.Routing, data.VisualModel, data.EditorState).Scene);
        var semantic = new SemanticModelSnapshot(scene.DocumentId, scene.SourceRevision,
            data.Graph.Nodes.Select(node => new SemanticElementSnapshot(node.Source.SemanticElementId,
                new("test:element"), [new("name", PropertyValue.FromText("duplicate name"))])),
            data.Graph.Edges.Select(edge => new SemanticRelationshipSnapshot(edge.Source.SemanticElementId,
                new("test:relationship"), data.Graph.Nodes[0].Source.SemanticElementId,
                data.Graph.Nodes[1].Source.SemanticElementId)));
        return (scene, new(semantic, data.VisualModel, new DocumentMetadataSnapshot(scene.DocumentId, scene.SourceRevision)));
    }

    private sealed class EmptyProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input) => Canvas2DDecorationContent.Empty;
    }

    private sealed class ManyMessageChildrenProvider(int count) : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            var part = context.Parts.Single(candidate => candidate.OwnerId.Value == "test:semantic:a" &&
                candidate.Kind == Canvas2DDecorationPartKind.NodeBody);
            return new(Children());

            IEnumerable<Canvas2DSceneItem> Children()
            {
                for (var index = 0; index < count; index++)
                {
                    var isImage = index % 2 == 0;
                    var key = FormattableString.Invariant($"message:{index / 2}:{(isImage ? "image" : "text")}");
                    var bounds = new RectD(part.Item.Bounds.Left + (isImage ? 0 : 16),
                        part.Item.Bounds.Bottom + 4 + index / 2 * 16, isImage ? 12 : 80, 12);
                    yield return new(Canvas2DSceneObjectIdentity.ForExtension(ProviderId, key),
                        Canvas2DSceneLayer.Decoration, index,
                        isImage ? Canvas2DSceneGeometry.Image(bounds, "example:shared-image") :
                            Canvas2DSceneGeometry.Text(bounds, "Waiting: 12"), part.CreateOrigin(key),
                        style: new(fill: "black", fontFamily: context.DefaultFontFamily));
                }
            }
        }
    }

    private sealed class InspectingProvider : ICanvas2DDecorationProvider
    {
        internal Canvas2DDecorationContext? Context { get; private set; }
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            Context = context;
            return Canvas2DDecorationContent.Empty;
        }
    }

    private sealed class ThrowingProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input) =>
            throw new InvalidOperationException("test provider failure");
    }

    private sealed class SemanticProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            var part = context.Parts.Single(part => part.Kind == Canvas2DDecorationPartKind.SemanticRegion);
            return new([new(Canvas2DSceneObjectIdentity.ForExtension(ProviderId, "region-annotation"),
                Canvas2DSceneLayer.Decoration, 0, Canvas2DSceneGeometry.Rectangle(new(100000, 100000, 100, 20)),
                part.CreateOrigin("region-annotation"))]);
        }
    }

    private sealed class AnnotationProvider(string mode = "valid") : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
        {
            // An explicit immutable native identity, not a label or arbitrary first match.
            var part = context.Parts.Single(part => part.OwnerId.Value == "test:semantic:a" && part.Kind == Canvas2DDecorationPartKind.NodeBody);
            var key = "annotation";
            var id = mode == "native-id" ? part.Item.Id : Canvas2DSceneObjectIdentity.ForExtension(
                mode == "wrong-namespace" ? new("wrong:namespace") : ProviderId, key);
            var bounds = new RectD(100000, 100000, 100, 20);
            var geometry = mode switch
            {
                "invalid-image" => Canvas2DSceneGeometry.Image(bounds, "missing-resource"),
                "invalid-font" => Canvas2DSceneGeometry.Text(bounds, "annotation"),
                _ => Canvas2DSceneGeometry.Rectangle(bounds),
            };
            var origin = mode == "unknown-origin" ? new Canvas2DSceneOriginTrace(
                Canvas2DSceneOriginCategory.RegisteredExtension, stableSourceKey: key, relatedSceneObjectIds: [new("missing")]) : part.CreateOrigin(key);
            if (mode == "forged-configuration-origin")
                origin = new(origin.Categories | Canvas2DSceneOriginCategory.Configuration, origin.SemanticElementId,
                    origin.VisualStateId, origin.ProjectedObjectId, origin.StableSourceKey,
                    origin.RelatedProjectedObjectIds, origin.RelatedSceneObjectIds);
            var item = new Canvas2DSceneItem(id, mode == "overlay" ? Canvas2DSceneLayer.Overlay : Canvas2DSceneLayer.Decoration,
                0, geometry, origin,
                clip: mode == "invalid-clip" ? new RectD(0, 0, double.PositiveInfinity, 1) : null,
                hitTestPolicy: mode == "interactive" ? new(Canvas2DHitTestMode.Bounds) : null,
                metadata: mode == "metadata" ? [Canvas2DSemanticSceneInteractionMetadata.Enabled] : null,
                persistentAppearance: mode == "appearance" ? [new("name", PropertyValue.FromText("bad"))] : null,
                bounds: mode == "invalid-bounds" ? new RectD(0, 0, 1, 1) : null);
            return new(mode == "duplicate" ? [item, item] : [item]);
        }
    }
}
