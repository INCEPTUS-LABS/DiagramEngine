using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Presentation;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>Currency of one component attachment, view context and ordered selection.</summary>
public sealed record BpmnPresentationContextStamp
{
    internal BpmnPresentationContextStamp(Guid instanceId, long attachmentVersion,
        DocumentId documentId, DocumentRevision revision, PresentationViewContext presentationContext,
        long contextVersion, long selectionVersion)
    {
        InstanceId = instanceId;
        AttachmentVersion = attachmentVersion;
        DocumentId = documentId;
        Revision = revision;
        PresentationContext = presentationContext;
        ContextVersion = contextVersion;
        SelectionVersion = selectionVersion;
    }

    public Guid InstanceId { get; }
    public long AttachmentVersion { get; }
    public DocumentId DocumentId { get; }
    public DocumentRevision Revision { get; }
    public PresentationViewContext PresentationContext { get; }
    public long ContextVersion { get; }
    public long SelectionVersion { get; }
}

public enum BpmnSemanticTargetKind { Element, Relationship }

public enum BpmnPresentationPartKind { NodeBody, NodeLabel, ConnectorPath, ConnectorLabel, SemanticRegion }

/// <summary>
/// One visible interactive part. Scene and spatial identities are transient presentation details;
/// semantic identity remains on its owner. Bounds are final Scene document coordinates.
/// </summary>
public sealed record BpmnPresentationPart
{
    internal BpmnPresentationPart(SceneObjectId sceneObjectId, BpmnPresentationPartKind kind,
        RectD bounds, Canvas2DSpatialRegionId? spatialRegionId)
    {
        SceneObjectId = sceneObjectId;
        Kind = kind;
        Bounds = bounds;
        SpatialRegionId = spatialRegionId;
    }

    public SceneObjectId SceneObjectId { get; }
    public BpmnPresentationPartKind Kind { get; }
    public RectD Bounds { get; }
    public Canvas2DSpatialRegionId? SpatialRegionId { get; }
}

/// <summary>A selected semantic owner, retaining its element/relationship distinction.</summary>
public sealed class BpmnSemanticSelectionTarget
{
    internal BpmnSemanticSelectionTarget(SemanticElementId semanticId, BpmnSemanticTargetKind kind,
        VisualStateId? visualStateId, ImmutableArray<BpmnPresentationPart> parts)
    {
        SemanticId = semanticId;
        Kind = kind;
        VisualStateId = visualStateId;
        Parts = parts;
    }

    public SemanticElementId SemanticId { get; }
    public BpmnSemanticTargetKind Kind { get; }
    public VisualStateId? VisualStateId { get; }
    public ImmutableArray<BpmnPresentationPart> Parts { get; }
}

/// <summary>
/// Coherent immutable readback. Unavailable snapshots have no actionable stamp or targets.
/// PrimaryPart identifies the actual selected hit when known; no arbitrary part is substituted.
/// </summary>
public sealed class BpmnPresentationSnapshot
{
    internal BpmnPresentationSnapshot(BpmnPresentationContextStamp? stamp,
        ImmutableArray<BpmnSemanticSelectionTarget> selection,
        BpmnPresentationPart? primaryPart,
        ImmutableArray<DocumentScopeSnapshot> scopePath,
        ImmutableArray<DocumentScopeSnapshot> availableScopes,
        ImmutableArray<Diagnostic> diagnostics)
    {
        Stamp = stamp;
        Selection = selection;
        PrimaryPart = primaryPart;
        ScopePath = scopePath;
        AvailableScopes = availableScopes;
        Diagnostics = diagnostics;
    }

    public bool IsAvailable => Stamp is not null;
    public BpmnPresentationContextStamp? Stamp { get; }
    public DocumentScopeId? ActiveScopeId => (Stamp?.PresentationContext as ProcessScopePresentationContext)?.ScopeId;
    public ImmutableArray<BpmnSemanticSelectionTarget> Selection { get; }
    public BpmnPresentationPart? PrimaryPart { get; }
    public ImmutableArray<DocumentScopeSnapshot> ScopePath { get; }
    public ImmutableArray<DocumentScopeSnapshot> AvailableScopes { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    internal static BpmnPresentationSnapshot Unavailable { get; } = new(null, [], null, [], [], []);
}

public sealed class BpmnPresentationOperationResult
{
    internal BpmnPresentationOperationResult(BpmnModelerOperationStatus status,
        BpmnPresentationSnapshot presentation, ImmutableArray<Diagnostic> diagnostics)
    {
        Status = status;
        Presentation = presentation;
        Diagnostics = diagnostics;
    }

    public BpmnModelerOperationStatus Status { get; }
    public bool Succeeded => Status == BpmnModelerOperationStatus.Succeeded;
    public BpmnPresentationSnapshot Presentation { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }
}

public sealed class BpmnSelectionChangedEventArgs : EventArgs
{
    internal BpmnSelectionChangedEventArgs(BpmnPresentationSnapshot presentation) => Presentation = presentation;
    public BpmnPresentationSnapshot Presentation { get; }
}
