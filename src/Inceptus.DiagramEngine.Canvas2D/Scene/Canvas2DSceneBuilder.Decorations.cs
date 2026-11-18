using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

public sealed partial class Canvas2DSceneBuilder
{
    // The transient lane shares primitive validation without entering contributor provenance,
    // saved geometry preparation or the native scene build pipeline.
    internal static void ValidateDecorationPrimitive(Canvas2DSceneItem item, List<Diagnostic> diagnostics) =>
        ValidateItem(item, diagnostics);
}
