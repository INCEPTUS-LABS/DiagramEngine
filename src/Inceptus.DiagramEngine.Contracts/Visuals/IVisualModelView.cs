using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Primitives;
using Inceptus.DiagramEngine.Contracts.Profiles;

namespace Inceptus.DiagramEngine.Contracts.Visuals;

public interface IVisualModelView
{
    DocumentId DocumentId { get; }

    /// <summary>
    /// Gets the revision of the complete Document represented by this component view.
    /// </summary>
    DocumentRevision Revision { get; }

    int Count { get; }

    ImmutableArray<VisualStateSnapshot> VisualStates { get; }

    ImmutableArray<ModelProfileElementPresentationSnapshot> ProfileElementPresentations { get; }

    bool TryGetVisualState(VisualStateId id, out VisualStateSnapshot? visualState);
}
