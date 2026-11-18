using System.Reflection;
using System.Runtime.CompilerServices;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class LifecycleObservationArchitectureTests
{
    private static readonly string[] CanvasFriends = ["Inceptus.DiagramEngine.Bpmn.Blazor", "Inceptus.DiagramEngine.IntegrationTests", "Inceptus.DiagramEngine.UnitTests"];
    private static readonly string[] PresentationFriends = ["Inceptus.DiagramEngine.Blazor", "Inceptus.DiagramEngine.IntegrationTests", "Inceptus.DiagramEngine.TestExample", "Inceptus.DiagramEngine.UnitTests"];
    // The owner explicitly approved this one assembly-identity declaration. It adds no
    // notation policy/reference. Keep every other occurrence subject to the existing guards.
    internal static string WithoutApprovedFriendDeclaration(string source) => source.Replace(
        "[assembly: InternalsVisibleTo(\"Inceptus.DiagramEngine.Bpmn.Blazor\")]", string.Empty, StringComparison.Ordinal);

    [Fact]
    public void LifecycleObservationUsesOnlyTheApprovedFriendBoundaries()
    {
        Assert.Equal(CanvasFriends,
            typeof(EditingSession).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(a => a.AssemblyName).Order(StringComparer.Ordinal));
        Assert.Equal(PresentationFriends,
            typeof(InceptusBpmnModeler).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>()
                .Select(a => a.AssemblyName).Order(StringComparer.Ordinal));
        Assert.DoesNotContain(typeof(EditingSession).Assembly.GetReferencedAssemblies(),
            a => a.Name!.Contains("Bpmn", StringComparison.Ordinal));
    }

    [Fact]
    public void LifecycleObservationAddsNoPublicDiagnosticTypesOrModelerMembers()
    {
        Assert.DoesNotContain(typeof(EditingSession).Assembly.GetExportedTypes(), t => t.Name.Contains("LifecycleObservation", StringComparison.Ordinal));
        Assert.DoesNotContain(typeof(InceptusBpmnModeler).Assembly.GetExportedTypes(), t => t.Name.Contains("LifecycleObservation", StringComparison.Ordinal));
        Assert.Null(typeof(InceptusBpmnModeler).GetMethod("BeginLifecycleObservation", BindingFlags.Public | BindingFlags.Instance));
        Assert.Null(typeof(EditingSession).GetProperty("LifecycleObservation", BindingFlags.Public | BindingFlags.Instance));
        Assert.DoesNotContain(typeof(InceptusBpmnModeler).Assembly.GetExportedTypes(), t => t.Name.Contains("ActionObservation", StringComparison.Ordinal));
        Assert.Null(typeof(InceptusBpmnModeler).GetMethod("BeginApplicationActionObservation", BindingFlags.Public | BindingFlags.Instance));
    }
}
