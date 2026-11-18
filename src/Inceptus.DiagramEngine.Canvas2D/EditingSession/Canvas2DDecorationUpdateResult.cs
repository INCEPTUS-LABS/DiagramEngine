using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Presentation;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

/// <summary>Outcome of a versioned transient input submission, separate from input acceptance.</summary>
public enum Canvas2DDecorationUpdateStatus
{
    /// <summary>The requested input and native base were acknowledged by the renderer.</summary>
    Presented,
    /// <summary>The attachment/revision or submitted resource catalog is invalid. Nothing was accepted.</summary>
    Rejected,
    /// <summary>Admission is busy, the base changed, or the owner is unavailable. Inspect Accepted.</summary>
    Unavailable,
    /// <summary>Cancelled before acceptance; the preceding input remains unchanged.</summary>
    Cancelled,
    /// <summary>An accepted input was retired by a newer accepted revision.</summary>
    Superseded,
    /// <summary>Composition or rendering failed after acceptance. Inspect the frame observation.</summary>
    Failed,
}

/// <summary>Currency of an attempted frame. FramePresented requires a renderer acknowledgement.</summary>
public sealed record Canvas2DDecorationPresentation
{
    public Canvas2DDecorationPresentation(Guid attachmentId, DocumentId documentId,
        DocumentRevision documentRevision, PresentationViewContext presentationContext,
        long baseSceneGeneration, long inputRevision, bool framePresented, bool decorationsIncluded)
    {
        AttachmentId = attachmentId;
        DocumentId = documentId;
        DocumentRevision = documentRevision;
        PresentationContext = presentationContext;
        BaseSceneGeneration = baseSceneGeneration;
        InputRevision = inputRevision;
        FramePresented = framePresented;
        DecorationsIncluded = decorationsIncluded;
    }

    public Guid AttachmentId { get; }
    public DocumentId DocumentId { get; }
    public DocumentRevision DocumentRevision { get; }
    public PresentationViewContext PresentationContext { get; }
    public long BaseSceneGeneration { get; }
    public long InputRevision { get; }
    public bool FramePresented { get; }
    public bool DecorationsIncluded { get; }
}

/// <summary>
/// Acceptance commits the input revision even when composition or graphics subsequently fail.
/// A failed composition may acknowledge a native-only frame; a graphics failure acknowledges none.
/// </summary>
public sealed record Canvas2DDecorationUpdateResult
{
    public Canvas2DDecorationUpdateResult(Canvas2DDecorationUpdateStatus status, bool accepted,
        long inputRevision, Canvas2DDecorationPresentation? presentation, ImmutableArray<Diagnostic> diagnostics)
    {
        Status = status;
        Accepted = accepted;
        InputRevision = inputRevision;
        Presentation = presentation;
        Diagnostics = diagnostics.IsDefault ? [] : diagnostics;
    }

    public Canvas2DDecorationUpdateStatus Status { get; }
    public bool Accepted { get; }
    public long InputRevision { get; }
    public Canvas2DDecorationPresentation? Presentation { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}
