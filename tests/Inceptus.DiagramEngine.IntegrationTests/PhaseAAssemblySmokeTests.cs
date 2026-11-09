using System.Reflection;

namespace Inceptus.DiagramEngine.IntegrationTests;

public sealed class PhaseAAssemblySmokeTests
{
    [Fact]
    public void AllProductionAssembliesCanBeResolvedTogether()
    {
        string[] assemblyNames =
        [
            "Inceptus.DiagramEngine.Contracts",
            "Inceptus.DiagramEngine.Runtime",
            "Inceptus.DiagramEngine.Canvas2D",
            "Inceptus.DiagramEngine.Bpmn",
            "Inceptus.DiagramEngine.Blazor",
        ];

        foreach (var assemblyName in assemblyNames)
        {
            var assembly = Assembly.Load(assemblyName);

            Assert.Equal(assemblyName, assembly.GetName().Name);
        }
    }
}

