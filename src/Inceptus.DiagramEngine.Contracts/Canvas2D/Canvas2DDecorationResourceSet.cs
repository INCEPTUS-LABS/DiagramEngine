using System.Collections.Immutable;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>Copied immutable SVG resources belonging to the same revision as decoration values.</summary>
public sealed class Canvas2DDecorationResourceSet
{
    public Canvas2DDecorationResourceSet(IEnumerable<Canvas2DDecorationSvgResource> svgImages)
    {
        ArgumentNullException.ThrowIfNull(svgImages);
        var builder = ImmutableSortedDictionary.CreateBuilder<string, Canvas2DDecorationSvgResource>(StringComparer.Ordinal);
        foreach (var image in svgImages)
        {
            ArgumentNullException.ThrowIfNull(image, nameof(svgImages));
            if (builder.Count == 64)
                throw new ArgumentException("A decoration input can contain at most 64 SVG resources.", nameof(svgImages));
            if (!builder.TryAdd(image.ImageReference, image))
                throw new ArgumentException("Decoration image references must be unique.", nameof(svgImages));
        }
        SvgImages = builder.ToImmutable();
    }

    public static Canvas2DDecorationResourceSet Empty { get; } = new([]);
    public ImmutableSortedDictionary<string, Canvas2DDecorationSvgResource> SvgImages { get; }
}
