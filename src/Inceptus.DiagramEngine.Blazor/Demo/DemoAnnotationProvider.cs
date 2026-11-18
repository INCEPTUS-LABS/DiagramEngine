using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Stateless application example; all message values arrive in the copied input map.</summary>
internal sealed class DemoAnnotationProvider : ICanvas2DDecorationProvider
{
    private static readonly Canvas2DSceneContributorId ProviderId = new("example:annotations");
    private static readonly DemoAnnotationProvider Instance = new();
    internal const string SvgResourceName = "Inceptus.DiagramEngine.Blazor.Demo.annotation-status.svg";

    public static Canvas2DDecorationInput CreateInput(string text, bool demonstrateFailure = false) =>
        CreateInput(string.IsNullOrWhiteSpace(text) ? [] : [new DemoAnnotationMessage("note", text, false, 0)],
            svgContent: null, demonstrateFailure);

    internal static ImmutableArray<DemoAnnotationMessage> CreateMixedMessages(string text) =>
    [new("text-note", text, false, 0), new("status-note", "Status ready", true, 1), new("status-only", null, true, 2)];

    internal static Canvas2DDecorationInput CreateInput(IEnumerable<DemoAnnotationMessage> messages,
        string? svgContent, bool demonstrateFailure = false)
    {
        ArgumentNullException.ThrowIfNull(messages);
        var frozen = messages.OrderBy(message => message.Order).ThenBy(message => message.Id, StringComparer.Ordinal).ToImmutableArray();
        if (frozen.Length > Canvas2DDecorationContent.MaximumItems)
            throw new ArgumentException("The example message collection exceeds its primitive budget.", nameof(messages));
        var identities = new HashSet<string>(StringComparer.Ordinal);
        var values = new List<KeyValuePair<string, PropertyValue>>
        {
            new("count", PropertyValue.FromInteger(frozen.Length)),
            new("fail", PropertyValue.FromBoolean(demonstrateFailure)),
        };
        var resources = new List<Canvas2DDecorationSvgResource>();
        for (var index = 0; index < frozen.Length; index++)
        {
            var message = frozen[index];
            ArgumentException.ThrowIfNullOrWhiteSpace(message.Id);
            if (!identities.Add(message.Id)) throw new ArgumentException("Example message IDs must be unique.", nameof(messages));
            if (message.Text is null && !message.HasImage)
                throw new ArgumentException("An example message needs text, an SVG image, or both.", nameof(messages));
            var prefix = "message:" + index.ToString(CultureInfo.InvariantCulture) + ":";
            values.Add(new(prefix + "id", PropertyValue.FromText(message.Id)));
            values.Add(new(prefix + "has-text", PropertyValue.FromBoolean(message.Text is not null)));
            values.Add(new(prefix + "text", PropertyValue.FromText(message.Text ?? string.Empty)));
            values.Add(new(prefix + "has-image", PropertyValue.FromBoolean(message.HasImage)));
            var reference = "example:annotations:svg:" + EncodeKey(message.Id);
            values.Add(new(prefix + "image", PropertyValue.FromText(reference)));
            if (message.HasImage)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(svgContent);
                var artwork = message.AlternateArtwork
                    ? svgContent.Replace("#0891b2", "#c2410c", StringComparison.Ordinal)
                    : svgContent;
                resources.Add(new(reference, artwork));
            }
        }
        return new(ProviderId, Instance, values, new Canvas2DDecorationResourceSet(resources));
    }

    internal static async Task<string> ReadSvgAsync(CancellationToken cancellationToken = default)
    {
        await using var resource = typeof(DemoAnnotationProvider).Assembly.GetManifestResourceStream(SvgResourceName)
            ?? throw new InvalidOperationException("The embedded annotation SVG example is unavailable.");
        using var reader = new StreamReader(resource);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }

    public Canvas2DDecorationContent Compose(Canvas2DDecorationContext context, PropertyMap input)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        if (input["fail"].BooleanValue) throw new InvalidOperationException("Requested annotation example failure.");
        var count = checked((int)input["count"].IntegerValue);
        if (count < 0 || count > Canvas2DDecorationContent.MaximumItems)
            throw new InvalidOperationException("The example message count is invalid.");
        var items = new List<Canvas2DSceneItem>();
        var omitted = 0;
        foreach (var ownerId in context.Parts.Where(part => part.OwnerKind == Canvas2DDecorationOwnerKind.Element &&
                     part.Kind == Canvas2DDecorationPartKind.NodeBody).Select(part => part.OwnerId)
                     .Distinct().OrderBy(id => id.Value, StringComparer.Ordinal))
        {
            var matches = context.FindParts(ownerId, Canvas2DDecorationOwnerKind.Element, Canvas2DDecorationPartKind.NodeBody);
            if (matches.Length != 1) { omitted++; continue; }
            var part = matches[0]; // Exactly one representation, never an arbitrary first match.
            for (var index = 0; index < count; index++)
            {
                var prefix = "message:" + index.ToString(CultureInfo.InvariantCulture) + ":";
                var messageId = input[prefix + "id"].TextValue;
                var hasText = input[prefix + "has-text"].BooleanValue;
                var hasImage = input[prefix + "has-image"].BooleanValue;
                var required = (hasText ? 1 : 0) + (hasImage ? 1 : 0);
                if (items.Count + required > Canvas2DDecorationContent.MaximumItems)
                    throw new InvalidOperationException("The complete example exceeds its primitive budget.");
                var x = part.Item.Bounds.Left;
                var y = part.Item.Bounds.Bottom + 5 + index * 28;
                if (hasImage)
                {
                    var imageBounds = new RectD(x, y + 2, 20, 20);
                    Add("image", Canvas2DSceneGeometry.Image(imageBounds, input[prefix + "image"].TextValue), imageBounds, index * 2);
                }
                if (hasText)
                {
                    var textBounds = new RectD(x + (hasImage ? 26 : 0), y, 180, 24);
                    Add("text", Canvas2DSceneGeometry.Text(textBounds, input[prefix + "text"].TextValue,
                        new(textBounds.Left, textBounds.Top + 12), Canvas2DTextAlignment.Start, Canvas2DTextBaseline.Middle),
                        textBounds, index * 2 + 1);
                }

                void Add(string role, Canvas2DSceneGeometry geometry, RectD clip, int zIndex)
                {
                    var key = EncodeKey(part.OwnerKind.ToString(), ownerId.Value, part.Item.Id.Value, messageId, role);
                    items.Add(new(Canvas2DSceneObjectIdentity.ForExtension(ProviderId, key),
                        Canvas2DSceneLayer.Decoration, zIndex, geometry, part.CreateOrigin(key), clip: clip,
                        style: new(fill: "#155e75", fontFamily: context.DefaultFontFamily, fontSize: 12),
                        hitTestPolicy: Canvas2DHitTestPolicy.None));
                }
            }
        }
        return new(items, omitted == 0 ? [] :
            [new Diagnostic("DEMO_ANNOTATION_AMBIGUOUS", DiagnosticSeverity.Warning,
                $"Omitted {omitted} owners with multiple visible body representations.")]);
    }

    private static string EncodeKey(params string[] segments) =>
        string.Concat(segments.Select(value => value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value));
}

internal sealed record DemoAnnotationMessage(string Id, string? Text, bool HasImage, int Order, bool AlternateArtwork = false);
