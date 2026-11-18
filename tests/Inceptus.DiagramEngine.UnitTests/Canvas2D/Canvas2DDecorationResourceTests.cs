using System.Globalization;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.UnitTests.Canvas2D;

public sealed class Canvas2DDecorationResourceTests
{
    private const string Namespace = "http://www.w3.org/2000/svg";
    private const string Reference = "example:status";

    [Fact]
    public void InputPreservesOriginalConstructorAndCopiesOneImmutableContentResourceRevision()
    {
        var original = typeof(Canvas2DDecorationInput).GetConstructor(
            [typeof(Canvas2DSceneContributorId), typeof(ICanvas2DDecorationProvider),
                typeof(IEnumerable<KeyValuePair<string, PropertyValue>>)]);
        Assert.NotNull(original);
        Assert.True(original.GetParameters()[2].HasDefaultValue);
        Assert.Null(original.GetParameters()[2].DefaultValue);
        var values = new Dictionary<string, PropertyValue> { ["text"] = PropertyValue.FromText("first") };
        var resource = new Canvas2DDecorationSvgResource(Reference, Svg("<rect width=\"24\" height=\"24\" fill=\"red\"/>"));
        var entries = new List<Canvas2DDecorationSvgResource> { resource };
        var resources = new Canvas2DDecorationResourceSet(entries);
        var input = new Canvas2DDecorationInput(new("example:annotations"), new EmptyProvider(), values, resources);
        entries.Clear();
        values["text"] = PropertyValue.FromText("later");
        Assert.Equal("first", input.Values["text"].TextValue);
        Assert.Same(resource, Assert.Single(input.Resources.SvgImages).Value);
        Assert.Same(Canvas2DDecorationResourceSet.Empty,
            new Canvas2DDecorationInput(new("example:annotations"), new EmptyProvider()).Resources);
        var changed = new Canvas2DDecorationResourceSet([new(Reference, Svg("<circle r=\"12\"/>"))]);
        Assert.NotEqual(changed.SvgImages[Reference].SvgContent, input.Resources.SvgImages[Reference].SvgContent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unqualified")]
    [InlineData(":empty")]
    [InlineData("empty:")]
    [InlineData("example:bad key")]
    [InlineData("example:bad\nkey")]
    public void ResourceRejectsInvalidReferenceBeforeSubmission(string reference) =>
        Assert.ThrowsAny<ArgumentException>(() => new Canvas2DDecorationSvgResource(reference, Svg()));

    [Fact]
    public void ResourceSetRejectsNullDuplicateAndUnboundedEnumerationBeforeSubmission()
    {
        Assert.Throws<ArgumentNullException>(() => new Canvas2DDecorationSvgResource(null!, Svg()));
        Assert.Throws<ArgumentNullException>(() => new Canvas2DDecorationSvgResource(Reference, null!));
        Assert.Throws<ArgumentException>(() => new Canvas2DDecorationSvgResource(new string('a', 255) + ":b", Svg()));
        Assert.Throws<ArgumentNullException>(() => new Canvas2DDecorationResourceSet(null!));
        Assert.Throws<ArgumentNullException>(() => new Canvas2DDecorationResourceSet([null!]));
        Assert.Throws<ArgumentException>(() => new Canvas2DDecorationResourceSet([new(Reference, Svg()), new(Reference, Svg())]));
        Assert.Throws<ArgumentNullException>(() => new Canvas2DDecorationInput(new("example:provider"), new EmptyProvider(), null, null!));
        var enumerated = 0;
        Assert.Throws<ArgumentException>(() => new Canvas2DDecorationResourceSet(Images()));
        Assert.Equal(65, enumerated);

        IEnumerable<Canvas2DDecorationSvgResource> Images()
        {
            while (true) yield return new($"example:{++enumerated}", Svg());
        }
    }

    [Fact]
    public void ResourceSetSortsOrdinallyAndAllowsTheExactResourceCountBoundary()
    {
        var resources = new Canvas2DDecorationResourceSet(Enumerable.Range(0, 64).Reverse()
            .Select(index => new Canvas2DDecorationSvgResource($"example:{index:D2}", Svg())));
        Assert.Equal("example:00", resources.SvgImages.First().Key);
        Assert.Equal("example:63", resources.SvgImages.Last().Key);
        Assert.Empty(Validate(resources));
    }

    [Theory]
    [InlineData("<g fill=\"#2e7d32\" stroke=\"black\" stroke-width=\".5\" opacity=\".8\"><rect x=\"1\" y=\"2\" width=\"20\" height=\"18\" rx=\"2\" ry=\"3\"/></g>")]
    [InlineData("<circle cx=\"12\" cy=\"12\" r=\"5\"/><ellipse cx=\"12\" cy=\"12\" rx=\"7\" ry=\"3\"/>")]
    [InlineData("<line x1=\"0\" y1=\"0\" x2=\"24\" y2=\"24\" stroke=\"rebeccapurple\" stroke-linecap=\"round\" stroke-linejoin=\"bevel\" stroke-miterlimit=\"2\" stroke-dasharray=\"1, 2\" stroke-dashoffset=\"-1\"/>")]
    [InlineData("<polyline points=\"0,0 12,12 24,0\" fill=\"none\"/><polygon points=\"0,0 24,0 12,24\" fill=\"#abc8\" fill-rule=\"evenodd\" fill-opacity=\".5\" stroke-opacity=\"1\"/>")]
    [InlineData("<path d=\"M0 0 L24 0 H12 V12 C12 13 13 14 14 14 S15 15 16 16 Q17 17 18 18 T20 20 A2 3 45 0 1 24 24 Z\"/>")]
    [InlineData("<path d=\"m0,0 1,1 l1-1 h2 v2 c1 1 2 2 3 3 s2 2 3 3 q1 1 2 2 t1 1 a2 3 0 1 0 3 3 z\"/>")]
    [InlineData("<g transform=\"translate(1, 2) scale(.5) rotate(45 12 12) skewX(5) skewY(-5) matrix(1 0 0 1 0 0)\"><path d=\"M.1 .2 L1e1 2E+1\"/></g>")]
    public void SupportedStaticVectorElementsAndNumbersAreAccepted(string child) => Assert.Empty(Validate(Svg(child)));

    [Fact]
    public void ViewBoxCoordinatesAreNotMistakenForIntrinsicPixelsAndParsingIsInvariant()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            Assert.Empty(Validate($"<svg xmlns=\"{Namespace}\" width=\"24px\" height=\"24px\" viewBox=\"-1000000 -2.5 1000000 1000000\" preserveAspectRatio=\"xMidYMid meet\"><path d=\"M0 0 L.5 1.5\"/></svg>"));
        }
        finally { CultureInfo.CurrentCulture = previous; }
    }

    [Theory]
    [InlineData("<script>alert(1)</script>")]
    [InlineData("<foreignObject><div/></foreignObject>")]
    [InlineData("<style>path{fill:red}</style>")]
    [InlineData("<text>Waiting: 12</text>")]
    [InlineData("<image href=\"data:image/png;base64,AA==\"/>")]
    [InlineData("<use href=\"#a\"/>")]
    [InlineData("<defs><linearGradient id=\"a\"/></defs>")]
    [InlineData("<animate attributeName=\"opacity\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" onclick=\"alert(1)\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" style=\"fill:red\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" fill=\"url(https://example.invalid/a)\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" fill=\"currentColor\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" filter=\"url(#a)\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\" font-family=\"remote\"/>")]
    [InlineData("<rect width=\"1\" height=\"1\"/><svg/>")]
    [InlineData("<rect width=\"1\" height=\"1\"><circle r=\"1\"/></rect>")]
    [InlineData("<g xmlns:xlink=\"http://www.w3.org/1999/xlink\"/>")]
    [InlineData("<g xml:base=\"https://example.invalid/\"/>")]
    [InlineData("<![CDATA[<script/>]]>")]
    [InlineData("<?xml-stylesheet href=\"https://example.invalid/a\"?>")]
    public void ActiveExternalAndUnsupportedArtworkIsRejectedWithoutEchoingPayload(string child)
    {
        var diagnostic = Assert.Single(Validate(Svg(child)));
        Assert.Equal("CANVAS2D_DECORATION_RESOURCE_INVALID", diagnostic.Code);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Reference, diagnostic.SourceIdentity);
        Assert.DoesNotContain(child, diagnostic.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("<!DOCTYPE svg [<!ENTITY e SYSTEM 'https://example.invalid/private'>]><svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'>&e;</svg>")]
    [InlineData("<!DOCTYPE svg [<!ENTITY e 'expanded'>]><svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24'>&e;</svg>")]
    [InlineData("<svg xmlns='urn:wrong' width='24' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg width='24' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='100%' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='24 px' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='\u00a024' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='0' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='1025' height='24' viewBox='0 0 24 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 0 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 1000001 24'/>")]
    [InlineData("<svg xmlns='http://www.w3.org/2000/svg' width='24' height='24' viewBox='0 0 24 24' preserveAspectRatio='defer xMidYMid meet'/>")]
    public void DocumentNamespacesEntitiesAndRootDimensionsAreStrict(string svg) => Assert.NotEmpty(Validate(svg));

    [Theory]
    [InlineData("<path d=\"L0 0\"/>")]
    [InlineData("<path d=\"M0 0 L1\"/>")]
    [InlineData("<path d=\"M0 0 A-1 1 0 0 1 2 2\"/>")]
    [InlineData("<path d=\"M0 0 A1 1 0 2 1 2 2\"/>")]
    [InlineData("<path d=\"M0 0 A1 1 0 1.0 1 2 2\"/>")]
    [InlineData("<path d=\"M0 0 A1 1 0 1e0 1 2 2\"/>")]
    [InlineData("<path d=\"M0 0 R1 1\"/>")]
    [InlineData("<path d=\"M0,,0\"/>")]
    [InlineData("<path d=\"M,0 0\"/>")]
    [InlineData("<path d=\"M0 0,\"/>")]
    [InlineData("<path d=\"M0 0 Z1 1\"/>")]
    [InlineData("<path d=\"M0 0 L1e309 1\"/>")]
    [InlineData("<circle r=\"NaN\"/>")]
    [InlineData("<circle r=\"Infinity\"/>")]
    [InlineData("<circle r=\"-1\"/>")]
    [InlineData("<polyline points=\"0 0 1\"/>")]
    [InlineData("<polygon points=\"0 0 1 1\"/>")]
    [InlineData("<g opacity=\"1.1\"/>")]
    [InlineData("<g transform=\"matrix(1 0 0)\"/>")]
    [InlineData("<g transform=\"translate(1), \"/>")]
    [InlineData("<g transform=\"rotate(NaN)\"/>")]
    [InlineData("<g transform=\"scale(1000001)\"/>")]
    public void MalformedOrNonfiniteGeometryIsRejected(string child) => Assert.NotEmpty(Validate(Svg(child)));

    [Fact]
    public void PayloadLimitsCountUtf8BytesAndWholeCatalogAtExactBoundaries()
    {
        var exact = PaddedSvg(Canvas2DDecorationResourceValidator.MaximumResourceBytes);
        Assert.Empty(Validate(exact));
        AssertLimit(Validate(exact + " "));
        AssertLimit(Validate(Svg("<!--" + new string('\u00e9', 140000) + "-->")));
        var eight = new Canvas2DDecorationResourceSet(Enumerable.Range(0, 8)
            .Select(index => new Canvas2DDecorationSvgResource($"example:{index}", exact)));
        Assert.Empty(Validate(eight));
        AssertLimit(Validate(new Canvas2DDecorationResourceSet(eight.SvgImages.Values.Append(new("example:extra", Svg())))));
    }

    [Fact]
    public void IntrinsicDecodedPixelBudgetCountsAllResourcesAndAllowsExactBoundary()
    {
        var eight = new Canvas2DDecorationResourceSet(Enumerable.Range(0, 8)
            .Select(index => new Canvas2DDecorationSvgResource($"example:{index}", Svg(width: 1000, height: 1000))));
        Assert.Empty(Validate(eight));
        AssertLimit(Validate(new Canvas2DDecorationResourceSet(eight.SvgImages.Values.Append(new("example:extra", Svg(width: 1, height: 1))))));
        Assert.Empty(Validate(Svg(width: 1024, height: 1024)));
    }

    [Fact]
    public void FractionalIntrinsicDimensionsUseRoundedPixelAreaForTheCatalogBudget()
    {
        var svg = $"<svg xmlns=\"{Namespace}\" width=\"499.99px\" height=\"999.99\" viewBox=\"0 0 24 24\"/>";
        var exact = new Canvas2DDecorationResourceSet(Enumerable.Range(0, 16)
            .Select(index => new Canvas2DDecorationSvgResource($"example:{index}", svg)));
        // Each dimension rounds up: 16 * 500 * 1,000 is the exact catalog limit.
        Assert.Empty(Validate(exact));
        // Floating-point area alone would incorrectly admit this additional image.
        Assert.True(16 * 499.99 * 999.99 + 1 < Canvas2DDecorationResourceValidator.MaximumDecodedArea);
        AssertLimit(Validate(new Canvas2DDecorationResourceSet(
            exact.SvgImages.Values.Append(new("example:extra", Svg(width: 1, height: 1))))));
    }

    [Fact]
    public void ElementDepthPathTransformAndNumericComplexityAreBounded()
    {
        const string rectangle = "<rect width=\"1\" height=\"1\"/>";
        Assert.Empty(Validate(Svg(string.Concat(Enumerable.Repeat(rectangle, 4095)))));
        AssertLimit(Validate(Svg(string.Concat(Enumerable.Repeat(rectangle, 4096)))));
        Assert.Empty(Validate(Svg(string.Concat(Enumerable.Repeat("<g>", 32)) + string.Concat(Enumerable.Repeat("</g>", 32)))));
        AssertLimit(Validate(Svg(string.Concat(Enumerable.Repeat("<g>", 33)) + string.Concat(Enumerable.Repeat("</g>", 33)))));
        var path = "M0 0 " + string.Concat(Enumerable.Repeat("L1 1 ", 4095));
        Assert.Empty(Validate(Svg($"<path d=\"{path}\"/>")));
        AssertLimit(Validate(Svg($"<path d=\"{path}L1 1\"/>")));
        var transforms = string.Concat(Enumerable.Repeat("translate(0 0) ", 32));
        Assert.Empty(Validate(Svg($"<g transform=\"{transforms}\"/>")));
        AssertLimit(Validate(Svg($"<g transform=\"{transforms}translate(0 0)\"/>")));
        var points = string.Concat(Enumerable.Repeat("0 0 ", 16381)); // 32,762 + six root numbers.
        Assert.Empty(Validate(Svg($"<polyline points=\"{points}\"/>")));
        AssertLimit(Validate(Svg($"<polyline points=\"{points}0 0\"/>")));
    }

    [Fact]
    public void NativeResourceReferencesCannotBeOverriddenAndEmptyInputRemainsCompatible()
    {
        Assert.Empty(Validate(Canvas2DDecorationResourceSet.Empty));
        var resources = new Canvas2DDecorationResourceSet([new(Reference, Svg())]);
        var diagnostic = Assert.Single(Canvas2DDecorationResourceValidator.Validate(resources, [Reference]));
        Assert.Equal("CANVAS2D_DECORATION_RESOURCE_COLLISION", diagnostic.Code);
        Assert.Empty(Canvas2DDecorationResourceValidator.Validate(resources, ["Example:status"]));
    }

    private static string Svg(string children = "", int width = 24, int height = 24) =>
        FormattableString.Invariant($"<svg xmlns=\"{Namespace}\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 24 24\">{children}</svg>");

    private static string PaddedSvg(int byteCount)
    {
        var empty = Svg("<!---->");
        return Svg("<!--" + new string(' ', byteCount - empty.Length) + "-->");
    }

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Validate(string svg) =>
        Validate(new Canvas2DDecorationResourceSet([new(Reference, svg)]));

    private static System.Collections.Immutable.ImmutableArray<Diagnostic> Validate(Canvas2DDecorationResourceSet resources) =>
        Canvas2DDecorationResourceValidator.Validate(resources, []);

    private static void AssertLimit(System.Collections.Immutable.ImmutableArray<Diagnostic> diagnostics) =>
        Assert.Equal("CANVAS2D_DECORATION_RESOURCE_LIMIT", Assert.Single(diagnostics).Code);

    private sealed class EmptyProvider : ICanvas2DDecorationProvider
    {
        public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input) => Canvas2DDecorationContent.Empty;
    }
}
