using System.Diagnostics;
using System.Text;
using System.Xml.Linq;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class RazorSourceProvenanceTests
{
    [Theory]
    [MemberData(nameof(MappingCases))]
    public async Task MapsOnlyParsedSourceDirectivesAndIsIdempotent(string host, string sourceRoot, string newline, bool bom)
    {
        using var fixture = new BuildFixture(host, sourceRoot);
        var sourcePath = sourceRoot + "/src/Example.razor";
        var mappedPath = "/_/src/Example.razor";
        var directives = new[]
        {
            $"#pragma checksum \"{sourcePath}\" \"{{8829d00f-11b8-4213-878b-770e8597ac16}}\" \"aabb\"",
            $"#line 12 \"{sourcePath}\"",
            $"#line (1,1)-(1,8) \"{sourcePath}\"",
        };
        var nonDirectives = $$""""
            // #line 4 "{{sourcePath}}"
            /*
            #line 5 "{{sourcePath}}"
            */
            class Example
            {
                const string Verbatim = @"{{sourcePath}}";
                const string Raw = """
            #line 6 "{{sourcePath}}"
            """;
            }
            #line hidden
            #line default
            """".ReplaceLineEndings(newline);
        var source = string.Join(newline, directives) + newline + nonDirectives + newline;
        var expected = string.Join(newline, directives.Select(line => line.Replace(sourcePath, mappedPath, StringComparison.Ordinal)))
            + newline + nonDirectives + newline;
        var encoding = new UTF8Encoding(bom);
        File.WriteAllText(fixture.Source, source, encoding);

        var first = await fixture.RunAsync();

        Assert.True(first.ExitCode == 0, first.Output);
        Assert.Contains("MappingHost=" + (host == "dotnet" ? "Core" : "Full"), first.Output, StringComparison.Ordinal);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)), File.ReadAllBytes(fixture.Source));
        var modified = File.GetLastWriteTimeUtc(fixture.Source);
        var second = await fixture.RunAsync();
        Assert.True(second.ExitCode == 0, second.Output);
        Assert.Equal(encoding.GetPreamble().Concat(encoding.GetBytes(expected)), File.ReadAllBytes(fixture.Source));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(fixture.Source));
    }

    [Theory]
    [MemberData(nameof(UnmappedCases))]
    public async Task UnmappedAbsoluteSourceDirectivesFailWithoutChangingTheFile(string host, string unmappedPath)
    {
        using var fixture = new BuildFixture(host, "D:/approved/checkout");
        var source = $"#line 1 \"{unmappedPath}\"\nclass Example {{ }}\n";
        File.WriteAllText(fixture.Source, source, new UTF8Encoding(false));
        var before = File.ReadAllBytes(fixture.Source);

        var result = await fixture.RunAsync();

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("outside the configured PathMap", result.Output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllBytes(fixture.Source));
    }

    [Fact]
    public void ReleaseMappingUsesSdkGenerationAndPreservesDebugGenerationAndEmbeddedSources()
    {
        var project = XDocument.Load(Path.Combine(RepositoryRoot,
            "src/Inceptus.DiagramEngine.Bpmn.Blazor/Inceptus.DiagramEngine.Bpmn.Blazor.csproj"));
        var generation = Assert.Single(project.Descendants("UseRazorSourceGenerator"));
        Assert.Equal("false", generation.Value);
        Assert.Equal("'$(Configuration)' == 'Release'", (string?)generation.Parent!.Attribute("Condition"));
        Assert.Equal("$(PathMap),$([System.IO.Path]::GetPathRoot('$(MSBuildProjectDirectory)'))_$([System.IO.Path]::DirectorySeparatorChar)=/_/",
            generation.Parent.Element("PathMap")?.Value);
        Assert.Equal("../../build/RazorSourcePaths.targets", (string?)Assert.Single(project.Descendants("Import")).Attribute("Project"));

        var targets = XDocument.Load(Path.Combine(RepositoryRoot, "build/RazorSourcePaths.targets"));
        var target = Assert.Single(targets.Descendants("Target"));
        Assert.Equal("RazorGenerateComponentDefinition", (string?)target.Attribute("AfterTargets"));
        Assert.Equal("'$(Configuration)' == 'Release' and '$(UseRazorSourceGenerator)' == 'false'",
            (string?)target.Attribute("Condition"));
        var task = Assert.Single(target.Elements());
        Assert.Equal("@(_RazorComponentDefinition)", (string?)task.Attribute("Files"));
        Assert.Equal("$(PathMap)", (string?)task.Attribute("PathMap"));
        Assert.Equal("$(RoslynAssembliesPath)", (string?)task.Attribute("RoslynDirectory"));
        Assert.DoesNotContain(targets.Descendants().Concat(project.Descendants()), element =>
            element.Name.LocalName is "EmbeddedFiles" or "EmbedUntrackedSources" or "DebugType" or "RepositoryCommit" or "SourceRevisionId");
    }

    public static IEnumerable<object[]> MappingCases()
    {
        foreach (var host in AvailableBuildHosts())
        {
            yield return [host, "D:\\a\\DocumentEngine\\DocumentEngine", "\r\n", true];
            yield return [host, "/home/runner/work/DocumentEngine/DocumentEngine", "\n", false];
            yield return [host, "D:/build=owner/source,checkout", "\n", true];
        }
    }

    public static IEnumerable<object[]> UnmappedCases()
    {
        foreach (var host in AvailableBuildHosts())
        {
            yield return [host, "C:\\Users\\another-user\\private\\Component.razor"];
            yield return [host, "/Users/another-user/private/Component.razor"];
            yield return [host, "\\\\private-server\\checkout\\Component.razor"];
        }
    }

    private static IEnumerable<string> AvailableBuildHosts()
    {
        yield return "dotnet";
        if (!OperatingSystem.IsWindows())
        {
            yield break;
        }

        // Add Full Framework coverage when Visual Studio is installed; CLI-only machines
        // continue to exercise the same production target without requiring another toolchain.
        var locator = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(locator))
        {
            yield break;
        }

        var start = new ProcessStartInfo(locator)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { "-latest", "-products", "*", "-requires", "Microsoft.Component.MSBuild",
            "-find", @"MSBuild\Current\Bin\amd64\MSBuild.exe" })
        {
            start.ArgumentList.Add(argument);
        }

        using var process = Process.Start(start) ?? throw new InvalidOperationException("Visual Studio discovery could not start.");
        var output = process.StandardOutput.ReadToEndAsync();
        if (!process.WaitForExit(30_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("Visual Studio discovery timed out.");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException("Visual Studio discovery failed.");
        }

        foreach (var path in output.GetAwaiter().GetResult().Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (!File.Exists(path))
            {
                throw new InvalidOperationException("The discovered Visual Studio MSBuild does not exist: " + path);
            }

            yield return path;
        }
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Inceptus.DiagramEngine.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("The canonical source tree was not found.");
        }
    }

    private sealed class BuildFixture : IDisposable
    {
        private readonly string directory = Path.Combine(Path.GetTempPath(), "inceptus-razor-provenance-" + Guid.NewGuid().ToString("N"));
        private readonly string project;
        private readonly string host;

        public BuildFixture(string host, string sourceRoot)
        {
            this.host = host;
            Directory.CreateDirectory(directory);
            File.Copy(Path.Combine(RepositoryRoot, "global.json"), Path.Combine(directory, "global.json"));
            Source = Path.Combine(directory, "Example.razor.g.cs");
            project = Path.Combine(directory, "Mapping.proj");
            new XDocument(new XElement("Project",
                new XElement("Import", new XAttribute("Project", "Sdk.props"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("PropertyGroup",
                    new XElement("TargetFramework", "net10.0"),
                    new XElement("IsPackable", "false"),
                    new XElement("Configuration", "Release"),
                    new XElement("UseRazorSourceGenerator", "false"),
                    new XElement("PathMap", sourceRoot.Replace(",", ",,", StringComparison.Ordinal)
                        .Replace("=", "==", StringComparison.Ordinal) + "=/_/,/_/=/_/")),
                new XElement("ItemGroup", new XElement("_RazorComponentDefinition", new XAttribute("Include", Source))),
                new XElement("Import", new XAttribute("Project", "Sdk.targets"), new XAttribute("Sdk", "Microsoft.NET.Sdk")),
                new XElement("Import", new XAttribute("Project", Path.Combine(RepositoryRoot, "build/RazorSourcePaths.targets"))),
                new XElement("Target", new XAttribute("Name", "RazorGenerateComponentDefinition")),
                new XElement("Target", new XAttribute("Name", "ExerciseMapping"),
                    new XAttribute("DependsOnTargets", "RazorGenerateComponentDefinition"),
                    new XElement("Message", new XAttribute("Importance", "high"),
                        new XAttribute("Text", "MappingHost=$(MSBuildRuntimeType); SDK=$(NETCoreSdkVersion); Roslyn=$(RoslynAssembliesPath)"))))).Save(project);
        }

        public string Source { get; }

        public async Task<(int ExitCode, string Output)> RunAsync()
        {
            var start = new ProcessStartInfo(host)
            {
                WorkingDirectory = RepositoryRoot,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            if (host == "dotnet")
            {
                start.ArgumentList.Add("msbuild");
            }

            foreach (var argument in new[] { project, "-t:ExerciseMapping", "-nologo", "-nodeReuse:false" })
            {
                start.ArgumentList.Add(argument);
            }

            using var process = Process.Start(start) ?? throw new InvalidOperationException("MSBuild could not start.");
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromMinutes(1));
                return (process.ExitCode, await output + await error);
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync();
                }
            }
        }

        public void Dispose() => Directory.Delete(directory, recursive: true);
    }
}
