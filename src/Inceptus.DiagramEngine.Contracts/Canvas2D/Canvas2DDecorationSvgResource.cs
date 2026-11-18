namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>
/// Frozen SVG image content for a transient decoration input. Content is validated against
/// the supported static SVG profile before input acceptance; it is never inserted into DOM.
/// </summary>
public sealed class Canvas2DDecorationSvgResource
{
    public Canvas2DDecorationSvgResource(string imageReference, string svgContent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);
        ArgumentException.ThrowIfNullOrWhiteSpace(svgContent);
        var separator = imageReference.IndexOf(':', StringComparison.Ordinal);
        if (imageReference.Length > 256 || separator <= 0 || separator == imageReference.Length - 1 ||
            imageReference.Any(static character => char.IsWhiteSpace(character) || char.IsControl(character)))
            throw new ArgumentException("A decoration image reference must be namespaced, without whitespace or control characters, and at most 256 characters.", nameof(imageReference));
        ImageReference = imageReference;
        SvgContent = svgContent;
    }

    public string ImageReference { get; }
    public string SvgContent { get; }
}
