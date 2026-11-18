using System.Globalization;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.Bpmn.Blazor;

/// <summary>
/// Immutable monochrome decorative path geometry. It contains no markup, resource references,
/// input behavior or application forms; the modeler owns the actionable control.
/// </summary>
public sealed class BpmnApplicationActionIcon
{
    public BpmnApplicationActionIcon(string pathData, RectD viewBox)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathData);
        if (pathData.Length > 4096)
            throw new ArgumentException("The application icon exceeds the supported geometry bounds.", nameof(pathData));
        if (!ValidExtent(viewBox.X) || !ValidExtent(viewBox.Y) || !ValidExtent(viewBox.Width) ||
            !ValidExtent(viewBox.Height) || viewBox.Width <= 0 || viewBox.Height <= 0)
            throw new ArgumentException("The application icon view box exceeds the supported geometry bounds.", nameof(viewBox));
        ValidatePath(pathData);
        PathData = pathData;
        ViewBox = viewBox;
    }

    public string PathData { get; }
    public RectD ViewBox { get; }

    private static bool ValidExtent(double value) => double.IsFinite(value) && Math.Abs(value) <= 1_000_000;

    private static void ValidatePath(string path)
    {
        var index = 0;
        var commandCount = 0;
        var subpath = false;
        var hasDrawing = false;
        while (true)
        {
            SkipWhitespace(path, ref index);
            if (index == path.Length) break;
            if (++commandCount > 256) Invalid();
            var command = path[index++];
            var operands = command switch
            {
                'M' or 'L' => 2,
                'H' or 'V' => 1,
                'C' => 6,
                'Q' => 4,
                'Z' => 0,
                _ => -1,
            };
            if (operands < 0 || !subpath && command != 'M') Invalid();
            if (command == 'M') subpath = true;
            else if (command != 'Z') hasDrawing = true;
            if (command == 'Z') subpath = false;
            for (var operand = 0; operand < operands; operand++)
            {
                var beforeSeparator = index;
                SkipWhitespace(path, ref index);
                if (operand > 0)
                {
                    if (index < path.Length && path[index] == ',')
                    {
                        index++;
                        SkipWhitespace(path, ref index);
                    }
                    else if (index == beforeSeparator) Invalid();
                }
                ReadNumber(path, ref index);
            }
            // A next segment must carry its own command. A comma is an operand separator,
            // never a prefix/suffix delimiter around commands.
            SkipWhitespace(path, ref index);
            if (index < path.Length && path[index] is not ('M' or 'L' or 'H' or 'V' or 'C' or 'Q' or 'Z')) Invalid();
        }
        if (!hasDrawing) Invalid();
    }

    private static void ReadNumber(string path, ref int index)
    {
        var start = index;
        if (index < path.Length && path[index] is '+' or '-') index++;
        var digits = 0;
        while (index < path.Length && char.IsAsciiDigit(path[index])) { index++; digits++; }
        if (index < path.Length && path[index] == '.')
        {
            index++;
            while (index < path.Length && char.IsAsciiDigit(path[index])) { index++; digits++; }
        }
        if (digits == 0) Invalid();
        if (index < path.Length && path[index] is 'e' or 'E')
        {
            index++;
            if (index < path.Length && path[index] is '+' or '-') index++;
            var exponentStart = index;
            while (index < path.Length && char.IsAsciiDigit(path[index])) index++;
            if (exponentStart == index) Invalid();
        }
        if (!double.TryParse(path.AsSpan(start, index - start), NumberStyles.Float,
            CultureInfo.InvariantCulture, out var value) || !ValidExtent(value)) Invalid();
    }

    private static void SkipWhitespace(string path, ref int index)
    {
        while (index < path.Length && path[index] is ' ' or '\t' or '\r' or '\n') index++;
    }

    private static void Invalid() => throw new ArgumentException("The application icon path does not conform to the supported static geometry grammar.");
}
