namespace Inceptus.DiagramEngine.UnitTests.Architecture;

/// <summary>
/// Recognizes only the owner-approved index removals for the local investigation app.
/// Historical phase guards still reject additions, edits and other removals. This
/// status-only policy never loads the app, probes its files or requires its presence.
/// </summary>
internal static class ApprovedLocalInvestigationSeparation
{
    private const string RemovalPrefix = "D  tests/Inceptus.DiagramEngine.TestExample/";

    internal static bool IsIndexOnlyRemoval(string statusLine) =>
        statusLine.StartsWith(RemovalPrefix, StringComparison.Ordinal) &&
        statusLine[RemovalPrefix.Length..] is
            "App.razor" or
            "App.razor.cs" or
            "App.razor.css" or
            "B4ApplicationActions.cs" or
            "Inceptus.DiagramEngine.TestExample.csproj" or
            "Program.cs" or
            "Properties/AssemblyInfo.cs" or
            "README.md" or
            "_Imports.razor" or
            "wwwroot/catalogue.json" or
            "wwwroot/fixtures/process.inceptus.json" or
            "wwwroot/index.html";
}
