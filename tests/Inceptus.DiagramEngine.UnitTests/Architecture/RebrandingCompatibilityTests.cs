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
        // The approved internal receipt uses a synchronous ValueTask forwarding wrapper
        // so it can retain the exact child cleanup Task. Its public signature is unchanged;
        // only this method's compiler-generated async attribute moves to the private core.
        const string disposal = "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.DocumentCanvas Public, Final, Virtual, HideBySig, VtableLayoutMask DisposeAsync System.Threading.Tasks.ValueTask ()  ";
        Assert.True(current.Remove(disposal), "Missing unchanged child disposal signature.");
        Assert.True(current.Add(disposal + "[System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(Inceptus.DiagramEngine.Bpmn.Blazor.Components.DocumentCanvas+<DisposeAsync>d__N))]"));
        // The approved application controls add exact signatures and cause Roslyn to move
        // equivalent root nullable metadata. Restore only those explicit records; all old
        // action constructors/delegates and the historical fingerprint remain required.
        foreach (var addition in ApplicationContextualActionsPublicApi.Added)
            Assert.True(current.Remove(addition), $"Missing approved application controls signature: {addition}");
        foreach (var replaced in ApplicationContextualActionsPublicApi.Replaced)
            Assert.True(current.Add(replaced), $"Unexpected duplicate application controls historical signature: {replaced}");
        // Approved composite resources add only immutable input values and an overload;
        // the original constructor and every historical signature remain required.
        foreach (var addition in CompositeDecorationPublicApiAdded)
            Assert.True(current.Remove(addition), $"Missing approved composite decoration signature: {addition}\n" +
                string.Join('\n', current.Where(static line => line.Contains("Canvas2DDecoration", StringComparison.Ordinal))));
        // The approved transient-decoration extension is strictly additive. Require every
        // exact new signature before proving the unchanged historical contract below.
        foreach (var addition in DecorationPublicApiAdded)
            Assert.True(current.Remove(addition), $"Missing approved decoration signature: {addition}");
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

    private static readonly string[] CompositeDecorationPublicApiAdded =
    [
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName get_Resources Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorId:providerId::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider:provider::;None:System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.String,Inceptus.DiagramEngine.Contracts.Properties.PropertyValue>>:values::[System.Runtime.CompilerServices.NullableAttribute(new Byte[4] { 2, 0, 1, 1 })];None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet:resources::)  ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Resources ",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.String:imageReference::;None:System.String:svgContent::)  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource Public, HideBySig, SpecialName get_ImageReference System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource Public, HideBySig, SpecialName get_SvgContent System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource System.String ImageReference ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource System.String SvgContent ",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource>:svgImages::)  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Public, Static, HideBySig, SpecialName get_Empty Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Public, HideBySig, SpecialName get_SvgImages System.Collections.Immutable.ImmutableSortedDictionary`2<System.String,Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource> ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet Empty ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationResourceSet System.Collections.Immutable.ImmutableSortedDictionary`2<System.String,Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationSvgResource> SvgImages ",
    ];

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


    // Exact approved transient-decoration delta; no historical signature is excluded or replaced.
    private static readonly string[] DecorationPublicApiAdded =
    [
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, SpecialName, RTSpecialName System.Int32 value__  ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Cancelled 3 ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Failed 5 ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Presented 0 ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Rejected 1 ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Superseded 4 ",
        "F Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Unavailable 2 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, Static, Literal, HasDefault System.Int32 MaximumDiagnostics 32 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, Static, Literal, HasDefault System.Int32 MaximumItems 10000 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Public, SpecialName, RTSpecialName System.Int32 value__  ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Element 0 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Relationship 1 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, SpecialName, RTSpecialName System.Int32 value__  ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind ConnectorLabel 3 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind ConnectorPath 2 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind NodeBody 0 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind NodeLabel 1 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Other 5 ",
        "F Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Static, Literal, HasDefault Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind SemanticRegion 4 ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Final, Virtual, HideBySig, VtableLayoutMask Equals System.Boolean (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:other::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, HideBySig <Clone>$ Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, HideBySig, SpecialName get_AttachmentVersion System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, HideBySig, SpecialName get_DocumentId Inceptus.DiagramEngine.Contracts.Primitives.DocumentId ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, HideBySig, SpecialName get_InstanceId System.Guid ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Static, HideBySig, SpecialName op_Equality System.Boolean (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:left::;None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Static, HideBySig, SpecialName op_Inequality System.Boolean (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:left::;None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Virtual, HideBySig Equals System.Boolean (None:System.Object:obj::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Virtual, HideBySig GetHashCode System.Int32 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Virtual, HideBySig ToString System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig CaptureDecorationAttachment Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment ()  [System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig ClearDecorationsAsync System.Threading.Tasks.ValueTask`1<Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:attachment::;None:System.Int64:inputRevision::;Optional, HasDefault:System.Threading.CancellationToken:cancellationToken:null:[System.Runtime.InteropServices.OptionalAttribute()])  ",
        "M Inceptus.DiagramEngine.Bpmn.Blazor.Components.InceptusBpmnModeler Public, HideBySig ReplaceDecorationsAsync System.Threading.Tasks.ValueTask`1<Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] (None:Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment:attachment::;None:System.Int64:inputRevision::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput:input::;Optional, HasDefault:System.Threading.CancellationToken:cancellationToken:null:[System.Runtime.InteropServices.OptionalAttribute()])  ",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Final, Virtual, HideBySig, VtableLayoutMask Equals System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:other::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig <Clone>$ Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_AttachmentId System.Guid ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_BaseSceneGeneration System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_DecorationsIncluded System.Boolean ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_DocumentId Inceptus.DiagramEngine.Contracts.Primitives.DocumentId ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_DocumentRevision Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_FramePresented System.Boolean ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_InputRevision System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName get_PresentationContext Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.Guid:attachmentId::;None:Inceptus.DiagramEngine.Contracts.Primitives.DocumentId:documentId::;None:Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision:documentRevision::;None:Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext:presentationContext::;None:System.Int64:baseSceneGeneration::;None:System.Int64:inputRevision::;None:System.Boolean:framePresented::;None:System.Boolean:decorationsIncluded::)  ",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Static, HideBySig, SpecialName op_Equality System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:left::;None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Static, HideBySig, SpecialName op_Inequality System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:left::;None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Virtual, HideBySig Equals System.Boolean (None:System.Object:obj::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Virtual, HideBySig GetHashCode System.Int32 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Virtual, HideBySig ToString System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Final, Virtual, HideBySig, VtableLayoutMask Equals System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult:other::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig <Clone>$ Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName get_Accepted System.Boolean ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName get_Diagnostics System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName get_InputRevision System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName get_Presentation Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName get_Status Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus:status::;None:System.Boolean:accepted::;None:System.Int64:inputRevision::;None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation:presentation::;None:System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic>:diagnostics::[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })])  ",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Static, HideBySig, SpecialName op_Equality System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult:left::;None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Static, HideBySig, SpecialName op_Inequality System.Boolean (None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult:left::;None:Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Virtual, HideBySig Equals System.Boolean (None:System.Object:obj::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Virtual, HideBySig GetHashCode System.Int32 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Virtual, HideBySig ToString System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSession Public, HideBySig ReplaceDecorationsAsync System.Threading.Tasks.ValueTask`1<Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] (None:System.Int64:inputRevision::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput:input::;Optional, HasDefault:System.Threading.CancellationToken:cancellationToken:null:[System.Runtime.InteropServices.OptionalAttribute()];Optional, HasDefault:System.Threading.CancellationToken:retirementToken:null:[System.Runtime.InteropServices.OptionalAttribute()])  [System.Runtime.CompilerServices.AsyncStateMachineAttribute(typeof(Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSession+<ReplaceDecorationsAsync>d__N))];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSession Public, HideBySig, SpecialName get_DecorationAttachmentId System.Guid ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState Public, HideBySig, SpecialName get_DecorationDiagnostics System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState Public, HideBySig, SpecialName get_DecorationInputRevision System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState Public, HideBySig, SpecialName get_DecorationPresentation Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, HideBySig, SpecialName get_Diagnostics System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, HideBySig, SpecialName get_Items System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, HideBySig, SpecialName, RTSpecialName .ctor  (Optional, HasDefault:System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem>:items:null:[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 2, 1 })];[System.Runtime.InteropServices.OptionalAttribute()];Optional, HasDefault:System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic>:diagnostics:null:[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 2, 1 })];[System.Runtime.InteropServices.OptionalAttribute()])  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, Static, HideBySig, SpecialName get_Empty Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig FindParts System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] (None:Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId:ownerId::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind:ownerKind::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind:partKind::)  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_AttachmentId System.Guid ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_BaseItems System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_BaseSceneGeneration System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_DefaultFontFamily System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_DocumentId Inceptus.DiagramEngine.Contracts.Primitives.DocumentId ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_InputRevision System.Int64 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_Parts System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart>[System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })] ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_PresentationContext Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName get_SourceRevision Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:System.Guid:attachmentId::;None:Inceptus.DiagramEngine.Contracts.Primitives.DocumentId:documentId::;None:Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision:sourceRevision::;None:Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext:presentationContext::;None:System.Int64:baseSceneGeneration::;None:System.Int64:inputRevision::;None:System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem>:baseItems::;None:System.Collections.Generic.IEnumerable`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart>:parts::;None:System.String:defaultFontFamily::[System.Runtime.CompilerServices.NullableAttribute((Byte)2)])  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName get_Provider Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName get_ProviderId Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorId ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName get_Values Inceptus.DiagramEngine.Contracts.Properties.PropertyMap ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorId:providerId::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider:provider::;Optional, HasDefault:System.Collections.Generic.IEnumerable`1<System.Collections.Generic.KeyValuePair`2<System.String,Inceptus.DiagramEngine.Contracts.Properties.PropertyValue>>:values:null:[System.Runtime.CompilerServices.NullableAttribute(new Byte[4] { 2, 0, 1, 1 })];[System.Runtime.InteropServices.OptionalAttribute()])  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Final, Virtual, HideBySig, VtableLayoutMask Equals System.Boolean (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart:other::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig <Clone>$ Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig CreateOrigin Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneOriginTrace (None:System.String:stableSourceKey::)  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig, SpecialName get_Item Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig, SpecialName get_Kind Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig, SpecialName get_OwnerId Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig, SpecialName get_OwnerKind Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, HideBySig, SpecialName, RTSpecialName .ctor  (None:Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId:ownerId::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind:ownerKind::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind:kind::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem:item::)  ",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Static, HideBySig, SpecialName op_Equality System.Boolean (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart:left::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Static, HideBySig, SpecialName op_Inequality System.Boolean (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart:left::;None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart:right::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Virtual, HideBySig Equals System.Boolean (None:System.Object:obj::)  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Virtual, HideBySig GetHashCode System.Int32 ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Virtual, HideBySig ToString System.String ()  [System.Runtime.CompilerServices.CompilerGeneratedAttribute()]",
        "M Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider Public, Virtual, HideBySig, VtableLayoutMask, Abstract Compose Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent (None:Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext:context::;None:Inceptus.DiagramEngine.Contracts.Properties.PropertyMap:input::)  ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Inceptus.DiagramEngine.Contracts.Primitives.DocumentId DocumentId ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment System.Guid InstanceId ",
        "P Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment System.Int64 AttachmentVersion ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext PresentationContext ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Inceptus.DiagramEngine.Contracts.Primitives.DocumentId DocumentId ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision DocumentRevision ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation System.Boolean DecorationsIncluded ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation System.Boolean FramePresented ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation System.Guid AttachmentId ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation System.Int64 BaseSceneGeneration ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation System.Int64 InputRevision ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Presentation ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Status ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult System.Boolean Accepted ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic> Diagnostics [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult System.Int64 InputRevision ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSession System.Guid DecorationAttachmentId ",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation DecorationPresentation [System.Runtime.CompilerServices.NullableAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic> DecorationDiagnostics [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Canvas2D.EditingSession.EditingSessionState System.Int64 DecorationInputRevision ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Empty ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem> Items [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Diagnostics.Diagnostic> Diagnostics [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Inceptus.DiagramEngine.Contracts.Presentation.PresentationViewContext PresentationContext ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Inceptus.DiagramEngine.Contracts.Primitives.DocumentId DocumentId ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Inceptus.DiagramEngine.Contracts.Primitives.DocumentRevision SourceRevision ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart> Parts [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.Collections.Immutable.ImmutableArray`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem> BaseItems [System.Runtime.CompilerServices.NullableAttribute(new Byte[2] { 0, 1 })]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.Guid AttachmentId ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.Int64 BaseSceneGeneration ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.Int64 InputRevision ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext System.String DefaultFontFamily [System.Runtime.CompilerServices.NullableAttribute((Byte)2)]",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneContributorId ProviderId ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider Provider ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Inceptus.DiagramEngine.Contracts.Properties.PropertyMap Values ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind OwnerKind ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Kind ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DSceneItem Item ",
        "P Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Inceptus.DiagramEngine.Contracts.Primitives.SemanticElementId OwnerId ",
        "T Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment Public, Sealed, BeforeFieldInit : System.Object System.IEquatable`1<Inceptus.DiagramEngine.Bpmn.Blazor.BpmnDecorationAttachment> [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation Public, Sealed, BeforeFieldInit : System.Object System.IEquatable`1<Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationPresentation> [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult Public, Sealed, BeforeFieldInit : System.Object System.IEquatable`1<Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateResult> [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)2)]",
        "T Inceptus.DiagramEngine.Canvas2D.EditingSession.Canvas2DDecorationUpdateStatus Public, Sealed : System.Enum System.IComparable;System.IConvertible;System.IFormattable;System.ISpanFormattable ",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContent Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationContext Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationInput Public, Sealed, BeforeFieldInit : System.Object  [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationOwnerKind Public, Sealed : System.Enum System.IComparable;System.IConvertible;System.IFormattable;System.ISpanFormattable ",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart Public, Sealed, BeforeFieldInit : System.Object System.IEquatable`1<Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPart> [System.Runtime.CompilerServices.NullableAttribute((Byte)0)];[System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.Canvas2DDecorationPartKind Public, Sealed : System.Enum System.IComparable;System.IConvertible;System.IFormattable;System.ISpanFormattable ",
        "T Inceptus.DiagramEngine.Contracts.Canvas2D.ICanvas2DDecorationProvider Public, ClassSemanticsMask, Abstract, BeforeFieldInit :   [System.Runtime.CompilerServices.NullableContextAttribute((Byte)1)]",
    ];
}
