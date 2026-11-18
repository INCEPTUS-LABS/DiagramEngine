using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>
/// One frozen application input. Values are copied at construction; submit a new input and
/// revision to change them. The provider must be stateless and must not read mutable application
/// dictionaries, changing closures, or live engine objects during composition.
/// </summary>
public sealed class Canvas2DDecorationInput
{
    public Canvas2DDecorationInput(Canvas2DSceneContributorId providerId,
        ICanvas2DDecorationProvider provider,
        IEnumerable<KeyValuePair<string, PropertyValue>>? values = null)
        : this(providerId, provider, values, Canvas2DDecorationResourceSet.Empty)
    {
    }

    /// <summary>Copies one content/resource revision; SVG validation precedes session acceptance.</summary>
    public Canvas2DDecorationInput(Canvas2DSceneContributorId providerId,
        ICanvas2DDecorationProvider provider,
        IEnumerable<KeyValuePair<string, PropertyValue>>? values,
        Canvas2DDecorationResourceSet resources)
    {
        ArgumentNullException.ThrowIfNull(providerId);
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(resources);
        ProviderId = providerId;
        Provider = provider;
        Values = new PropertyMap(values);
        Resources = resources;
    }

    public Canvas2DSceneContributorId ProviderId { get; }
    public ICanvas2DDecorationProvider Provider { get; }
    public PropertyMap Values { get; }
    public Canvas2DDecorationResourceSet Resources { get; }
}

/// <summary>
/// Deterministically composes passive primitives from one immutable input and one native base.
/// Implementations must be stateless, must not retain borrowed context objects, and must not
/// read changing closures or mutable application data. Use the supplied frozen values only.
/// </summary>
public interface ICanvas2DDecorationProvider
{
    Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input);
}
