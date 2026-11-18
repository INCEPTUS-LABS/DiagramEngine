using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>
/// An opaque handle to one accepted modeler attachment. Native revision, selection and view
/// changes do not retire it; a successful source replacement or component disposal does.
/// </summary>
public sealed record BpmnDecorationAttachment
{
    internal BpmnDecorationAttachment(Guid instanceId, long attachmentVersion,
        DocumentId documentId, Guid sessionAttachmentId)
    {
        InstanceId = instanceId;
        AttachmentVersion = attachmentVersion;
        DocumentId = documentId;
        SessionAttachmentId = sessionAttachmentId;
    }

    public Guid InstanceId { get; }
    public long AttachmentVersion { get; }
    public DocumentId DocumentId { get; }
    internal Guid SessionAttachmentId { get; }
}
