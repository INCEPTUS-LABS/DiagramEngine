using System.Diagnostics.CodeAnalysis;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Semantics;

namespace Inceptus.DiagramEngine.Contracts.ScopeNavigation;

/// <summary>
/// Supplies notation-owned scope navigation semantics for one semantic element type.
/// Generic presentation remains responsible for executing runtime navigation.
/// </summary>
public interface IScopeNavigationContribution
{
    /// <summary>
    /// Resolves the existing scope opened by <paramref name="ownerElement"/>.
    /// Implementations must not create or mutate scopes.
    /// </summary>
    bool TryResolveTargetScope(
        DocumentSnapshot document,
        SemanticElementSnapshot ownerElement,
        [NotNullWhen(true)] out DocumentScopeId? targetScopeId);

    /// <summary>
    /// Resolves a readable, current label for a scope-owning semantic element.
    /// </summary>
    string ResolveBreadcrumbLabel(
        DocumentSnapshot document,
        SemanticElementSnapshot ownerElement);
}
