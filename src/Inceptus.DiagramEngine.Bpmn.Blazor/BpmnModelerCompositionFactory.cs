using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Bpmn.Blazor.Presentation;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Contracts.Toolbox;

namespace Inceptus.DiagramEngine.Bpmn.Blazor.Composition;

/// <summary>
/// Creates one independently owned initial Document and its reusable composition per modeler.
/// </summary>
internal sealed class BpmnModelerCompositionFactory : IDocumentCanvasCompositionFactory
{
    private readonly ImmutableArray<IBpmnModelerStartupDocumentProvider> _startupProviders;
    private readonly Func<IEnumerable<IBpmnModelerStartupDocumentProvider>>? _resolveStartupProviders;
    private readonly DocumentSnapshot? _initialDocument;

    internal BpmnModelerCompositionFactory(
        IEnumerable<IBpmnModelerStartupDocumentProvider>? startupProviders = null,
        DocumentSnapshot? initialDocument = null,
        EditingSessionAccessMode accessMode = EditingSessionAccessMode.Editable)
    {
        _startupProviders = CopyStartupProviders(startupProviders);
        _initialDocument = initialDocument;
        if (!Enum.IsDefined(accessMode)) throw new ArgumentOutOfRangeException(nameof(accessMode));
        AccessMode = accessMode;
    }

    internal BpmnModelerCompositionFactory(
        Func<IEnumerable<IBpmnModelerStartupDocumentProvider>> resolveStartupProviders,
        DocumentSnapshot? initialDocument = null,
        EditingSessionAccessMode accessMode = EditingSessionAccessMode.Editable)
        : this(initialDocument: initialDocument, accessMode: accessMode)
    {
        ArgumentNullException.ThrowIfNull(resolveStartupProviders);
        _resolveStartupProviders = resolveStartupProviders;
    }

    internal static ToolboxCatalog ToolboxCatalog => BpmnModelerComposition.ToolboxCatalog;

    internal EditingSessionAccessMode AccessMode { get; }

    internal BpmnModelerCompositionFactory WithInitialDocument(DocumentSnapshot? initialDocument) =>
        _resolveStartupProviders is null
            ? new(_startupProviders, initialDocument, AccessMode)
            : new(_resolveStartupProviders, initialDocument, AccessMode);

    internal BpmnModelerCompositionFactory WithAccessMode(EditingSessionAccessMode accessMode) =>
        _resolveStartupProviders is null
            ? new(_startupProviders, _initialDocument, accessMode)
            : new(_resolveStartupProviders, _initialDocument, accessMode);

    public async ValueTask<DocumentCanvasComposition> CreateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_initialDocument is not null)
        {
            var reconstruction = BpmnModelerComposition.ReconstructDocument(_initialDocument);
            if (!reconstruction.Succeeded)
            {
                throw new InvalidOperationException(
                    "The initial Document snapshot is not valid for the BPMN modeler.");
            }

            return BpmnModelerComposition.Create(reconstruction.Document!, AccessMode);
        }

        var startupProviders = _resolveStartupProviders is null
            ? _startupProviders
            : CopyStartupProviders(_resolveStartupProviders() ?? throw new InvalidOperationException(
                "The BPMN modeler startup Document providers could not be resolved."));
        var document = startupProviders.Length switch
        {
            0 => BpmnModelerComposition.CreateEmptyDocument(),
            1 => await startupProviders[0]
                .GetInitialDocumentAsync(cancellationToken)
                .ConfigureAwait(false),
            _ => throw new InvalidOperationException(
                "Only one BPMN modeler startup Document provider may be registered."),
        };

        cancellationToken.ThrowIfCancellationRequested();

        return BpmnModelerComposition.Create(document ?? throw new InvalidOperationException(
            "The BPMN modeler startup Document provider returned no Document."), AccessMode);
    }

    private static ImmutableArray<IBpmnModelerStartupDocumentProvider> CopyStartupProviders(
        IEnumerable<IBpmnModelerStartupDocumentProvider>? startupProviders)
    {
        var providers = startupProviders?.ToImmutableArray() ?? [];
        if (providers.Any(static provider => provider is null))
        {
            throw new ArgumentException(
                "Startup Document providers cannot contain null values.",
                nameof(startupProviders));
        }

        return providers;
    }
}
