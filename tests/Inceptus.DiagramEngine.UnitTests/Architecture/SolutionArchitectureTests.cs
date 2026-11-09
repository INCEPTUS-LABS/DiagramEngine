using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Inceptus.DiagramEngine.Contracts.Geometry;
using Inceptus.DiagramEngine.Contracts.Primitives;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class SolutionArchitectureTests
{
    private const string Contracts = "Inceptus.DiagramEngine.Contracts";
    private const string Runtime = "Inceptus.DiagramEngine.Runtime";
    private const string Canvas2D = "Inceptus.DiagramEngine.Canvas2D";
    private const string Bpmn = "Inceptus.DiagramEngine.Bpmn";
    private const string BpmnBlazor = "Inceptus.DiagramEngine.Bpmn.Blazor";
    private const string Organizational = "Inceptus.DiagramEngine.Organizational";
    private const string Integration = "Inceptus.Bpmn.Integration";
    private const string Blazor = "Inceptus.DiagramEngine.Blazor";

    private static readonly IReadOnlyDictionary<string, string[]> ExpectedProductionGraph =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [],
            [Runtime] = [Contracts],
            [Canvas2D] = [Contracts, Runtime],
            [Bpmn] = [Contracts, Organizational],
            [Organizational] = [Contracts],
            [BpmnBlazor] = [Bpmn, Canvas2D, Contracts, Organizational, Runtime],
            [Integration] = [Bpmn, Canvas2D, Contracts, Organizational, Runtime],
            [Blazor] = [Integration, BpmnBlazor, Canvas2D, Contracts, Runtime],
        };

    private static readonly string[] ProductionAssemblyNames =
        [Contracts, Runtime, Canvas2D, Bpmn, Organizational, BpmnBlazor, Integration, Blazor];

    [Fact]
    public void ProductionProjectReferencesMatchTheApprovedGraphExactly()
    {
        var projects = GetProductionProjectPaths();

        foreach (var (projectName, expectedReferences) in ExpectedProductionGraph)
        {
            var actualReferences = ReadProjectReferences(projects[projectName])
                .Select(path => Path.GetFileNameWithoutExtension(path)
                    ?? throw new InvalidOperationException("A project reference has no file name."))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), actualReferences);
        }
    }

    [Fact]
    public void ProductionProjectSetContainsExactlyTheApprovedProjects()
    {
        var actualProjects = GetProductionProjectPaths().Keys.Order(StringComparer.Ordinal);
        var expectedProjects = ExpectedProductionGraph.Keys.Order(StringComparer.Ordinal);

        Assert.Equal(expectedProjects, actualProjects);
    }

    [Fact]
    public void ProductionProjectGraphIsAcyclicAndAllReferencesResolve()
    {
        var projects = GetProductionProjectPaths();
        var graph = projects.ToDictionary(
            pair => pair.Key,
            pair => ReadProjectReferences(pair.Value)
                .Select(path => Path.GetFileNameWithoutExtension(path)
                    ?? throw new InvalidOperationException("A project reference has no file name."))
                .ToArray(),
            StringComparer.Ordinal);

        foreach (var references in graph.Values)
        {
            foreach (var reference in references)
            {
                Assert.True(graph.ContainsKey(reference), $"Unknown production project reference: {reference}.");
            }
        }

        var visited = new HashSet<string>(StringComparer.Ordinal);
        var active = new HashSet<string>(StringComparer.Ordinal);

        foreach (var project in graph.Keys)
        {
            Assert.False(HasCycle(project, graph, visited, active), $"A cycle includes {project}.");
        }
    }

    [Fact]
    public void BuildConfigurationIsCentralAndTargetsNet10()
    {
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));
        var packageProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));

        Assert.Equal("net10.0", ReadElementValue(buildProperties, "TargetFramework"));
        Assert.Equal("enable", ReadElementValue(buildProperties, "Nullable"));
        Assert.Equal("enable", ReadElementValue(buildProperties, "ImplicitUsings"));
        Assert.Equal("true", ReadElementValue(buildProperties, "TreatWarningsAsErrors"));
        Assert.Equal("true", ReadElementValue(buildProperties, "Deterministic"));
        Assert.Equal("true", ReadElementValue(buildProperties, "DeterministicSourcePaths"));
        Assert.Equal("$(MSBuildThisFileDirectory)=/_/", ReadElementValue(buildProperties, "PathMap"));
        Assert.Equal("true", ReadElementValue(buildProperties, "EnableNETAnalyzers"));
        Assert.Equal("14.0", ReadElementValue(buildProperties, "LangVersion"));
        Assert.Equal("true", ReadElementValue(packageProperties, "ManagePackageVersionsCentrally"));
    }

    [Fact]
    public void SharedPackageFamilyMetadataIsExplicit()
    {
        var buildProperties = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.props"));

        Assert.Equal("1.0.0", ReadElementValue(buildProperties, "Version"));
        Assert.Equal("Robert Prokopczuk", ReadElementValue(buildProperties, "Authors"));

        var releaseTargets = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Build.targets"));
        Assert.Equal("MIT", ReadElementValue(releaseTargets, "PackageLicenseExpression"));
        Assert.Equal("Copyright (c) 2026 Inceptus Robert Prokopczuk",
            ReadElementValue(releaseTargets, "Copyright"));
        Assert.Equal("https://inceptus.online/bpmn/editor/", ReadElementValue(releaseTargets, "PackageProjectUrl"));
        Assert.Equal("https://github.com/INCEPTUS-LABS/DiagramEngine",
            ReadElementValue(releaseTargets, "RepositoryUrl"));
        Assert.Equal("git", ReadElementValue(releaseTargets, "RepositoryType"));
        Assert.Equal("true", ReadElementValue(releaseTargets, "PublishRepositoryUrl"));
        Assert.Equal("portable", ReadElementValue(releaseTargets, "DebugType"));
        Assert.Equal("true", ReadElementValue(releaseTargets, "IncludeSymbols"));
        Assert.Equal("snupkg", ReadElementValue(releaseTargets, "SymbolPackageFormat"));
        Assert.Equal("README.md", ReadElementValue(releaseTargets, "PackageReadmeFile"));
        Assert.Equal("inceptus-nuget-icon.png", ReadElementValue(releaseTargets, "PackageIcon"));
        Assert.DoesNotContain(releaseTargets.Descendants(), element =>
            element.Name.LocalName is "RepositoryCommit" or "SourceRevisionId" or "PackageLicenseFile");
        Assert.All(releaseTargets.Root!.Elements(), group =>
            Assert.Equal("'$(IsPackable)' == 'true'", (string?)group.Attribute("Condition")));

        string[] releaseDocuments = ["README.md", "LICENSE", "RELEASE-NOTES.md", "THIRD-PARTY-NOTICES.md", "branding/inceptus-nuget-icon.png"];
        var packedFiles = releaseTargets.Descendants("None").ToArray();
        Assert.Equal(releaseDocuments.Order(StringComparer.Ordinal),
            packedFiles.Select(file => ((string)file.Attribute("Include")!)
                .Replace("$(MSBuildThisFileDirectory)", string.Empty, StringComparison.Ordinal))
                .Order(StringComparer.Ordinal));
        Assert.All(packedFiles, file =>
        {
            Assert.Equal("true", (string?)file.Attribute("Pack"));
            var isIcon = ((string)file.Attribute("Include")!).EndsWith("branding/inceptus-nuget-icon.png", StringComparison.Ordinal);
            Assert.Equal(isIcon ? "inceptus-nuget-icon.png" : "/", (string?)file.Attribute("PackagePath"));
        });
        Assert.All(releaseDocuments, file => Assert.True(File.Exists(Path.Combine(RepositoryRoot, file))));
        Assert.Contains("Copyright (c) 2026 Inceptus Robert Prokopczuk",
            File.ReadAllText(Path.Combine(RepositoryRoot, "LICENSE")), StringComparison.Ordinal);
    }

    [Fact]
    public void OnlyCanvas2DRuntimeFriendDependencyHasAMatchingPackageConstraint()
    {
        var constraints = GetProductionProjectPaths().SelectMany(pair =>
            XDocument.Load(pair.Value).Descendants("_ProjectReferencesWithVersions")
                .Select(element => (Project: pair.Key, Element: element))).ToArray();
        var constraint = Assert.Single(constraints);
        Assert.Equal(Canvas2D, constraint.Project);
        Assert.Equal("@(_ProjectReferencesWithVersions)", (string?)constraint.Element.Attribute("Update"));
        Assert.Equal("'%(_ProjectReferencesWithVersions.Filename)' == 'Inceptus.DiagramEngine.Runtime'",
            (string?)constraint.Element.Attribute("Condition"));
        Assert.Equal("[$(PackageVersion)]", constraint.Element.Element("ProjectVersion")?.Value);
        Assert.Equal("_GetProjectReferenceVersions",
            (string?)constraint.Element.Ancestors("Target").Single().Attribute("AfterTargets"));
    }

    [Fact]
    public void ReusablePackageProjectsDeclareExplicitFoundationMetadata()
    {
        var expectedMetadata = new Dictionary<string, (string Description, string Tags)>(StringComparer.Ordinal)
        {
            [Contracts] =
            (
                "Immutable contracts and extension APIs for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;contracts"
            ),
            [Runtime] =
            (
                "Headless document, command, history, projection, layout, routing, validation, and native persistence runtime for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;runtime;headless"
            ),
            [Bpmn] =
            (
                "BPMN notation semantics, commands, validation, and plugin contributions for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;bpmn"
            ),
            [Organizational] =
            (
                "Organizational Profile semantics and plugin contributions for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;organizational"
            ),
            [Canvas2D] =
            (
                "Canvas2D browser editing session, interaction, scene, and renderer integration for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;canvas2d;browser"
            ),
            [BpmnBlazor] =
            (
                "BPMN modeler components and browser presentation integration for the Inceptus Diagram Engine.",
                "inceptus;diagram-engine;bpmn;blazor;modeler"
            ),
        };

        foreach (var (projectName, expected) in expectedMetadata)
        {
            var project = XDocument.Load(FindProject(projectName));

            Assert.Equal(projectName, ReadElementValue(project, "PackageId"));
            Assert.Equal(projectName, ReadElementValue(project, "AssemblyName"));
            Assert.Equal(projectName, ReadElementValue(project, "RootNamespace"));
            Assert.Equal("true", ReadElementValue(project, "IsPackable"));
            Assert.Equal(expected.Description, ReadElementValue(project, "Description"));
            Assert.Equal(expected.Tags, ReadElementValue(project, "PackageTags"));
            Assert.DoesNotContain(
                project.Descendants(),
                element => element.Name.LocalName is
                    "Version" or "VersionPrefix" or "VersionSuffix" or "PackageVersion" or "Authors");
        }
    }

    [Fact]
    public void ApplicationAndTestProjectsRemainExplicitlyNonPackable()
    {
        string[] nonPackableProjects =
        [
            Blazor,
            "Inceptus.DiagramEngine.UnitTests",
            "Inceptus.DiagramEngine.IntegrationTests",
        ];

        foreach (var projectName in nonPackableProjects)
        {
            var project = XDocument.Load(FindProject(projectName));

            Assert.Equal("false", ReadElementValue(project, "IsPackable"));
        }
    }

    [Fact]
    public void SdkAndCentralPackageVersionsArePinnedExactly()
    {
        using var globalConfiguration = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(RepositoryRoot, "global.json")));
        var sdk = globalConfiguration.RootElement.GetProperty("sdk");

        Assert.Equal("10.0.403", sdk.GetProperty("version").GetString());
        Assert.Equal("disable", sdk.GetProperty("rollForward").GetString());
        Assert.False(sdk.GetProperty("allowPrerelease").GetBoolean());

        var packageDocument = XDocument.Load(Path.Combine(RepositoryRoot, "Directory.Packages.props"));
        var actualVersions = packageDocument
            .Descendants()
            .Where(element => element.Name.LocalName == "PackageVersion")
            .ToDictionary(
                element => (string?)element.Attribute("Include")
                    ?? throw new InvalidOperationException("A central package has no name."),
                element => (string?)element.Attribute("Version")
                    ?? throw new InvalidOperationException("A central package has no version."),
                StringComparer.Ordinal);
        var expectedVersions = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.Components.Web"] = "10.0.14",
            ["Microsoft.AspNetCore.Components.WebAssembly"] = "10.0.14",
            ["Microsoft.AspNetCore.Components.WebAssembly.DevServer"] = "10.0.14",
            ["Microsoft.JSInterop"] = "10.0.14",
            ["Microsoft.Extensions.Localization"] = "10.0.14",
            ["Microsoft.NET.Test.Sdk"] = "18.11.0",
            ["xunit"] = "2.9.3",
            ["xunit.runner.visualstudio"] = "3.1.5",
        };

        Assert.Equal(
            expectedVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal),
            actualVersions.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [Fact]
    public void ProjectsDeclareOnlyTheExpectedDirectPackages()
    {
        var expectedPackages = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [],
            [Runtime] = [],
            [Canvas2D] = ["Microsoft.JSInterop"],
            [Bpmn] = [],
            [BpmnBlazor] =
            [
                "Microsoft.AspNetCore.Components.Web",
                "Microsoft.Extensions.Localization",
                "Microsoft.JSInterop",
            ],
            [Organizational] = [],
            [Blazor] =
            [
                "Microsoft.AspNetCore.Components.WebAssembly",
                "Microsoft.AspNetCore.Components.WebAssembly.DevServer",
            ],
            ["Inceptus.DiagramEngine.UnitTests"] =
            [
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "xunit.runner.visualstudio",
            ],
            ["Inceptus.DiagramEngine.IntegrationTests"] =
            [
                "Microsoft.NET.Test.Sdk",
                "xunit",
                "xunit.runner.visualstudio",
            ],
        };

        foreach (var (projectName, expected) in expectedPackages)
        {
            var projectPath = FindProject(projectName);
            var document = XDocument.Load(projectPath);
            var packageReferences = document
                .Descendants()
                .Where(element => element.Name.LocalName == "PackageReference")
                .ToArray();
            var actual = packageReferences
                .Select(element => (string?)element.Attribute("Include"))
                .Where(value => value is not null)
                .Cast<string>()
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expected.Order(StringComparer.Ordinal), actual);
            Assert.All(packageReferences, reference => Assert.Null(reference.Attribute("Version")));
        }
    }

    [Fact]
    public void CompiledAssembliesDoNotContainForbiddenProductionDependencies()
    {
        var forbiddenReferences = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            [Contracts] = [Runtime, Canvas2D, Bpmn, Organizational, BpmnBlazor, Blazor],
            [Runtime] = [Canvas2D, Bpmn, Organizational, BpmnBlazor, Blazor],
            [Canvas2D] = [Bpmn, Organizational, BpmnBlazor, Blazor],
            [Bpmn] = [Runtime, Canvas2D, BpmnBlazor, Blazor],
            [Organizational] = [Runtime, Canvas2D, Bpmn, BpmnBlazor, Blazor],
            [BpmnBlazor] = [Blazor],
        };

        foreach (var (assemblyName, forbidden) in forbiddenReferences)
        {
            var references = LoadProductionAssembly(assemblyName)
                .GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name is not null)
                .Cast<string>()
                .ToHashSet(StringComparer.Ordinal);

            foreach (var forbiddenReference in forbidden)
            {
                Assert.DoesNotContain(forbiddenReference, references);
            }
        }
    }

    [Fact]
    public void UpstreamAssembliesReferenceNoBrowserOrJavaScriptInteropAssemblies()
    {
        string[] forbiddenPrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];

        foreach (var assemblyName in new[] { Contracts, Runtime, Bpmn, Organizational })
        {
            var references = LoadProductionAssembly(assemblyName).GetReferencedAssemblies();

            Assert.DoesNotContain(
                references,
                reference => forbiddenPrefixes.Any(prefix =>
                    reference.Name?.StartsWith(prefix, StringComparison.Ordinal) == true));
        }
    }

    [Fact]
    public void UpstreamPublicSignaturesContainNoBrowserGraphicsOrDomTypes()
    {
        foreach (var assemblyName in new[] { Contracts, Runtime, Bpmn, Organizational })
        {
            var forbiddenTypes = GetPublicSignatureTypes(LoadProductionAssembly(assemblyName))
                .Where(IsBrowserOrDomType)
                .Select(type => type.FullName ?? type.Name)
                .Distinct(StringComparer.Ordinal)
                .ToArray();

            Assert.Empty(forbiddenTypes);
        }
    }

    [Fact]
    public void NoGenericRendererBackendRegistryOrSelectionApiExists()
    {
        const string ApprovedRenderer =
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRenderer";
        string[] forbiddenTypeNameFragments =
        [
            "RenderingBackend",
            "RenderBackend",
            "RendererRegistry",
            "RendererSelector",
            "RendererSelection",
        ];
        string[] forbiddenMemberNames =
        [
            "GetRenderer",
            "RegisterRenderer",
            "RegisterRenderingBackend",
            "ResolveRenderer",
            "ResolveRenderingBackend",
            "SelectRenderer",
            "SelectRenderingBackend",
            "TryGetRenderer",
        ];

        var rendererTypes = new List<Type>();
        foreach (var assemblyName in ProductionAssemblyNames)
        {
            foreach (var type in LoadProductionAssembly(assemblyName).GetTypes())
            {
                if (type.Name.EndsWith("Renderer", StringComparison.Ordinal))
                {
                    rendererTypes.Add(type);
                }

                Assert.DoesNotContain(
                    forbiddenTypeNameFragments,
                    fragment => type.Name.Contains(fragment, StringComparison.Ordinal));

                var declaredMemberNames = type
                    .GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                        BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Select(member => member.Name)
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var memberName in forbiddenMemberNames)
                {
                    Assert.DoesNotContain(memberName, declaredMemberNames);
                }
            }
        }

        var renderer = Assert.Single(rendererTypes);
        Assert.Equal(ApprovedRenderer, renderer.FullName);
        Assert.True(renderer.IsClass);
        Assert.True(renderer.IsSealed);
        Assert.False(renderer.IsAbstract);
    }

    [Fact]
    public void PhaseATypesExposeNoPluginImplementableFrameworkEngine()
    {
        var contractEngineTypes = LoadProductionAssembly(Contracts)
            .GetExportedTypes()
            .Where(type => type.Name.Contains("Engine", StringComparison.Ordinal))
            .ToArray();
        var bpmnEngineImplementations = LoadProductionAssembly(Bpmn)
            .GetTypes()
            .Where(type =>
                type.BaseType?.Name.Contains("Engine", StringComparison.Ordinal) == true ||
                type.GetInterfaces().Any(@interface =>
                    @interface.Name.Contains("Engine", StringComparison.Ordinal)))
            .ToArray();

        Assert.Empty(contractEngineTypes);
        Assert.Empty(bpmnEngineImplementations);
    }

    [Fact]
    public void ContractsExportExactlyTheApprovedPhaseHTypes()
    {
        string[] expectedTypeNames =
        [
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DCanonicalSceneItemVisualOverride",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationSourceSpace",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScopeGeometryBaseContext",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScopeGeometryBaseResult",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDerivedRegionMapping",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedRegionEnvelope",
            "Inceptus.DiagramEngine.Contracts.Presentation.IProcessPresentationViewProvider",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScopeGeometryNodeBounds",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScopeGeometryPresentationContext",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScopeGeometryPresentationResult",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialCoordinateBand",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialCoordinateMap",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DScopeGeometryContributor",
            "Inceptus.DiagramEngine.Contracts.Commands.ConnectorRoutingIntent",
            "Inceptus.DiagramEngine.Contracts.Commands.ConnectorRoutingIntentKind",
            "Inceptus.DiagramEngine.Contracts.Commands.SetConnectorRoutingTypeCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.SpatialRegionHeightIntent",
            "Inceptus.DiagramEngine.Contracts.Commands.SpatialScopeWidthIntent",
            "Inceptus.DiagramEngine.Contracts.History.SpatialScopeWidthHistoryDelta",
            "Inceptus.DiagramEngine.Contracts.Routing.SpatialScopeWidthSnapshot",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialDimensionConstraints",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialResizeEdge",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialResizeTarget",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialResizeRequest",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DSpatialResizePlanner",
            "Inceptus.DiagramEngine.Contracts.EditorState.Canvas2DSpatialResizeFeedback",
            "Inceptus.DiagramEngine.Contracts.EditorState.Canvas2DSpatialResizePhase",
            "Inceptus.DiagramEngine.Contracts.History.ConnectorRoutingTypeHistoryDelta",
            "Inceptus.DiagramEngine.Contracts.History.SpatialRegionHeightHistoryDelta",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorAutomaticRouteProof",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorPathQuality",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingEndpointObservation",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingRecord",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingType",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingOutcome",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorNoRouteReason",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingPreparationPurpose",
            "Inceptus.DiagramEngine.Contracts.Routing.IConnectorRoutingStatePreparer",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingStatePreparationRequest",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingStatePreparationResult",
            "Inceptus.DiagramEngine.Contracts.Routing.IStableConnectorRoutingPolicy",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingAssessment",
            "Inceptus.DiagramEngine.Contracts.Routing.ConnectorRoutingWorkResult",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingLogicalGeometry",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeGeometryInputs",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeGeometryContributorSnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeGeometrySnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeTextMeasurementSnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeNodeCaptionSnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeNodeGeometrySnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.SpatialRegionGeometrySnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.ScopeRoutingSnapshot",
            "Inceptus.DiagramEngine.Contracts.Routing.DocumentRoutingSnapshot",
            "Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext",
            "Inceptus.DiagramEngine.Contracts.Presentation.ProcessScopePresentationContext",
            "Inceptus.DiagramEngine.Contracts.Presentation.DocumentPresentationContext",
            "Inceptus.DiagramEngine.Contracts.Presentation.PresentationScopePlacement",
            "Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewPlan",
            "Inceptus.DiagramEngine.Contracts.Presentation.IPresentationViewProvider",
            "Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewProviderRegistration",
            "Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewResolution",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DHitTestMode",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DHitTestPolicy",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneConfiguration",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContribution",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributionContext",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributionResult",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributionStage",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorDescriptor",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScenePanDependency",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneMoveGestureDependency",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneTransientDependency",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScenePlacementDependency",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DTransientInteractionMetadata",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorId",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorRegistration",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneGeometry",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneExportMetadata",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneGeometryKind",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneLayer",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneObjectIdentity",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneOriginCategory",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneOriginTrace",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DScenePresentationContext",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationMapping",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationRoute",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DConnectorPresentationRoutingRequest",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialEditKind",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialEditRequest",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlanResult",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlannerCatalog",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialEditPlannerRegistration",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialPresentationPlan",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialRegion",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialRegionId",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSpatialVisualPlacement",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneStyle",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DNodePresentationMetadata",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSemanticSceneInteractionMetadata",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DTextAlignment",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DTextBaseline",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DSceneContributor",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DConnectorPresentationRouter",
            "Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DSpatialEditPlanner",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationCatalog",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationId",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationPlan",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationPlanResult",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationRegistration",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionCreationSourceRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ConnectionToolState",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ElementConnectionEndpoint",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ElementConnectionPreview",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ElementConnectionSourceRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ElementConnectionCreationRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ElementConnectionCandidate",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.IElementConnectionCreationCommandFactory",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.AnchorConnectionTargetEligibilityRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ConnectorTargetAnchorAcquisition",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.ConnectorTargetEdgeResolver",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.IAnchorConnectionCreationCommandFactory",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.IAnchorConnectionTargetEligibility",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionKind",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionRejectionReason",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionRequest",
            "Inceptus.DiagramEngine.Contracts.ConnectionCreation.TargetAnchorAcquisitionResult",
            "Inceptus.DiagramEngine.Contracts.Commands.AddConnectorAnchorCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.CompoundDocumentCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.AuthoritativeDocumentComponent",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandCategory",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandExecutionDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandExecutionResult",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandExecutionStatus",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandHandlerRegistration",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandHandlerResult",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandPipelineInvalidation",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandValidationDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandValidationResult",
            "Inceptus.DiagramEngine.Contracts.Commands.CommandValidatorRegistration",
            "Inceptus.DiagramEngine.Contracts.Commands.CreateTopLevelDocumentScopeCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.DocumentChangedEvent",
            "Inceptus.DiagramEngine.Contracts.Commands.ICommand",
            "Inceptus.DiagramEngine.Contracts.Commands.ICommandPipelineInvalidation",
            "Inceptus.DiagramEngine.Contracts.Commands.ICommandHandler",
            "Inceptus.DiagramEngine.Contracts.Commands.ICommandEnvelopeValidator",
            "Inceptus.DiagramEngine.Contracts.Commands.ICommandValidator",
            "Inceptus.DiagramEngine.Contracts.Commands.IDocumentChangedSubscriber",
            "Inceptus.DiagramEngine.Contracts.Commands.MoveLabelCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.MoveVisualStateCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.MoveVisualStatesCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.ModelProfileAvailabilityChange",
            "Inceptus.DiagramEngine.Contracts.Commands.NodeGeometryPipelineImpact",
            "Inceptus.DiagramEngine.Contracts.Commands.PipelineInvalidation",
            "Inceptus.DiagramEngine.Contracts.Commands.RemoveConnectorAnchorCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.ResizeVisualStateCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.SetModelProfileAvailabilityCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateConnectionRouteCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateBoundaryAttachmentCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateDocumentPublicationCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateNodeLabelVisualOverrideCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateSemanticElementNameCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.UpdateSemanticElementPropertyCommand",
            "Inceptus.DiagramEngine.Contracts.Commands.VisualStateMove",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionCatalog",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionApplicabilityRequest",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionDefinition",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionId",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionPlan",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.CanvasBackgroundActionRequest",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneCommandActionCatalog",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneCommandActionDefinition",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneCommandActionId",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneCommandActionPlan",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneCommandActionRequest",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneViewActionCatalog",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneViewActionDefinition",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneViewActionId",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneViewActionPlan",
            "Inceptus.DiagramEngine.Contracts.ContextMenus.SemanticSceneViewActionRequest",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionCatalog",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionId",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionPlan",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionPlanResult",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionRegistration",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionRequest",
            "Inceptus.DiagramEngine.Contracts.Deletion.DiagramDeletionTargetKind",
            "Inceptus.DiagramEngine.Contracts.Deletion.IDiagramDeletionCommandFactory",
            "Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic",
            "Inceptus.DiagramEngine.Contracts.Diagnostics.DiagnosticSeverity",
            "Inceptus.DiagramEngine.Contracts.Documents.DocumentSnapshot",
            "Inceptus.DiagramEngine.Contracts.Documents.DocumentPublicationSnapshot",
            "Inceptus.DiagramEngine.Contracts.Documents.IDocumentView",
            "Inceptus.DiagramEngine.Contracts.EditorState.EditorFeedbackPresentationMode",
            "Inceptus.DiagramEngine.Contracts.EditorState.EditorFeedbackSnapshot",
            "Inceptus.DiagramEngine.Contracts.EditorState.EditorGestureSnapshot",
            "Inceptus.DiagramEngine.Contracts.EditorState.EditorStateSnapshot",
            "Inceptus.DiagramEngine.Contracts.EditorState.GridSettings",
            "Inceptus.DiagramEngine.Contracts.EditorState.IGridSettingsSource",
            "Inceptus.DiagramEngine.Contracts.EditorState.ViewportSnapshot",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionCatalog",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionId",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionPlan",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionPlanResult",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionRegistration",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionRequest",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.ConnectorEndpointReconnectionStartRequest",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.IConnectorEndpointReconnectionCommandFactory",
            "Inceptus.DiagramEngine.Contracts.EndpointReconnection.IConnectorEndpointReconnectionCandidateEligibility",
            "Inceptus.DiagramEngine.Contracts.Geometry.DocumentGeometryBoundary",
            "Inceptus.DiagramEngine.Contracts.Geometry.GridGeometry",
            "Inceptus.DiagramEngine.Contracts.Geometry.Matrix2D",
            "Inceptus.DiagramEngine.Contracts.Geometry.PointD",
            "Inceptus.DiagramEngine.Contracts.Geometry.RectD",
            "Inceptus.DiagramEngine.Contracts.Geometry.SizeD",
            "Inceptus.DiagramEngine.Contracts.Geometry.VectorD",
            "Inceptus.DiagramEngine.Contracts.History.CommandHistoryPolicyRegistration",
            "Inceptus.DiagramEngine.Contracts.History.CommandHistoryPreparationResult",
            "Inceptus.DiagramEngine.Contracts.History.HistoryDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.History.HistoryOperationResult",
            "Inceptus.DiagramEngine.Contracts.History.HistoryOperationStatus",
            "Inceptus.DiagramEngine.Contracts.History.HistoryRecordingBehavior",
            "Inceptus.DiagramEngine.Contracts.History.HistoryStatus",
            "Inceptus.DiagramEngine.Contracts.History.ICommandHistoryPolicy",
            "Inceptus.DiagramEngine.Contracts.History.IHistoryCommandFactory",
            "Inceptus.DiagramEngine.Contracts.Layout.ILayoutAlgorithm",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutAlgorithmRegistration",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutAlgorithmResult",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutComputation",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutContext",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutExecutionResult",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutExecutionStatus",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutGroupGeometry",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutNodeGeometry",
            "Inceptus.DiagramEngine.Contracts.Layout.LayoutResult",
            "Inceptus.DiagramEngine.Contracts.Metadata.DocumentMetadataSnapshot",
            "Inceptus.DiagramEngine.Contracts.Metadata.IDocumentMetadataView",
            "Inceptus.DiagramEngine.Contracts.Primitives.AlgorithmId",
            "Inceptus.DiagramEngine.Contracts.Primitives.CommandTypeId",
            "Inceptus.DiagramEngine.Contracts.Primitives.CommandValidatorId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ConnectorAnchorId",
            "Inceptus.DiagramEngine.Contracts.Primitives.DocumentId",
            "Inceptus.DiagramEngine.Contracts.Primitives.DocumentScopeId",
            "Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision",
            "Inceptus.DiagramEngine.Contracts.Primitives.ElementPropertyFieldId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ModelProfileId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ProjectedObjectId",
            "Inceptus.DiagramEngine.Contracts.Primitives.PredefinedConnectorAnchorDefinitionId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ProjectionRuleId",
            "Inceptus.DiagramEngine.Contracts.Primitives.SceneObjectId",
            "Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId",
            "Inceptus.DiagramEngine.Contracts.Primitives.SemanticTypeId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ToolboxGroupId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ToolboxItemId",
            "Inceptus.DiagramEngine.Contracts.Primitives.ToolboxSectionId",
            "Inceptus.DiagramEngine.Contracts.Primitives.VisualStateId",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileCatalog",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileDefinition",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileElementViewStateKey",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileElementAssignmentSnapshot",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileElementPresentationSnapshot",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileElementViewStateSnapshot",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileStateSnapshot",
            "Inceptus.DiagramEngine.Contracts.Profiles.ModelProfileViewStateSnapshot",
            "Inceptus.DiagramEngine.Contracts.Properties.ElementPropertiesSchema",
            "Inceptus.DiagramEngine.Contracts.Properties.ElementPropertiesSchemaCatalog",
            "Inceptus.DiagramEngine.Contracts.Properties.ElementPropertyEditorKind",
            "Inceptus.DiagramEngine.Contracts.Properties.ElementPropertyFieldDefinition",
            "Inceptus.DiagramEngine.Contracts.Properties.PropertyMap",
            "Inceptus.DiagramEngine.Contracts.Properties.PropertyValue",
            "Inceptus.DiagramEngine.Contracts.Properties.PropertyValueKind",
            "Inceptus.DiagramEngine.Contracts.Properties.SemanticPropertyMutationKind",
            "Inceptus.DiagramEngine.Contracts.Projection.ConnectorLabelPlacementIntent",
            "Inceptus.DiagramEngine.Contracts.Projection.ElementProjectionRuleInput",
            "Inceptus.DiagramEngine.Contracts.Projection.IProjectedObject",
            "Inceptus.DiagramEngine.Contracts.Projection.IProjectionRule",
            "Inceptus.DiagramEngine.Contracts.Projection.NodeLabelPlacement",
            "Inceptus.DiagramEngine.Contracts.Projection.NodeLabelPlacementKind",
            "Inceptus.DiagramEngine.Contracts.Projection.NodeLabelInteractionPolicy",
            "Inceptus.DiagramEngine.Contracts.Projection.NodeGeometryInteractionPolicy",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedEdge",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedBoundaryAttachment",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedGraph",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedGroup",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedLabel",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedNode",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedObjectIdentity",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedObjectKind",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedPlacementHint",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedPort",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedConnectorAnchor",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectedConnectorAnchorMetadata",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionContext",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionResult",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionRuleContribution",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionRuleInput",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionRuleRegistration",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionRuleResult",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionSourceKind",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionSourceTrace",
            "Inceptus.DiagramEngine.Contracts.Projection.ProjectionStatus",
            "Inceptus.DiagramEngine.Contracts.Projection.RelationshipProjectionRuleInput",
            "Inceptus.DiagramEngine.Contracts.Publishing.IPublishedExecutionPolicy",
            "Inceptus.DiagramEngine.Contracts.Publishing.IPublishedNodeDataMapper",
            "Inceptus.DiagramEngine.Contracts.Publishing.IPublishedTokenRoleClassifier",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedAttachedTrigger",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedCommunicationRoute",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedExecutionDescription",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedExecutionMetadata",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedNodeData",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedTokenRole",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedTokenRoleClassification",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedTokenRoleClassificationRequest",
            "Inceptus.DiagramEngine.Contracts.Publishing.PublishedTriggerKind",
            "Inceptus.DiagramEngine.Contracts.Routing.IRoutingAlgorithm",
            "Inceptus.DiagramEngine.Contracts.Routing.IRoutingInputPreparer",
            "Inceptus.DiagramEngine.Contracts.Routing.PreparedRoutingInput",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutedConnectorGeometry",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingAlgorithmRegistration",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingAlgorithmResult",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingComputation",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingContext",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingObstacleDomain",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingExecutionResult",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingExecutionStatus",
            "Inceptus.DiagramEngine.Contracts.Routing.RoutingResult",
            "Inceptus.DiagramEngine.Contracts.ScopeNavigation.IScopeNavigationContribution",
            "Inceptus.DiagramEngine.Contracts.ScopeNavigation.ScopeNavigationCatalog",
            "Inceptus.DiagramEngine.Contracts.ScopeNavigation.ScopeNavigationRegistration",
            "Inceptus.DiagramEngine.Contracts.Semantics.ISemanticModelView",
            "Inceptus.DiagramEngine.Contracts.Semantics.DocumentScopeSnapshot",
            "Inceptus.DiagramEngine.Contracts.Semantics.SemanticElementSnapshot",
            "Inceptus.DiagramEngine.Contracts.Semantics.SemanticElementScopeMembershipSnapshot",
            "Inceptus.DiagramEngine.Contracts.Semantics.SemanticElementContainmentKind",
            "Inceptus.DiagramEngine.Contracts.Semantics.SemanticModelSnapshot",
            "Inceptus.DiagramEngine.Contracts.Semantics.SemanticRelationshipSnapshot",
            "Inceptus.DiagramEngine.Contracts.Text.ITextMetricsService",
            "Inceptus.DiagramEngine.Contracts.Text.TextDirection",
            "Inceptus.DiagramEngine.Contracts.Text.TextFontStyle",
            "Inceptus.DiagramEngine.Contracts.Text.TextMeasurementRequest",
            "Inceptus.DiagramEngine.Contracts.Text.TextMeasurementResult",
            "Inceptus.DiagramEngine.Contracts.Text.TextMeasurementStatus",
            "Inceptus.DiagramEngine.Contracts.Text.TextMetrics",
            "Inceptus.DiagramEngine.Contracts.Text.TextMetricsDiagnosticCodes",
            "Inceptus.DiagramEngine.Contracts.Text.TextWritingMode",
            "Inceptus.DiagramEngine.Contracts.Toolbox.IToolboxPlacementCommandFactory",
            "Inceptus.DiagramEngine.Contracts.Toolbox.IToolboxPlacementCandidateProvider",
            "Inceptus.DiagramEngine.Contracts.Toolbox.IToolboxPlacementPreviewProvider",
            "Inceptus.DiagramEngine.Contracts.Creation.DocumentCreationIdentity",
            "Inceptus.DiagramEngine.Contracts.Creation.IDocumentCreationIdentityProvider",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxCatalog",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxContribution",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxGroupDefinition",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxIconDescriptor",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxItemDefinition",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxConnectionItemDefinition",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxSectionDefinition",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementCatalog",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementCandidate",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementPlan",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementPlanResult",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementRegistration",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementRequest",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementTarget",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementPreview",
            "Inceptus.DiagramEngine.Contracts.Toolbox.ToolboxPlacementPreviewRequest",
            "Inceptus.DiagramEngine.Contracts.Validation.IModelValidationRule",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationCatalog",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationContext",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationIssue",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationIssueId",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationRuleId",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationSeverity",
            "Inceptus.DiagramEngine.Contracts.Validation.ModelValidationTarget",
            "Inceptus.DiagramEngine.Contracts.Validation.ValidationSnapshot",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchor",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectionCandidateMetadata",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorEndpointDecoration",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorVisualDescriptor",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorVisualMetadata",
            "Inceptus.DiagramEngine.Contracts.Visuals.BoundaryAttachmentPlacement",
            "Inceptus.DiagramEngine.Contracts.Visuals.BoundaryAttachmentSide",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorGeometryResolver",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorInsertion",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorOccupancy",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorPolicyMode",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorReferenceIdentity",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorRole",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorRoleCapability",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorAnchorSide",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorEndpointKind",
            "Inceptus.DiagramEngine.Contracts.Visuals.EdgeConnectorAnchorPolicy",
            "Inceptus.DiagramEngine.Contracts.Visuals.ElementConnectorAnchorPolicy",
            "Inceptus.DiagramEngine.Contracts.Visuals.ElementConnectorAnchorPolicyEvaluator",
            "Inceptus.DiagramEngine.Contracts.Visuals.ElementConnectorAnchorPolicyRegistration",
            "Inceptus.DiagramEngine.Contracts.Visuals.ElementConnectorAnchorPolicyRegistry",
            "Inceptus.DiagramEngine.Contracts.Visuals.ElementConnectorAnchorResolver",
            "Inceptus.DiagramEngine.Contracts.Visuals.IElementConnectorAnchorPolicyProvider",
            "Inceptus.DiagramEngine.Contracts.Visuals.IVisualModelView",
            "Inceptus.DiagramEngine.Contracts.Visuals.NodeLabelVisualOverride",
            "Inceptus.DiagramEngine.Contracts.Visuals.PredefinedConnectorAnchorDefinition",
            "Inceptus.DiagramEngine.Contracts.Visuals.ResolvedConnectorAnchor",
            "Inceptus.DiagramEngine.Contracts.Visuals.ResolvedConnectorAnchorKind",
            "Inceptus.DiagramEngine.Contracts.Visuals.VisualModelSnapshot",
            "Inceptus.DiagramEngine.Contracts.Visuals.VisualPlacementMode",
            "Inceptus.DiagramEngine.Contracts.Visuals.ConnectorLabelPlacement",
            "Inceptus.DiagramEngine.Contracts.Visuals.VisualStateSnapshot",
        ];
        var actualTypeNames = LoadProductionAssembly(Contracts)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTypeNames.Order(StringComparer.Ordinal), actualTypeNames);
    }

    [Fact]
    public void Canvas2DExportsExactlyTheApprovedPhaseN104SessionSceneRenderingInteractionAndPublishingTypes()
    {
        string[] expectedTypeNames =
        [
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSession",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.ConnectorRoutingStatePreparer",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionAttachResult",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionAttachStatus",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionConfiguration",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionDiagnosticCodes",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionGeneration",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionOperationResult",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionOperationStatus",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionStateChangedEventArgs",
            "Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionStatus",
            "Inceptus.DiagramEngine.Canvas2D.Export.Canvas2DGraphicalExporter",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportArtifact",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportFormat",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportOptions",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportPreparationResult",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportPreparer",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportResources",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportResult",
            "Inceptus.DiagramEngine.Canvas2D.Export.GraphicalExportStatus",
            "Inceptus.DiagramEngine.Canvas2D.Export.PreparedGraphicalScene",
            "Inceptus.DiagramEngine.Canvas2D.Export.SvgGraphicalExporter",
            "Inceptus.DiagramEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestConfiguration",
            "Inceptus.DiagramEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestResult",
            "Inceptus.DiagramEngine.Canvas2D.HitTesting.Canvas2DSceneHitTestService",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DConnectorAnchorContextAction",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DConnectorAnchorContextActionKind",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DConnectorRouteContextAction",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DConnectorRouteContextActionKind",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DInteractionController",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DInteractionDiagnosticCodes",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DInteractionResult",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DInteractionStatus",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DNodeLabelContextAction",
            "Inceptus.DiagramEngine.Canvas2D.Interaction.Canvas2DPointerInput",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.EditingSessionPresentationCapture",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.EditingSessionPresentationCaptureResult",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.EditingSessionPresentationCaptureStatus",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedInitialView",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedMatrix",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPoint",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPublication",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPresentation",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPresentationConnector",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPresentationItem",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedPresentationNode",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedProcessPackage",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedProcessPackageBuildResult",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedProcessPackageBuilder",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedProcessSnapshot",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedRect",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedRuntimeConfiguration",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedSource",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedTokenGraph",
            "Inceptus.DiagramEngine.Canvas2D.Publishing.PublishedTokenNode",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DFontResource",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRenderer",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRendererConfiguration",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRendererDiagnosticCodes",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRendererOperationStatus",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DRendererResult",
            "Inceptus.DiagramEngine.Canvas2D.Rendering.Canvas2DSurfaceSize",
            "Inceptus.DiagramEngine.Canvas2D.Scene.Canvas2DScene",
            "Inceptus.DiagramEngine.Canvas2D.Scene.Canvas2DSceneBuilder",
            "Inceptus.DiagramEngine.Canvas2D.Scene.Canvas2DSceneBuildResult",
            "Inceptus.DiagramEngine.Canvas2D.Scene.Canvas2DSceneBuildStatus",
            "Inceptus.DiagramEngine.Canvas2D.Scene.Canvas2DSpatialLookup",
        ];
        var actualTypeNames = LoadProductionAssembly(Canvas2D)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .Order(StringComparer.Ordinal);

        Assert.Equal(expectedTypeNames.Order(StringComparer.Ordinal), actualTypeNames);
    }

    [Fact]
    public void BpmnBlazorExportsTheApprovedFacadeWhileKeepingItsCompositionAndHostInternal()
    {
        string[] requiredTypeNames =
        [
            "Inceptus.DiagramEngine.Bpmn.Blazor.IBpmnModelerStartupDocumentProvider",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Components.DocumentCanvas",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Components.ToolboxPanel",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerDocumentChangeKind",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerDisplayMode",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerOperation",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerReadyEventArgs",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerDocumentChangedEventArgs",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerOperationFailedEventArgs",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerOperationStatus",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerDocumentResult",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerFileArtifact",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerFileResult",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerNativeImportOptions",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerNativeSaveStatus",
            "Inceptus.DiagramEngine.Bpmn.Blazor.BpmnModelerSaveCheckpoint",
            "Microsoft.Extensions.DependencyInjection.BpmnModelerServiceCollectionExtensions",
        ];
        var assembly = LoadProductionAssembly(BpmnBlazor);
        var actualTypeNames = assembly
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.All(requiredTypeNames, typeName => Assert.Contains(typeName, actualTypeNames));

        string[] internalTypeNames =
        [
            "Inceptus.DiagramEngine.Bpmn.Blazor.Composition.BpmnModelerComposition",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Composition.BpmnModelerCompositionFactory",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.DocumentCanvasHost",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.BpmnModelerFacade",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.BpmnModelerDocumentNotificationSource",
            "Inceptus.DiagramEngine.Bpmn.Blazor.Presentation.BpmnModelerNotifications",
        ];
        Assert.All(internalTypeNames, typeName =>
            Assert.False(assembly.GetType(typeName, throwOnError: true)!.IsVisible));
    }

    [Fact]
    public void DemoBlazorExportsOnlyItsApplicationComponents()
    {
        var actualTypeNames = LoadProductionAssembly(Blazor)
            .GetExportedTypes()
            .Select(type => type.FullName)
            .Where(name => name is not null)
            .Cast<string>();

        Assert.Equal(["Inceptus.DiagramEngine.Blazor.App", "Inceptus.DiagramEngine.Blazor.Demo.BpmnDemoHost"], actualTypeNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void ContractsExposeOnlyTwoDimensionalGeometry()
    {
        string[] forbiddenFullNames =
        [
            "System.Numerics.Matrix4x4",
            "System.Numerics.Plane",
            "System.Numerics.Quaternion",
            "System.Numerics.Vector3",
            "System.Windows.Media.Media3D.Matrix3D",
            "System.Windows.Media.Media3D.Point3D",
            "System.Windows.Media.Media3D.Transform3D",
            "System.Windows.Media.Media3D.Vector3D",
        ];

        var contractsAssembly = typeof(PointD).Assembly;
        string[] expectedGeometryTypes =
        [
            typeof(GridGeometry).FullName!,
            typeof(DocumentGeometryBoundary).FullName!,
            typeof(Matrix2D).FullName!,
            typeof(PointD).FullName!,
            typeof(RectD).FullName!,
            typeof(SizeD).FullName!,
            typeof(VectorD).FullName!,
        ];
        var actualGeometryTypes = contractsAssembly
            .GetExportedTypes()
            .Where(type => type.Namespace == "Inceptus.DiagramEngine.Contracts.Geometry")
            .Select(type => type.FullName!)
            .Order(StringComparer.Ordinal);
        var forbiddenNamedTypes = contractsAssembly
            .GetExportedTypes()
            .Where(type => type.Name.EndsWith("3D", StringComparison.Ordinal) ||
                type.Name.Contains("ThreeD", StringComparison.Ordinal))
            .ToArray();
        var forbiddenSignatureTypes = GetPublicSignatureTypes(contractsAssembly)
            .Where(type => forbiddenFullNames.Contains(type.FullName, StringComparer.Ordinal))
            .ToArray();

        Assert.Equal(expectedGeometryTypes.Order(StringComparer.Ordinal), actualGeometryTypes);
        Assert.Empty(forbiddenNamedTypes);
        Assert.Empty(forbiddenSignatureTypes);
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

            throw new DirectoryNotFoundException("Could not locate the repository root.");
        }
    }

    private static Dictionary<string, string> GetProductionProjectPaths() =>
        Directory
            .GetFiles(Path.Combine(RepositoryRoot, "src"), "*.csproj", SearchOption.AllDirectories)
            .ToDictionary(
                path => Path.GetFileNameWithoutExtension(path),
                path => path,
                StringComparer.Ordinal);

    private static string FindProject(string projectName)
    {
        var searchRoot = projectName.EndsWith("Tests", StringComparison.Ordinal)
            ? Path.Combine(RepositoryRoot, "tests")
            : Path.Combine(RepositoryRoot, "src");
        var matches = Directory.GetFiles(searchRoot, $"{projectName}.csproj", SearchOption.AllDirectories);

        return Assert.Single(matches);
    }

    private static IEnumerable<string> ReadProjectReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new InvalidOperationException("The project path has no directory.");

        return document
            .Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => (string?)element.Attribute("Include"))
            .Where(include => include is not null)
            .Cast<string>()
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, include)));
    }

    private static bool HasCycle(
        string project,
        IReadOnlyDictionary<string, string[]> graph,
        ISet<string> visited,
        ISet<string> active)
    {
        if (active.Contains(project))
        {
            return true;
        }

        if (!visited.Add(project))
        {
            return false;
        }

        active.Add(project);

        foreach (var dependency in graph[project])
        {
            if (HasCycle(dependency, graph, visited, active))
            {
                return true;
            }
        }

        active.Remove(project);
        return false;
    }

    private static string? ReadElementValue(XContainer document, string name) =>
        document.Descendants().Single(element => element.Name.LocalName == name).Value;

    private static Assembly LoadProductionAssembly(string name) =>
        name == Contracts ? typeof(DocumentId).Assembly : Assembly.Load(name);

    private static IEnumerable<Type> GetPublicSignatureTypes(Assembly assembly)
    {
        foreach (var type in assembly.GetExportedTypes())
        {
            foreach (var signatureType in ExpandType(type))
            {
                yield return signatureType;
            }

            if (type.BaseType is not null)
            {
                foreach (var signatureType in ExpandType(type.BaseType))
                {
                    yield return signatureType;
                }
            }

            foreach (var implementedInterface in type.GetInterfaces())
            {
                foreach (var signatureType in ExpandType(implementedInterface))
                {
                    yield return signatureType;
                }
            }

            foreach (var member in type.GetMembers(
                         BindingFlags.Public | BindingFlags.Instance |
                         BindingFlags.Static | BindingFlags.DeclaredOnly))
            {
                foreach (var memberType in GetMemberTypes(member))
                {
                    foreach (var signatureType in ExpandType(memberType))
                    {
                        yield return signatureType;
                    }
                }
            }
        }
    }

    private static IEnumerable<Type> GetMemberTypes(MemberInfo member)
    {
        switch (member)
        {
            case ConstructorInfo constructor:
                return constructor.GetParameters().Select(parameter => parameter.ParameterType);
            case MethodInfo method:
                return new[] { method.ReturnType }
                    .Concat(method.GetParameters().Select(parameter => parameter.ParameterType))
                    .Concat(method.GetGenericArguments().SelectMany(argument => argument.GetGenericParameterConstraints()));
            case PropertyInfo property:
                return new[] { property.PropertyType }
                    .Concat(property.GetIndexParameters().Select(parameter => parameter.ParameterType));
            case FieldInfo field:
                return [field.FieldType];
            case EventInfo @event when @event.EventHandlerType is not null:
                return [@event.EventHandlerType];
            case Type nestedType:
                return [nestedType];
            default:
                return [];
        }
    }

    private static IEnumerable<Type> ExpandType(Type type)
    {
        yield return type;

        if (type.HasElementType && type.GetElementType() is { } elementType)
        {
            foreach (var expanded in ExpandType(elementType))
            {
                yield return expanded;
            }
        }

        foreach (var genericArgument in type.GetGenericArguments())
        {
            foreach (var expanded in ExpandType(genericArgument))
            {
                yield return expanded;
            }
        }
    }

    private static bool IsBrowserOrDomType(Type type)
    {
        string[] forbiddenNamespacePrefixes =
        [
            "Microsoft.AspNetCore.Components",
            "Microsoft.JSInterop",
            "System.Runtime.InteropServices.JavaScript",
        ];
        string[] forbiddenTypeNames =
        [
            "CanvasGradient",
            "CanvasPattern",
            "CanvasRenderingContext2D",
            "DOMEvent",
            "DOMNode",
            "ElementReference",
            "HTMLElement",
            "HTMLCanvasElement",
            "ImageBitmap",
            "MouseEvent",
            "Path2D",
            "PointerEvent",
            "SVGElement",
            "WebGLRenderingContext",
            "WheelEvent",
        ];

        return forbiddenNamespacePrefixes.Any(prefix =>
                   type.Namespace?.StartsWith(prefix, StringComparison.Ordinal) == true) ||
               forbiddenTypeNames.Contains(type.Name, StringComparer.Ordinal);
    }
}
