using System.Collections.Immutable;
using System.Globalization;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Inceptus.DiagramEngine.Contracts.Diagnostics;
using Inceptus.DiagramEngine.Contracts.Geometry;

namespace Inceptus.DiagramEngine.UnitTests.Blazor;

public sealed class BpmnApplicationActionContractTests
{
    [Fact]
    public void ExistingActionConstructorRemainsAvailableAndNewIconIsOptional()
    {
        var old = new BpmnApplicationAction("app:inspect", "Inspect");
        Assert.Null(old.Icon);
        Assert.True(old.IsEnabled);
        Assert.Null(old.DisabledExplanation);
        var signature = typeof(BpmnApplicationAction).GetConstructor([typeof(string), typeof(string), typeof(bool), typeof(string)]);
        Assert.NotNull(signature);
        Assert.True(signature.GetParameters()[2].IsOptional);
        Assert.True(signature.GetParameters()[3].IsOptional);
        var icon = new BpmnApplicationActionIcon("M0 0 L12 12 Z", new(0, 0, 24, 24));
        var action = new BpmnApplicationAction("app:inspect", "Inspect", false, "No linked record", icon);
        Assert.Same(icon, action.Icon);
        Assert.Equal("No linked record", action.DisabledExplanation);
        Assert.All(typeof(BpmnApplicationAction).GetConstructor([typeof(string), typeof(string), typeof(bool), typeof(string), typeof(BpmnApplicationActionIcon)])!
            .GetParameters(), parameter => Assert.False(parameter.IsOptional));
    }

    [Theory]
    [InlineData("M0 0 L10 0 L10 10 Z")]
    [InlineData("M0,0 H1 V2 Q3 4 5 6 C7 8 9 10 11 12 Z")]
    [InlineData("M +.5 -1.25 L 1e2 -2E-2 Z M0 0 L1 1")]
    [InlineData("M0\t0\nL2\r3")]
    [InlineData("M-1000000 1000000 L0 0")]
    public void IconAcceptsOnlyTheDocumentedStaticPathGrammar(string path)
    {
        var icon = new BpmnApplicationActionIcon(path, new(-1_000_000, -1_000_000, 1_000_000, 1_000_000));
        Assert.Equal(path, icon.PathData);
        Assert.Equal(new RectD(-1_000_000, -1_000_000, 1_000_000, 1_000_000), icon.ViewBox);
        Assert.Null(typeof(BpmnApplicationActionIcon).GetProperty(nameof(icon.PathData))!.SetMethod);
        Assert.Null(typeof(BpmnApplicationActionIcon).GetProperty(nameof(icon.ViewBox))!.SetMethod);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("M0 0")]
    [InlineData("M0 0 Z")]
    [InlineData("L0 0")]
    [InlineData("M0 0 l1 1")]
    [InlineData("M0 0 A1 1 0 0 0 2 2")]
    [InlineData("M0 0 S1 1 2 2")]
    [InlineData("M0 0 T1 1")]
    [InlineData("M0 0 L1 1 2 2")]
    [InlineData("M0 0 L1")]
    [InlineData("M0 0 C1 2 3 4 5")]
    [InlineData("M0 0 Q1 2 3")]
    [InlineData("M0 0 L1 1 Z L2 2")]
    [InlineData("M0 0 L1 1 Z Z")]
    [InlineData("M,0 0 L1 1")]
    [InlineData("M0,,0 L1 1")]
    [InlineData("M0 0, L1 1")]
    [InlineData("M0 0 L1,1,")]
    [InlineData("M0-1 L1 1")]
    [InlineData("M0 0 L1.1.1 2")]
    [InlineData("M0 0 L1e 2")]
    [InlineData("M0 0 L1e+ 2")]
    [InlineData("M0 0 LNaN 2")]
    [InlineData("M0 0 LInfinity 2")]
    [InlineData("M0 0 L1e999 2")]
    [InlineData("M0 0 L1000001 2")]
    [InlineData("M0 0 L-1000001 2")]
    [InlineData("M0 0 L١ ٢")]
    [InlineData("M0 0 L1 1\0")]
    [InlineData("M0 0 L1 1\" onload=\"alert(1)")]
    [InlineData("<svg><path d='M0 0 L1 1'/></svg>")]
    [InlineData("https://example.invalid/icon.svg")]
    [InlineData("url(#icon)")]
    public void IconRejectsMalformedUnsupportedOrExecutableContent(string path) =>
        Assert.Throws<ArgumentException>(() => new BpmnApplicationActionIcon(path, new(0, 0, 24, 24)));

    [Fact]
    public void IconBoundsAreInclusiveAndDoNotAllowUnboundedGeometry()
    {
        const string path = "M0 0 L1 1";
        Assert.Throws<ArgumentNullException>(() => new BpmnApplicationActionIcon(null!, new(0, 0, 24, 24)));
        Assert.Equal(4096, new BpmnApplicationActionIcon(path.PadRight(4096), new(0, 0, 24, 24)).PathData.Length);
        Assert.Throws<ArgumentException>(() => new BpmnApplicationActionIcon(path.PadRight(4097), new(0, 0, 24, 24)));
        var commands = "M0 0 " + string.Concat(Enumerable.Repeat("L1 1 ", 255));
        _ = new BpmnApplicationActionIcon(commands, new(0, 0, 24, 24));
        Assert.Throws<ArgumentException>(() => new BpmnApplicationActionIcon(commands + "Z", new(0, 0, 24, 24)));
        foreach (var viewBox in new RectD[] { default, new(0, 0, 0, 1), new(0, 0, 1, 0),
            new(1_000_001, 0, 1, 1), new(0, -1_000_001, 1, 1), new(0, 0, 1_000_001, 1), new(0, 0, 1, 1_000_001) })
            Assert.Throws<ArgumentException>(() => new BpmnApplicationActionIcon(path, viewBox));
    }

    [Fact]
    public void IconParsingIsInvariantUnderCommaDecimalCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            var icon = new BpmnApplicationActionIcon("M.5 .25 L1.75 2.5 Z", new(.5, .25, 24.5, 24.25));
            Assert.Equal("M.5 .25 L1.75 2.5 Z", icon.PathData);
            Assert.Equal(.5, icon.ViewBox.X);
            Assert.Throws<ArgumentException>(() => new BpmnApplicationActionIcon("M0,5 0,25 L1 1", new(0, 0, 24, 24)));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Theory]
    [InlineData(BpmnModelerOperationStatus.Succeeded)]
    [InlineData(BpmnModelerOperationStatus.Unavailable)]
    [InlineData(BpmnModelerOperationStatus.Cancelled)]
    [InlineData(BpmnModelerOperationStatus.Failed)]
    public void TypedResultHasExactlyTheApprovedAttemptOutcomes(BpmnModelerOperationStatus status)
    {
        var result = new BpmnApplicationActionResult(status);
        Assert.Equal(status, result.Status);
        Assert.Equal(status == BpmnModelerOperationStatus.Succeeded, result.Succeeded);
        Assert.False(result.Diagnostics.IsDefault);
        Assert.Empty(result.Diagnostics);
    }

    [Theory]
    [InlineData(BpmnModelerOperationStatus.Rejected)]
    [InlineData((BpmnModelerOperationStatus)(-1))]
    [InlineData((BpmnModelerOperationStatus)99)]
    public void TypedResultRejectsUnsupportedStatus(BpmnModelerOperationStatus status) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => new BpmnApplicationActionResult(status));

    [Theory]
    [InlineData("null")]
    [InlineData("count")]
    [InlineData("code-length")]
    [InlineData("code-control")]
    [InlineData("message-length")]
    [InlineData("source-length")]
    [InlineData("context-count")]
    [InlineData("context-key")]
    [InlineData("context-value")]
    public void TypedResultRejectsUnboundedDiagnostics(string invalid)
    {
        var diagnostic = new Diagnostic(invalid == "code-length" ? new('c', 129) : invalid == "code-control" ? "bad\ncode" : "CODE",
            DiagnosticSeverity.Warning, invalid == "message-length" ? new('m', 513) : "Message",
            invalid == "source-length" ? new('s', 257) : null,
            invalid switch
            {
                "context-count" => Enumerable.Range(0, 9).Select(index => new KeyValuePair<string, string>(index.ToString(CultureInfo.InvariantCulture), "value")),
                "context-key" => [new(new('k', 65), "value")],
                "context-value" => [new("key", new('v', 513))],
                _ => null,
            });
        ImmutableArray<Diagnostic> diagnostics = invalid switch
        {
            "null" => [null!],
            "count" => [.. Enumerable.Repeat(diagnostic, 17)],
            _ => [diagnostic],
        };
        Assert.Throws<ArgumentException>(() => new BpmnApplicationActionResult(BpmnModelerOperationStatus.Failed, diagnostics));
    }

    [Fact]
    public void TypedResultRetainsImmutableDiagnosticsAtTheirInclusiveBounds()
    {
        var context = Enumerable.Range(0, 8).Select(index => new KeyValuePair<string, string>(
            index.ToString(CultureInfo.InvariantCulture).PadRight(64, 'k'), new('v', 512)));
        var diagnostic = new Diagnostic(new('c', 128), DiagnosticSeverity.Warning, new('m', 512), new('s', 256), context);
        var diagnostics = ImmutableArray.CreateRange(Enumerable.Repeat(diagnostic, 16));
        var result = new BpmnApplicationActionResult(BpmnModelerOperationStatus.Succeeded, diagnostics);
        Assert.Equal(diagnostics, result.Diagnostics);
        Assert.All(result.Diagnostics, value => Assert.Same(diagnostic, value));
        Assert.Null(typeof(BpmnApplicationActionResult).GetProperty(nameof(result.Status))!.SetMethod);
        Assert.Null(typeof(BpmnApplicationActionResult).GetProperty(nameof(result.Diagnostics))!.SetMethod);
    }

    [Theory]
    [InlineData(BpmnModelerOperationStatus.Succeeded, true)]
    [InlineData(BpmnModelerOperationStatus.Cancelled, true)]
    [InlineData(BpmnModelerOperationStatus.Unavailable, false)]
    [InlineData(BpmnModelerOperationStatus.Failed, false)]
    public void ErrorDiagnosticsCannotContradictSuccessfulOrCancelledAttempts(BpmnModelerOperationStatus status, bool rejects)
    {
        ImmutableArray<Diagnostic> diagnostics = [new("APP_FAILURE", DiagnosticSeverity.Error, "Application failure")];
        if (rejects)
            Assert.Throws<ArgumentException>(() => new BpmnApplicationActionResult(status, diagnostics));
        else
            Assert.Same(diagnostics[0], Assert.Single(new BpmnApplicationActionResult(status, diagnostics).Diagnostics));
    }
}
