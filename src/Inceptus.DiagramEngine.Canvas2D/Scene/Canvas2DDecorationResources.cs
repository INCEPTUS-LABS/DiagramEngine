using System.Collections.Immutable;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

/// <summary>Immutable resource identities available to passive composition; no graphics capability.</summary>
internal sealed class Canvas2DDecorationResources
{
    internal Canvas2DDecorationResources(string? defaultFontFamily,
        IEnumerable<string> fontFamilies, IEnumerable<string> imageReferences)
    {
        ArgumentNullException.ThrowIfNull(fontFamilies);
        ArgumentNullException.ThrowIfNull(imageReferences);
        DefaultFontFamily = defaultFontFamily;
        FontFamilies = fontFamilies.ToImmutableHashSet(StringComparer.Ordinal);
        ImageReferences = imageReferences.ToImmutableHashSet(StringComparer.Ordinal);
    }

    internal string? DefaultFontFamily { get; }
    internal ImmutableHashSet<string> FontFamilies { get; }
    internal ImmutableHashSet<string> ImageReferences { get; }
}
