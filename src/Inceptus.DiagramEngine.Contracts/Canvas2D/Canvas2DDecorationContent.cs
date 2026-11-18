using System.Collections.Immutable;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Contracts.Canvas2D;

/// <summary>Immutable, bounded output of one transient decoration composition.</summary>
public sealed class Canvas2DDecorationContent
{
    public const int MaximumItems = 10000;
    public const int MaximumDiagnostics = 32;

    public Canvas2DDecorationContent(IEnumerable<Canvas2DSceneItem>? items = null,
        IEnumerable<Diagnostic>? diagnostics = null)
    {
        Items = CopyBounded(items, MaximumItems, nameof(items));
        Diagnostics = CopyBounded(diagnostics, MaximumDiagnostics, nameof(diagnostics));
    }

    public static Canvas2DDecorationContent Empty { get; } = new();
    public ImmutableArray<Canvas2DSceneItem> Items { get; }
    public ImmutableArray<Diagnostic> Diagnostics { get; }

    private static ImmutableArray<T> CopyBounded<T>(IEnumerable<T>? values, int maximum, string name)
        where T : class
    {
        if (values is null) return [];
        var builder = ImmutableArray.CreateBuilder<T>();
        foreach (var value in values)
        {
            ArgumentNullException.ThrowIfNull(value, name);
            if (builder.Count == maximum)
                throw new ArgumentException("The decoration output exceeds its bounded collection limit.", name);
            builder.Add(value);
        }
        return builder.ToImmutable();
    }
}
