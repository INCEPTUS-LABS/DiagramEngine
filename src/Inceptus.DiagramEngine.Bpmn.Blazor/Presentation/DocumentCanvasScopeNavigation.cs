using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;

internal sealed record DocumentCanvasScopeNavigationContextAction(
    SemanticElementId OwnerSemanticElementId,
    SemanticTypeId SemanticTypeId,
    VisualStateId VisualStateId,
    DocumentScopeId TargetScopeId,
    string Label);

internal sealed record DocumentCanvasElementScopeNavigationRequest(
    long DocumentSessionVersion,
    DocumentId DocumentId,
    DocumentRevision Revision,
    DocumentScopeId SourceScopeId,
    DocumentCanvasScopeNavigationContextAction Action);

internal sealed record DocumentCanvasScopeBreadcrumbSegment(
    DocumentScopeId ScopeId,
    string Label,
    bool IsActive,
    string? LabelResourceKey = null);

internal static class DocumentCanvasScopeBreadcrumb
{
    internal const string RootLabel = "Main Process";
    internal const string FallbackNestedLabel = "Scope";

    internal static ImmutableArray<DocumentCanvasScopeBreadcrumbSegment> Empty => [];
}
