using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Inceptus.DiagramEngine.Contracts.Canvas2D;

namespace Inceptus.DiagramEngine.Canvas2D.Rendering.Interop;

// Immutable, private transport. Content identity is independent of a caller's reference name;
// attachment/input identity authorizes installation and is never a persistent model identity.
internal sealed record Canvas2DDecorationFrameResources(
    string AttachmentId,
    long InputRevision,
    string Identity,
    Canvas2DDecorationImageData[] Images)
{
    internal static Canvas2DDecorationFrameResources? Create(
        Canvas2DDecorationResourceSet resources, Guid attachmentId, long inputRevision)
    {
        if (resources.SvgImages.IsEmpty)
        {
            return null;
        }

        var images = resources.SvgImages.Select(static entry => new Canvas2DDecorationImageData(
            entry.Key,
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entry.Value.SvgContent))),
            entry.Value.SvgContent)).ToArray();
        var manifest = new StringBuilder("static-svg-v1;");
        foreach (var image in images)
        {
            manifest.Append(image.Reference.Length.ToString(CultureInfo.InvariantCulture))
                .Append(':').Append(image.Reference).Append(':').Append(image.ContentIdentity).Append(';');
        }
        var attachment = attachmentId.ToString("N");
        var identity = attachment + ":" + inputRevision.ToString(CultureInfo.InvariantCulture) + ":" +
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.ToString())));
        return new(attachment, inputRevision, identity, images);
    }
}

internal sealed record Canvas2DDecorationImageData(string Reference, string ContentIdentity, string SvgContent);
