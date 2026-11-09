using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.EditorState;
using Inceptus.DiagramEngine.Contracts.Layout;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Projection;
using Inceptus.DiagramEngine.Contracts.Profiles;
using Inceptus.DiagramEngine.Contracts.Routing;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

// Provenance of already composed content, never an independent presentation authority.
internal sealed record Canvas2DScenePanReuseSource(
    Canvas2DSceneBuilder Builder,
    DocumentSnapshot Document,
    DocumentScopeId ScopeId,
    ModelProfileViewStateSnapshot ProfileViewState,
    ModelProfileElementViewStateSnapshot ProfileElementViewState,
    ProjectedGraph Graph,
    LayoutResult Layout,
    RoutingResult Routing,
    EditorStateSnapshot EditorState);
