using System.Collections.Immutable;
using Inceptus.DiagramEngine.Canvas2D.Rendering;
using Inceptus.DiagramEngine.Canvas2D.Scene;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Documents;

namespace Inceptus.DiagramEngine.Canvas2D.EditingSession;

public sealed partial class EditingSession
{
    internal LifecycleObservation? LifecycleObservation { get; set; }
    private Canvas2DDecorationInput? _decorationInput;
    private long _decorationInputRevision;
    private int _decorationUpdateActive;
    private Canvas2DScene? _decorationBaseScene;
    private long _decorationBaseGeneration;
    private Canvas2DDecorationComposition? _decorationComposition;
    private Canvas2DDecorationPresentation? _decorationPresentation;
    private ImmutableArray<Diagnostic> _decorationDiagnostics = [];
    // A failed resource belongs to the accepted input, not its current native base. Native
    // recomposition must not restart a failed decode on every pan, scope or surface update.
    private ImmutableArray<Diagnostic> _decorationResourceFailure = [];

    /// <summary>Unique to this attachment, including replacements with identical native IDs.</summary>
    public Guid DecorationAttachmentId { get; } = Guid.NewGuid();

    /// <summary>
    /// Replaces frozen transient input without executing the native pipeline. Null is a versioned
    /// clear. Revisions must be positive and strictly increasing. Only one submission is admitted;
    /// overlapping calls return Unavailable without accepting or queueing input.
    /// Caller cancellation applies before acceptance. The optional retirement token belongs to
    /// the owning host's lifetime, and prevents late output after that owner has been retired.
    /// </summary>
    public async ValueTask<Canvas2DDecorationUpdateResult> ReplaceDecorationsAsync(
        long inputRevision,
        Canvas2DDecorationInput? input,
        CancellationToken cancellationToken = default,
        CancellationToken retirementToken = default)
    {
        if (Interlocked.CompareExchange(ref _decorationUpdateActive, 1, 0) != 0)
            return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, false, inputRevision);

        var entered = false;
        var accepted = false;
        try
        {
            CancellationToken lifetime;
            lock (_sync)
            {
                if (_closing || _closed)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, false, inputRevision);
                lifetime = _lifetime.Token;
            }
            // Cancellation is an admission concern. After acceptance the renderer completes its
            // atomic frame dispatch; cancellation never pretends the accepted input rolled back.
            using var presentationLifetime = CancellationTokenSource.CreateLinkedTokenSource(lifetime, retirementToken);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, presentationLifetime.Token);
            await _rendererGate.WaitAsync(linked.Token).ConfigureAwait(false);
            entered = true;
            Canvas2DScene scene;
            EditingSessionGeneration generation;
            lock (_sync)
            {
                if (linked.IsCancellationRequested)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Cancelled, false, inputRevision);
                if (_closing || _closed || _status != EditingSessionStatus.Ready ||
                    _currentScene is null || _document?.Revision != _currentScene.SourceRevision)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, false, inputRevision);
                if (inputRevision <= _decorationInputRevision)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Rejected, false, inputRevision);
            }

            // Bounded validation uses no callbacks or native mutation and runs outside the
            // session lock. Invalid resources never retire the previously accepted input.
            var resourceDiagnostics = input is null ? [] :
                Canvas2DDecorationResourceValidator.Validate(input.Resources, _renderer.Configuration.ImageResources.Keys);
            if (linked.IsCancellationRequested)
                return DecorationResult(Canvas2DDecorationUpdateStatus.Cancelled, false, inputRevision);
            if (!resourceDiagnostics.IsEmpty)
                return new(Canvas2DDecorationUpdateStatus.Rejected, false, inputRevision, null, resourceDiagnostics);

            lock (_sync)
            {
                if (linked.IsCancellationRequested)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Cancelled, false, inputRevision);
                if (_closing || _closed || _status != EditingSessionStatus.Ready ||
                    _currentScene is null || _document?.Revision != _currentScene.SourceRevision)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, false, inputRevision);
                if (inputRevision <= _decorationInputRevision)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Rejected, false, inputRevision);

                _decorationInput = input;
                _decorationInputRevision = inputRevision;
                _decorationComposition = null;
                _decorationPresentation = null;
                _decorationDiagnostics = [];
                _decorationResourceFailure = [];
                _presentedGeneration = null;
                _pendingPanPresentation = null;
                scene = _currentScene;
                generation = _generation;
                accepted = true;
                LifecycleObservation?.Accepted(DecorationAttachmentId, inputRevision);
            }

            var rendered = await RenderDecoratedSceneUnderGateAsync(scene, generation, presentationLifetime.Token, retirementToken)
                .ConfigureAwait(false);
            lock (_sync)
            {
                if (_closing || _closed || retirementToken.IsCancellationRequested)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, true, inputRevision);
                if (_decorationInputRevision != inputRevision)
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Superseded, true, inputRevision);
                if (!IsCurrentDecorationBaseUnderLock(scene, generation))
                    return DecorationResult(Canvas2DDecorationUpdateStatus.Unavailable, true, inputRevision);
                return DecorationResult(rendered.Succeeded &&
                    _decorationComposition?.Succeeded != false &&
                    _decorationPresentation?.FramePresented == true
                        ? Canvas2DDecorationUpdateStatus.Presented
                        : Canvas2DDecorationUpdateStatus.Failed, true, inputRevision);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested ||
            retirementToken.IsCancellationRequested || _closing || _closed)
        {
            return DecorationResult(accepted ? Canvas2DDecorationUpdateStatus.Unavailable :
                Canvas2DDecorationUpdateStatus.Cancelled, accepted, inputRevision);
        }
        finally
        {
            if (entered) _rendererGate.Release();
            Interlocked.Exchange(ref _decorationUpdateActive, 0);
            if (accepted) NotifyStateChanged();
        }
    }

    // Called only while the existing renderer gate is held. The native scene remains the sole
    // source for interaction, scope geometry, exports and publication. Combined content is never
    // installed in _currentScene or passed to a pipeline/contributor registry.
    private async ValueTask<Canvas2DRendererResult> RenderDecoratedSceneUnderGateAsync(
        Canvas2DScene scene, EditingSessionGeneration generation, CancellationToken cancellationToken = default,
        CancellationToken retirementToken = default)
    {
        using var renderLifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, retirementToken, _lifetime.Token);
        cancellationToken = renderLifetime.Token;
        Canvas2DDecorationInput? input;
        long revision;
        long baseGeneration;
        long surfaceGeneration;
        DocumentSnapshot snapshot;
        Canvas2DDecorationComposition? composition;
        ImmutableArray<Diagnostic> resourceFailure;
        lock (_sync)
        {
            if (cancellationToken.IsCancellationRequested || !IsCurrentDecorationBaseUnderLock(scene, generation))
                return RendererFailure("The native decoration base is no longer current.");
            input = _decorationInput;
            revision = _decorationInputRevision;
            surfaceGeneration = _surfaceGeneration;
            snapshot = AttachedDocument.CaptureSnapshot();
            if (!ReferenceEquals(scene, _decorationBaseScene))
            {
                _decorationBaseScene = scene;
                _decorationBaseGeneration = checked(_decorationBaseGeneration + 1);
                _decorationComposition = null;
                _decorationPresentation = null;
            }
            baseGeneration = _decorationBaseGeneration;
            composition = _decorationComposition;
            resourceFailure = _decorationResourceFailure;
        }

        if (input is not null && composition is null)
            composition = !resourceFailure.IsEmpty ? new(scene, false, resourceFailure) :
                Canvas2DDecorationComposer.Compose(scene, snapshot, DecorationAttachmentId,
                baseGeneration, revision, input, new Canvas2DDecorationResources(
                    _renderer.Configuration.DefaultFontFamily,
                    _renderer.Configuration.FontResources.Where(static resource =>
                        resource.FontWeight == 400 && resource.FontStyle == Inceptus.DiagramEngine.Contracts.Text.TextFontStyle.Normal)
                        .Select(static resource => resource.FontFamily),
                    _renderer.Configuration.ImageResources.Keys.Concat(input.Resources.SvgImages.Keys)));

        lock (_sync)
        {
            if (cancellationToken.IsCancellationRequested || !IsCurrentDecorationBaseUnderLock(scene, generation) || revision != _decorationInputRevision ||
                surfaceGeneration != _surfaceGeneration)
                return RendererFailure("The native decoration base changed during composition.");
            _decorationComposition = composition;
            _decorationDiagnostics = composition?.Diagnostics ?? [];
            _decorationPresentation = new(DecorationAttachmentId, scene.DocumentId, scene.SourceRevision,
                scene.PresentationContext, baseGeneration, revision, false, false);
        }

        var resources = input is not null && composition?.Succeeded == true
            ? input.Resources : Canvas2DDecorationResourceSet.Empty;
        var result = await _renderer.RenderAsync((composition?.Scene ?? scene).WithGrid(GridSettings),
            resources, DecorationAttachmentId, revision, cancellationToken).ConfigureAwait(false);
        if (Canvas2DRenderer.IsDecorationResourceFailure(result))
        {
            lock (_sync)
            {
                if (retirementToken.IsCancellationRequested || !IsCurrentDecorationBaseUnderLock(scene, generation) ||
                    revision != _decorationInputRevision || surfaceGeneration != _surfaceGeneration)
                    return result;
                _decorationResourceFailure = result.Diagnostics;
                composition = new(scene, false, result.Diagnostics);
                _decorationComposition = composition;
                _decorationDiagnostics = result.Diagnostics;
            }
            // The accepted input remains failed. Only a successfully drawn native fallback can
            // acknowledge a frame; no SVG child or unrelated text sibling is partially retained.
            result = await _renderer.RenderAsync(scene.WithGrid(GridSettings), cancellationToken).ConfigureAwait(false);
        }
        lock (_sync)
        {
            if (!retirementToken.IsCancellationRequested && IsCurrentDecorationBaseUnderLock(scene, generation) && revision == _decorationInputRevision &&
                surfaceGeneration == _surfaceGeneration)
            {
                _presentationDiagnostics = result.Diagnostics;
                _presentedGeneration = result.Succeeded ? generation : null;
                _pendingPanPresentation = null;
                _decorationPresentation = new(DecorationAttachmentId, scene.DocumentId, scene.SourceRevision,
                    scene.PresentationContext, baseGeneration, revision, result.Succeeded,
                    result.Succeeded && input is not null && composition?.Succeeded == true &&
                    !ReferenceEquals(composition.Scene, scene));
            }
        }
        return result;
    }

    private bool IsCurrentDecorationBaseUnderLock(Canvas2DScene scene, EditingSessionGeneration generation) =>
        !_closing && !_closed && _status == EditingSessionStatus.Ready && generation == _generation &&
        ReferenceEquals(scene, _currentScene) && _document?.Revision == scene.SourceRevision;

    private Canvas2DDecorationUpdateResult DecorationResult(
        Canvas2DDecorationUpdateStatus status, bool accepted, long inputRevision)
    {
        lock (_sync)
        {
            return new(status, accepted, inputRevision,
                accepted && _decorationPresentation?.InputRevision == inputRevision ? _decorationPresentation : null,
                accepted ? _presentationDiagnostics.AddRange(_decorationDiagnostics)
                    .OrderByDescending(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error)
                    .Take(Canvas2DDecorationContent.MaximumDiagnostics).ToImmutableArray() : []);
        }
    }
}
