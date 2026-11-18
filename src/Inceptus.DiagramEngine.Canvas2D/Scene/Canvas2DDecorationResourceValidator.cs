using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using Inceptus.DiagramEngine.Contracts.Canvas2D;
using Inceptus.DiagramEngine.Contracts.Diagnostics;

namespace Inceptus.DiagramEngine.Canvas2D.Scene;

/// <summary>Admission validation for the bounded, self-contained static SVG image profile.</summary>
internal static class Canvas2DDecorationResourceValidator
{
    internal const int MaximumResourceBytes = 256 * 1024;
    internal const int MaximumTotalBytes = 2 * 1024 * 1024;
    internal const int MaximumElements = 4096;
    internal const int MaximumPathSegments = 4096;
    internal const int MaximumNumbers = 32768;
    internal const double MaximumDecodedArea = 8_000_000;
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string InvalidCode = "CANVAS2D_DECORATION_RESOURCE_INVALID";
    private const string LimitCode = "CANVAS2D_DECORATION_RESOURCE_LIMIT";
    private const string CollisionCode = "CANVAS2D_DECORATION_RESOURCE_COLLISION";
    private static readonly SearchValues<char> HexCharacters = SearchValues.Create("0123456789abcdefABCDEF");
    private static readonly HashSet<string> Colors = new((
        "aliceblue antiquewhite aqua aquamarine azure beige bisque black blanchedalmond blue blueviolet brown burlywood " +
        "cadetblue chartreuse chocolate coral cornflowerblue cornsilk crimson cyan darkblue darkcyan darkgoldenrod darkgray " +
        "darkgreen darkgrey darkkhaki darkmagenta darkolivegreen darkorange darkorchid darkred darksalmon darkseagreen " +
        "darkslateblue darkslategray darkslategrey darkturquoise darkviolet deeppink deepskyblue dimgray dimgrey dodgerblue " +
        "firebrick floralwhite forestgreen fuchsia gainsboro ghostwhite gold goldenrod gray green greenyellow grey honeydew " +
        "hotpink indianred indigo ivory khaki lavender lavenderblush lawngreen lemonchiffon lightblue lightcoral lightcyan " +
        "lightgoldenrodyellow lightgray lightgreen lightgrey lightpink lightsalmon lightseagreen lightskyblue lightslategray " +
        "lightslategrey lightsteelblue lightyellow lime limegreen linen magenta maroon mediumaquamarine mediumblue mediumorchid " +
        "mediumpurple mediumseagreen mediumslateblue mediumspringgreen mediumturquoise mediumvioletred midnightblue mintcream " +
        "mistyrose moccasin navajowhite navy oldlace olive olivedrab orange orangered orchid palegoldenrod palegreen paleturquoise " +
        "palevioletred papayawhip peachpuff peru pink plum powderblue purple rebeccapurple red rosybrown royalblue saddlebrown " +
        "salmon sandybrown seagreen seashell sienna silver skyblue slateblue slategray slategrey snow springgreen steelblue tan " +
        "teal thistle tomato turquoise violet wheat white whitesmoke yellow yellowgreen transparent").Split(' '), StringComparer.OrdinalIgnoreCase);

    internal static ImmutableArray<Diagnostic> Validate(Canvas2DDecorationResourceSet resources,
        IEnumerable<string> nativeReferences)
    {
        ArgumentNullException.ThrowIfNull(resources);
        ArgumentNullException.ThrowIfNull(nativeReferences);
        var native = nativeReferences.ToHashSet(StringComparer.Ordinal);
        var totalBytes = 0;
        var totalArea = 0d;
        foreach (var resource in resources.SvgImages.Values)
        {
            if (native.Contains(resource.ImageReference))
                return Failure(CollisionCode, "A decoration resource cannot replace a native image resource.", resource.ImageReference);
            // Check character count first so oversized strings do not require an unbounded encoding pass.
            if (resource.SvgContent.Length > MaximumResourceBytes)
                return Failure(LimitCode, "The decoration SVG resource exceeds a payload limit.", resource.ImageReference);
            var bytes = Encoding.UTF8.GetByteCount(resource.SvgContent);
            totalBytes += bytes;
            if (bytes > MaximumResourceBytes || totalBytes > MaximumTotalBytes)
                return Failure(LimitCode, "The decoration SVG resources exceed a payload limit.", resource.ImageReference);
            try
            {
                totalArea += ValidateSvg(resource.SvgContent);
                if (totalArea > MaximumDecodedArea)
                    return Failure(LimitCode, "The decoration SVG resources exceed the decoded-area limit.", resource.ImageReference);
            }
            catch (SvgValidationException exception)
            {
                return Failure(exception.IsLimit ? LimitCode : InvalidCode,
                    exception.IsLimit ? "The decoration SVG exceeds a static-profile complexity limit." :
                        "The decoration SVG does not conform to the supported static image profile.", resource.ImageReference);
            }
            catch (XmlException)
            {
                return Failure(InvalidCode, "The decoration SVG is not a supported self-contained XML image.", resource.ImageReference);
            }
        }
        return [];
    }

    private static double ValidateSvg(string content)
    {
        using var text = new StringReader(content);
        using var reader = XmlReader.Create(text, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            MaxCharactersInDocument = MaximumResourceBytes,
            IgnoreComments = true,
        });
        var budget = new Budget();
        var parents = new Stack<string>();
        var seenRoot = false;
        var area = 0d;
        while (reader.Read())
        {
            if (reader.NodeType == XmlNodeType.Element)
            {
                if (++budget.Elements > MaximumElements || reader.Depth > 32 || reader.AttributeCount > 32)
                    throw new SvgValidationException(true);
                if (reader.NamespaceURI != SvgNamespace || reader.Prefix.Length != 0 ||
                    reader.Depth == 0 && (seenRoot || reader.LocalName != "svg") ||
                    reader.Depth > 0 && (parents.Count == 0 || parents.Peek() is not ("svg" or "g") || reader.LocalName == "svg"))
                    throw new SvgValidationException();
                var name = reader.LocalName;
                var attributes = ReadAttributes(reader);
                ValidateElement(name, attributes, budget);
                if (reader.Depth == 0)
                {
                    seenRoot = true;
                    area = ValidateRoot(attributes, budget);
                }
                if (!reader.IsEmptyElement) parents.Push(name);
            }
            else if (reader.NodeType == XmlNodeType.EndElement)
                parents.Pop();
            else if (reader.NodeType is not (XmlNodeType.Whitespace or XmlNodeType.SignificantWhitespace or XmlNodeType.XmlDeclaration))
                throw new SvgValidationException();
        }
        if (!seenRoot) throw new SvgValidationException();
        return area;
    }

    private static Dictionary<string, string> ReadAttributes(XmlReader reader)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal);
        if (!reader.MoveToFirstAttribute()) return attributes;
        do
        {
            if (reader.Name == "xmlns" && reader.Depth == 1 && reader.Value == SvgNamespace) continue;
            if (reader.NamespaceURI.Length != 0 || reader.Prefix.Length != 0)
                throw new SvgValidationException();
            attributes.Add(reader.LocalName, reader.Value);
        } while (reader.MoveToNextAttribute());
        reader.MoveToElement();
        return attributes;
    }

    private static void ValidateElement(string name, Dictionary<string, string> attributes, Budget budget)
    {
        if (name is not ("svg" or "g" or "path" or "rect" or "circle" or "ellipse" or "line" or "polyline" or "polygon"))
            throw new SvgValidationException();
        foreach (var (attribute, value) in attributes)
        {
            switch (attribute)
            {
                case "fill":
                case "stroke":
                    if (!IsPaint(value)) throw new SvgValidationException();
                    break;
                case "opacity":
                case "fill-opacity":
                case "stroke-opacity":
                    Scalar(value, budget, 0, 1);
                    break;
                case "stroke-width":
                    Scalar(value, budget, 0);
                    break;
                case "stroke-miterlimit":
                    Scalar(value, budget, 1);
                    break;
                case "stroke-dashoffset":
                    Scalar(value, budget);
                    break;
                case "stroke-dasharray":
                    if (value != "none" && Numbers(value, budget).Any(static number => number < 0))
                        throw new SvgValidationException();
                    break;
                case "stroke-linecap":
                    if (value is not ("butt" or "round" or "square")) throw new SvgValidationException();
                    break;
                case "stroke-linejoin":
                    if (value is not ("miter" or "round" or "bevel")) throw new SvgValidationException();
                    break;
                case "fill-rule":
                    if (value is not ("nonzero" or "evenodd")) throw new SvgValidationException();
                    break;
                case "transform":
                    ValidateTransform(value, budget);
                    break;
                default:
                    if (!IsGeometryAttribute(name, attribute)) throw new SvgValidationException();
                    break;
            }
        }

        switch (name)
        {
            case "path":
                ValidatePath(Required(attributes, "d"), budget);
                break;
            case "polyline":
            case "polygon":
                var points = Numbers(Required(attributes, "points"), budget);
                if (points.Length % 2 != 0 || points.Length < (name == "polygon" ? 6 : 4))
                    throw new SvgValidationException();
                break;
            case "rect":
                Scalar(Required(attributes, "width"), budget, 0);
                Scalar(Required(attributes, "height"), budget, 0);
                break;
            case "circle":
                Scalar(Required(attributes, "r"), budget, 0);
                break;
            case "ellipse":
                Scalar(Required(attributes, "rx"), budget, 0);
                Scalar(Required(attributes, "ry"), budget, 0);
                break;
        }
        foreach (var (attribute, value) in attributes)
        {
            if (attribute is "x" or "y" or "cx" or "cy" or "x1" or "x2" or "y1" or "y2")
                Scalar(value, budget);
            else if (name == "rect" && attribute is "rx" or "ry") Scalar(value, budget, 0);
        }
    }

    private static bool IsGeometryAttribute(string element, string attribute) => element switch
    {
        "svg" => attribute is "width" or "height" or "viewBox" or "preserveAspectRatio",
        "path" => attribute == "d",
        "rect" => attribute is "x" or "y" or "width" or "height" or "rx" or "ry",
        "circle" => attribute is "cx" or "cy" or "r",
        "ellipse" => attribute is "cx" or "cy" or "rx" or "ry",
        "line" => attribute is "x1" or "y1" or "x2" or "y2",
        "polyline" or "polygon" => attribute == "points",
        _ => false,
    };

    private static double ValidateRoot(Dictionary<string, string> attributes, Budget budget)
    {
        var viewBox = Numbers(Required(attributes, "viewBox"), budget);
        if (viewBox.Length != 4 || viewBox[2] <= 0 || viewBox[3] <= 0)
            throw new SvgValidationException();
        var width = Dimension(Required(attributes, "width"), budget);
        var height = Dimension(Required(attributes, "height"), budget);
        if (attributes.TryGetValue("preserveAspectRatio", out var preserve))
        {
            var parts = preserve.Split([' ', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length is < 1 or > 2 || parts[0] is not ("none" or "xMinYMin" or "xMidYMin" or "xMaxYMin" or
                "xMinYMid" or "xMidYMid" or "xMaxYMid" or "xMinYMax" or "xMidYMax" or "xMaxYMax") ||
                parts.Length == 2 && (parts[0] == "none" || parts[1] is not ("meet" or "slice")))
                throw new SvgValidationException();
        }
        return Math.Ceiling(width) * Math.Ceiling(height);
    }

    private static double Dimension(string value, Budget budget)
    {
        value = value.Trim(' ', '\t', '\r', '\n');
        if (value.EndsWith("px", StringComparison.Ordinal))
        {
            value = value[..^2];
            if (value.Length == 0 || char.IsWhiteSpace(value[^1])) throw new SvgValidationException();
        }
        var result = Scalar(value, budget, 0, 1024);
        if (result == 0) throw new SvgValidationException();
        return result;
    }

    private static bool IsPaint(string value)
    {
        if (value == "none" || Colors.Contains(value)) return true;
        return value.Length is 4 or 5 or 7 or 9 && value[0] == '#' &&
            value.AsSpan(1).IndexOfAnyExcept(HexCharacters) < 0;
    }

    private static string Required(Dictionary<string, string> attributes, string key) =>
        attributes.TryGetValue(key, out var value) ? value : throw new SvgValidationException();

    private static double Scalar(string value, Budget budget, double minimum = -1_000_000, double maximum = 1_000_000)
    {
        var numbers = Numbers(value, budget);
        if (numbers.Length != 1 || numbers[0] < minimum || numbers[0] > maximum)
            throw new SvgValidationException();
        return numbers[0];
    }

    private static double[] Numbers(string value, Budget budget)
    {
        var reader = new NumberReader(value, budget);
        var values = new List<double>();
        while (!reader.End) values.Add(reader.Read());
        if (values.Count == 0) throw new SvgValidationException();
        return [.. values];
    }

    private static void ValidatePath(string value, Budget budget)
    {
        var reader = new NumberReader(value, budget);
        var command = '\0';
        var started = false;
        while (!reader.End)
        {
            if (char.IsAsciiLetter(reader.Current))
            {
                command = reader.Take();
                if (!started && command is not ('M' or 'm')) throw new SvgValidationException();
                if (command is 'Z' or 'z')
                {
                    budget.PathSegment();
                    command = '\0';
                    continue;
                }
            }
            var arity = char.ToUpperInvariant(command) switch
            {
                'M' or 'L' or 'T' => 2,
                'H' or 'V' => 1,
                'C' => 6,
                'S' or 'Q' => 4,
                'A' => 7,
                _ => throw new SvgValidationException(),
            };
            for (var index = 0; index < arity; index++)
            {
                var number = reader.Read();
                if (char.ToUpperInvariant(command) == 'A' &&
                    (index < 2 && number < 0 || index is 3 or 4 && !reader.LastWasFlag))
                    throw new SvgValidationException();
            }
            budget.PathSegment();
            started = true;
            command = command switch { 'M' => 'L', 'm' => 'l', _ => command };
        }
        if (!started) throw new SvgValidationException();
    }

    private static void ValidateTransform(string value, Budget budget)
    {
        var position = 0;
        var transforms = 0;
        while (position < value.Length)
        {
            while (position < value.Length && IsXmlWhitespace(value[position])) position++;
            if (position == value.Length) break;
            if (++transforms > 32) throw new SvgValidationException(true);
            var start = position;
            while (position < value.Length && char.IsAsciiLetter(value[position])) position++;
            var name = value[start..position];
            while (position < value.Length && IsXmlWhitespace(value[position])) position++;
            if (position == value.Length || value[position++] != '(') throw new SvgValidationException();
            start = position;
            while (position < value.Length && value[position] != ')') position++;
            if (position == value.Length) throw new SvgValidationException();
            var numbers = Numbers(value[start..position++], budget);
            if (!(name switch
            {
                "matrix" => numbers.Length == 6,
                "translate" or "scale" => numbers.Length is 1 or 2,
                "rotate" => numbers.Length is 1 or 3,
                "skewX" or "skewY" => numbers.Length == 1,
                _ => false,
            })) throw new SvgValidationException();
            while (position < value.Length && IsXmlWhitespace(value[position])) position++;
            if (position < value.Length && value[position] == ',')
            {
                position++;
                while (position < value.Length && IsXmlWhitespace(value[position])) position++;
                if (position == value.Length) throw new SvgValidationException();
            }
        }
        if (transforms == 0) throw new SvgValidationException();
    }

    private sealed class Budget
    {
        internal int Elements;
        internal int Numbers;
        private int _pathSegments;
        internal void PathSegment()
        {
            if (++_pathSegments > MaximumPathSegments) throw new SvgValidationException(true);
        }
    }

    private static bool IsXmlWhitespace(char value) => value is ' ' or '\t' or '\r' or '\n';

    private sealed class NumberReader(string value, Budget budget)
    {
        private int _position;
        private bool _afterNumber;
        private bool _hadWhitespace;
        internal bool LastWasFlag { get; private set; }
        internal bool End
        {
            get
            {
                SkipSpace();
                return _position == value.Length;
            }
        }
        internal char Current => value[_position];
        internal char Take()
        {
            _afterNumber = false;
            _hadWhitespace = false;
            return value[_position++];
        }

        internal double Read()
        {
            SkipSpace();
            if (_position < value.Length && value[_position] == ',')
            {
                if (!_afterNumber) throw new SvgValidationException();
                _position++;
                SkipSpace();
            }
            else if (_afterNumber && !_hadWhitespace && _position < value.Length && value[_position] is not ('+' or '-'))
                throw new SvgValidationException();
            var start = _position;
            if (_position < value.Length && value[_position] is '+' or '-') _position++;
            var digits = 0;
            while (_position < value.Length && char.IsAsciiDigit(value[_position])) { _position++; digits++; }
            if (_position < value.Length && value[_position] == '.')
            {
                _position++;
                while (_position < value.Length && char.IsAsciiDigit(value[_position])) { _position++; digits++; }
            }
            if (digits == 0) throw new SvgValidationException();
            if (_position < value.Length && value[_position] is 'e' or 'E')
            {
                _position++;
                if (_position < value.Length && value[_position] is '+' or '-') _position++;
                var exponent = _position;
                while (_position < value.Length && char.IsAsciiDigit(value[_position])) _position++;
                if (exponent == _position) throw new SvgValidationException();
            }
            if (++budget.Numbers > MaximumNumbers) throw new SvgValidationException(true);
            if (!double.TryParse(value.AsSpan(start, _position - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number) || Math.Abs(number) > 1_000_000)
                throw new SvgValidationException();
            _afterNumber = true;
            _hadWhitespace = false;
            LastWasFlag = _position - start == 1 && value[start] is '0' or '1';
            return number;
        }

        private void SkipSpace()
        {
            while (_position < value.Length && value[_position] is ' ' or '\t' or '\r' or '\n')
            {
                _position++;
                _hadWhitespace = true;
            }
        }
    }

    private static ImmutableArray<Diagnostic> Failure(string code, string message, string source) =>
        [new(code, DiagnosticSeverity.Error, message, source)];

    private sealed class SvgValidationException(bool isLimit = false) : Exception
    {
        internal bool IsLimit { get; } = isLimit;
    }
}
