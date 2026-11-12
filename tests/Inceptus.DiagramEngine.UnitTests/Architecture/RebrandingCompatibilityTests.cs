using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Inceptus.Bpmn.Integration;
using Inceptus.DiagramEngine.Bpmn;
using Inceptus.DiagramEngine.Bpmn.Blazor.Components;
using Inceptus.DiagramEngine.Canvas2D.EditingSession;
using Inceptus.DiagramEngine.Contracts.Documents;
using Inceptus.DiagramEngine.Organizational;
using Inceptus.DiagramEngine.Runtime.Documents;

namespace Inceptus.DiagramEngine.UnitTests.Architecture;

public sealed class RebrandingCompatibilityTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(DocumentSnapshot).Assembly, typeof(Document).Assembly,
        typeof(BpmnPluginRegistration).Assembly, typeof(OrganizationalPluginRegistration).Assembly,
        typeof(EditingSession).Assembly, typeof(InceptusBpmnModeler).Assembly,
        typeof(BpmnDocumentSerializer).Assembly,
    ];

    // Captured from DEVELOPMENT 0d943a29eb721d9487b6d838ed2357a519ca367d before migration.
    // The approved namespace prefixes, private async state-machine ordinals and compiler-emitted
    // Debug async stepping metadata are normalized;
    // public signatures, attribute identities, declaring types and member names stay intact.
    [Fact]
    public void PublicAndProtectedApiRetainsThePreMigrationContract()
    {
        var current = Assemblies.SelectMany(DescribeAssembly).ToHashSet(StringComparer.Ordinal);
        // Stage 4 is strictly additive over the accepted Stage 3 API. Require each exact
        // approved addition before applying the existing Stage 3 compatibility proof.
        foreach (var addition in Stage4PublicApi.Added)
            Assert.True(current.Remove(addition), $"Missing approved Stage 4 signature: {addition}");
        // Exact approved Stage 3 delta, including Roslyn's equivalent relocation of nullable
        // context metadata on the root component. Require every new signature, then reconstruct
        // the historical contract for its original fingerprint. No signatures are ignored.
        foreach (var addition in Stage3PublicApi.Added)
            Assert.True(current.Remove(addition), $"Missing approved Stage 3 signature: {addition}");
        foreach (var replaced in Stage3PublicApi.Replaced)
            Assert.True(current.Add(replaced), $"Unexpected duplicate historical signature: {replaced}");
        var lines = current.Order(StringComparer.Ordinal).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
        // Derived from the original 8,912-line baseline-api.txt retained in
        // TestResults/phase-a-rebranding, whose unnormalized hash is
        // 1E44A0B57BC40D0B094F23A9658283916B49AB3B635CF77FC174EFA275DE752A.
        // Normalizing its 24 async ordinals produces this hash, independently of the candidate.
        Assert.Equal("186B70B921A28E79017C6976DDFE2038FE634C31AB0558DEE4C5446C49BFAE6D", hash);
    }

    [Fact]
    public void AsyncOrdinalNormalizationPreservesActualTypeMemberAndAttributeIdentity()
    {
        const string original = "[System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(Example.Session+<ExecuteAsync>d__12))]";
        const string renumbered = "[System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(Example.Session+<ExecuteAsync>d__153))]";
        var normalized = NormalizeAsyncStateMachineOrdinal(original);

        Assert.Equal(normalized, NormalizeAsyncStateMachineOrdinal(renumbered));
        Assert.NotEqual(normalized, NormalizeAsyncStateMachineOrdinal(
            original.Replace("Example.Session", "Example.OtherSession", StringComparison.Ordinal)));
        Assert.NotEqual(normalized, NormalizeAsyncStateMachineOrdinal(
            original.Replace("ExecuteAsync", "OtherAsync", StringComparison.Ordinal)));

        var otherAttribute = original.Replace("AsyncStateMachineAttribute", "OtherAttribute", StringComparison.Ordinal);
        Assert.Equal(otherAttribute, NormalizeAsyncStateMachineOrdinal(otherAttribute));
        var nonOrdinal = original.Replace("d__12", "d__custom", StringComparison.Ordinal);
        Assert.Equal(nonOrdinal, NormalizeAsyncStateMachineOrdinal(nonOrdinal));
    }

    [Fact]
    public void AsyncMethodNormalizationPreservesStateMachineIdentityAndOtherAttributes()
    {
        var method = typeof(RebrandingCompatibilityTests).GetMethod(
            nameof(AsyncMetadataProbeAsync), BindingFlags.Static | BindingFlags.NonPublic)!;
        var attributes = MethodAttributes(method);

        Assert.DoesNotContain(nameof(DebuggerStepThroughAttribute), attributes, StringComparison.Ordinal);
        Assert.Contains("[System.Diagnostics.DebuggerHiddenAttribute()]", attributes, StringComparison.Ordinal);
        Assert.Contains(
            "[System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(" +
            typeof(RebrandingCompatibilityTests).FullName + "+<AsyncMetadataProbeAsync>d__N))]",
            attributes, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(nameof(ExplicitSteppingMetadataProbe))]
    [InlineData(nameof(ManualStateMachineMetadataProbe))]
    public void MethodNormalizationPreservesExplicitSteppingAttributes(string methodName)
    {
        var method = typeof(RebrandingCompatibilityTests).GetMethod(
            methodName, BindingFlags.Static | BindingFlags.NonPublic)!;

        Assert.Equal(Attributes(method.CustomAttributes), MethodAttributes(method));
        Assert.Contains("[System.Diagnostics.DebuggerStepThroughAttribute()]",
            MethodAttributes(method), StringComparison.Ordinal);
    }

    [DebuggerHidden]
    private static async Task AsyncMetadataProbeAsync() => await Task.Yield();

    [DebuggerStepThrough]
    private static void ExplicitSteppingMetadataProbe() { }

    [DebuggerStepThrough]
    [AsyncStateMachine(typeof(ManualStateMachine))]
    private static Task ManualStateMachineMetadataProbe() => Task.CompletedTask;

    private sealed class ManualStateMachine : IAsyncStateMachine
    {
        public void MoveNext() { }
        public void SetStateMachine(IAsyncStateMachine stateMachine) { }
    }

    private static IEnumerable<string> DescribeAssembly(Assembly assembly)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
            BindingFlags.Static | BindingFlags.DeclaredOnly;
        foreach (var type in assembly.GetExportedTypes().OrderBy(TypeName, StringComparer.Ordinal))
        {
            var prefix = TypeName(type);
            yield return $"T {prefix} {type.Attributes} : {TypeName(type.BaseType)} {string.Join(';', type.GetInterfaces().Select(TypeName).Order(StringComparer.Ordinal))} {Attributes(type.CustomAttributes)}";
            foreach (var parameter in type.GetGenericArguments().Where(static argument => argument.IsGenericParameter))
                yield return $"G {prefix} {GenericParameter(parameter)}";
            foreach (var method in type.GetMethods(flags).Cast<MethodBase>().Concat(type.GetConstructors(flags))
                .Where(static method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly))
            {
                var result = method is MethodInfo info ? TypeName(info.ReturnType) + Attributes(info.ReturnParameter.CustomAttributes) : string.Empty;
                yield return $"M {prefix} {method.Attributes} {method.Name} {result} ({string.Join(';', method.GetParameters().Select(Parameter))}) " +
                    $"{string.Join(';', (method is MethodInfo generic ? generic.GetGenericArguments() : []).Select(GenericParameter))} {MethodAttributes(method)}";
            }
            foreach (var field in type.GetFields(flags).Where(static field => field.IsPublic || field.IsFamily || field.IsFamilyOrAssembly))
                yield return $"F {prefix} {field.Attributes} {TypeName(field.FieldType)} {field.Name} {(field.IsLiteral ? Value(field.GetRawConstantValue()) : string.Empty)} {Attributes(field.CustomAttributes)}";
            foreach (var property in type.GetProperties(flags).Where(static property => property.GetAccessors(true).Any(method => method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)))
                yield return $"P {prefix} {TypeName(property.PropertyType)} {property.Name} {Attributes(property.CustomAttributes)}";
            foreach (var item in type.GetEvents(flags).Where(static item => item.AddMethod is { } method && (method.IsPublic || method.IsFamily || method.IsFamilyOrAssembly)))
                yield return $"E {prefix} {TypeName(item.EventHandlerType)} {item.Name} {Attributes(item.CustomAttributes)}";
        }
    }

    private static string GenericParameter(Type type) => $"{type.Name}:{type.GenericParameterAttributes}:{string.Join(',', type.GetGenericParameterConstraints().Select(TypeName))}";
    private static string Parameter(ParameterInfo parameter) => $"{parameter.Attributes}:{TypeName(parameter.ParameterType)}:{parameter.Name}:{(parameter.HasDefaultValue ? Value(parameter.RawDefaultValue) : string.Empty)}:{Attributes(parameter.CustomAttributes)}";
    private static string Attributes(IEnumerable<CustomAttributeData> attributes) => string.Join(';', attributes.Select(static attribute => NormalizeAsyncStateMachineOrdinal(Normalize(attribute.ToString()))).Order(StringComparer.Ordinal));

    private static string MethodAttributes(MethodBase method)
    {
        var stateMachine = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
        // Debug async methods use a compiler-generated private class and receive a stepping hint.
        // Release state machines are structs. Preserve explicit hints on other methods, all other
        // attributes, and the async attribute itself; the original Release API hash stays unchanged.
        var hasDebugAsyncStateMachine = stateMachine is { IsClass: true, IsNestedPrivate: true } &&
            stateMachine.DeclaringType == method.DeclaringType &&
            stateMachine.IsDefined(typeof(CompilerGeneratedAttribute), inherit: false) &&
            typeof(IAsyncStateMachine).IsAssignableFrom(stateMachine) &&
            stateMachine.Name.StartsWith($"<{method.Name}>d__", StringComparison.Ordinal);
        return Attributes(method.CustomAttributes.Where(attribute =>
            !hasDebugAsyncStateMachine || attribute.AttributeType != typeof(DebuggerStepThroughAttribute)));
    }

    private static string NormalizeAsyncStateMachineOrdinal(string attribute)
    {
        const string prefix = "[System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(";
        const string suffix = "))]";
        if (!attribute.StartsWith(prefix, StringComparison.Ordinal) ||
            !attribute.EndsWith(suffix, StringComparison.Ordinal)) return attribute;

        var marker = attribute.LastIndexOf(">d__", StringComparison.Ordinal);
        if (marker < prefix.Length) return attribute;
        var ordinalStart = marker + 4;
        var ordinalEnd = attribute.Length - suffix.Length;
        var ordinal = attribute.AsSpan(ordinalStart, ordinalEnd - ordinalStart);
        if (ordinal.IsEmpty || ordinal.IndexOfAnyExceptInRange('0', '9') >= 0) return attribute;

        return string.Concat(attribute.AsSpan(0, ordinalStart), "N", attribute.AsSpan(ordinalEnd));
    }

    private static string Value(object? value) => value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    private static string TypeName(Type? type)
    {
        if (type is null) return string.Empty;
        if (type.IsGenericParameter) return "!" + type.GenericParameterPosition;
        if (type.IsByRef) return TypeName(type.GetElementType()) + "&";
        if (type.IsPointer) return TypeName(type.GetElementType()) + "*";
        if (type.IsArray) return TypeName(type.GetElementType()) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        return Normalize(type.IsGenericType
            ? type.GetGenericTypeDefinition().FullName + "<" + string.Join(',', type.GetGenericArguments().Select(TypeName)) + ">"
            : type.FullName ?? type.Name);
    }

    private static string Normalize(string text)
    {
        foreach (var suffix in new[] { "Contracts", "Runtime", "Bpmn", "Organizational", "Canvas2D" })
            text = text.Replace("Inceptus.DocumentEngine." + suffix, "Inceptus.DiagramEngine." + suffix, StringComparison.Ordinal);
        return text;
    }

}
