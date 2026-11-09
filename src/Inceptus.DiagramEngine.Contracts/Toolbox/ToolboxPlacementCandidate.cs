using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Properties;

namespace Inceptus.DiagramEngine.Contracts.Toolbox;

/// <summary>
/// Immutable notation-owned candidate selected from the current visible placement targets.
/// </summary>
public sealed class ToolboxPlacementCandidate
{
    public ToolboxPlacementCandidate(
        ToolboxPlacementTarget target,
        string feedbackKind,
        RectD previewBounds,
        IEnumerable<KeyValuePair<string, PropertyValue>>? properties = null,
        EditorFeedbackPresentationMode feedbackPresentationMode =
            EditorFeedbackPresentationMode.Default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(feedbackKind);
        if (!Enum.IsDefined(feedbackPresentationMode))
        {
            throw new ArgumentOutOfRangeException(
                nameof(feedbackPresentationMode),
                feedbackPresentationMode,
                "The editor-feedback presentation mode must be defined.");
        }

        if (previewBounds.IsEmpty)
        {
            throw new ArgumentException(
                "A Toolbox placement candidate requires non-empty preview bounds.",
                nameof(previewBounds));
        }

        Target = target;
        FeedbackKind = feedbackKind;
        PreviewBounds = previewBounds;
        Properties = new PropertyMap(properties);
        FeedbackPresentationMode = feedbackPresentationMode;
    }

    public ToolboxPlacementTarget Target { get; }

    public string FeedbackKind { get; }

    public RectD PreviewBounds { get; }

    public PropertyMap Properties { get; }

    public EditorFeedbackPresentationMode FeedbackPresentationMode { get; }
}
