using System.Collections.Immutable;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Microsoft.AspNetCore.Components;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Opt-in application annotations using only the modeler's public decoration API.</summary>
public partial class DemoAnnotations
{
    [Parameter] public bool Maintenance { get; set; }
    [Parameter] public InceptusBpmnModeler? Modeler { get; set; }
    private readonly CancellationTokenSource _lifetime = new();
    private BpmnDecorationAttachment? _attachment;
    private Canvas2DDecorationUpdateResult? _result;
    private long _revision;
    private string _text = "Review note";
    private string _feedback = "Annotations are off. Update to show the note.";
    private ImmutableArray<DemoAnnotationMessage> _messages = [];
    private string _selectedMessageId = "status-note";
    private string? _svgContent;
    private string? _preparationError;
    private bool _mixed;
    private bool _busy;
    private bool _disposed;
    private bool CanSubmit => !_busy && !_disposed && Modeler?.CaptureDecorationAttachment() is not null;
    private DemoAnnotationMessage? SelectedMessage => _messages.SingleOrDefault(message => message.Id == _selectedMessageId);
    private bool CanEditMixed => CanSubmit && _mixed && SelectedMessage is not null;

    protected override void OnParametersSet()
    {
        var current = Modeler?.CaptureDecorationAttachment();
        // A native rebuild can temporarily make capture unavailable without retiring input.
        if (current is null || current == _attachment) return;
        _attachment = current;
        _revision = Modeler?.CaptureEditorContext()?.Presentation.DecorationInputRevision ?? 0;
        _result = null;
        _mixed = false;
        _messages = [];
        _feedback = "Annotations are off for this attachment. Update to show the note.";
    }

    private Task UpdateAsync() => SubmitAsync(clear: false, fail: false);
    private Task ClearAsync() => SubmitAsync(clear: true, fail: false);
    private Task FailAsync() => SubmitAsync(clear: false, fail: true, _mixed ? _messages : null);
    private Task ShowMixedAsync() => SubmitAsync(clear: false, fail: false,
        DemoAnnotationProvider.CreateMixedMessages(string.IsNullOrWhiteSpace(_text) ? "Review note" : _text));

    private Task UpdateOneAsync()
    {
        if (!CanEditMixed) return Task.CompletedTask;
        return SubmitAsync(clear: false, fail: false, _messages.Select(message => message.Id == _selectedMessageId
            ? message with { Text = message.Text is null ? null : _text, AlternateArtwork = !message.AlternateArtwork }
            : message).ToImmutableArray());
    }

    private Task RemoveOneAsync() => !CanEditMixed ? Task.CompletedTask :
        SubmitAsync(clear: false, fail: false, _messages.Where(message => message.Id != _selectedMessageId).ToImmutableArray());

    private Task SwitchVariantAsync()
    {
        if (!CanEditMixed) return Task.CompletedTask;
        return SubmitAsync(clear: false, fail: false, _messages.Select(message => message.Id != _selectedMessageId ? message :
            message.HasImage ? message.Text is null
                ? message with { HasImage = false, Text = _text }
                : message with { Text = null }
                : message with { HasImage = true }).ToImmutableArray());
    }

    private static string Variant(DemoAnnotationMessage message) =>
        message.HasImage ? message.Text is null ? "SVG only" : "SVG + text" : "Text only";

    private async Task SubmitAsync(bool clear, bool fail, ImmutableArray<DemoAnnotationMessage>? messages = null)
    {
        if (!CanSubmit || Modeler is not { } modeler || modeler.CaptureDecorationAttachment() is not { } current) return;
        if (current != _attachment)
        {
            _attachment = current;
            _revision = Modeler?.CaptureEditorContext()?.Presentation.DecorationInputRevision ?? 0;
        }
        _busy = true;
        var inputPrepared = false;
        try
        {
            _preparationError = null;
            // Artwork acquisition belongs to the application and finishes before submission.
            if (messages is { } collection && collection.Any(message => message.HasImage) && _svgContent is null)
                _svgContent = await DemoAnnotationProvider.ReadSvgAsync(_lifetime.Token);
            if (_disposed) return;
            if (modeler.CaptureDecorationAttachment() != current)
            {
                _feedback = "The attachment changed while preparing the example. Submit again for the current source.";
                return;
            }
            var input = clear ? null : messages is { } mixed
                ? DemoAnnotationProvider.CreateInput(mixed, _svgContent, fail)
                : DemoAnnotationProvider.CreateInput(_text, fail);
            inputPrepared = true;
            // Another application action may have submitted input through the same public API.
            _revision = Math.Max(_revision, modeler.CaptureEditorContext()?.Presentation.DecorationInputRevision ?? 0);
            var revision = ++_revision;
            var result = clear
                ? await modeler.ClearDecorationsAsync(current, revision, _lifetime.Token)
                : await modeler.ReplaceDecorationsAsync(current, revision, input!, _lifetime.Token);
            if (!_disposed && modeler.CaptureDecorationAttachment() == current)
            {
                _result = result;
                if (result.Accepted)
                {
                    _mixed = !clear && messages.HasValue;
                    _messages = _mixed ? messages!.Value : [];
                    if (SelectedMessage is null) _selectedMessageId = _messages.IsEmpty ? string.Empty : _messages[0].Id;
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception exception) when (!inputPrepared && (exception is IOException or InvalidOperationException or ArgumentException))
        {
            if (!_disposed) _preparationError = "The example input could not be prepared: " + exception.Message;
        }
        finally { _busy = false; }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _attachment = null;
        _result = null;
        _messages = [];
        _svgContent = null;
        GC.SuppressFinalize(this);
    }
}
