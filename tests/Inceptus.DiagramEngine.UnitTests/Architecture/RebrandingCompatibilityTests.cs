using System.Globalization;
using System.Reflection;
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
    // Only the six approved namespace prefixes are normalized; domain/member names stay intact.
    [Fact]
    public void PublicAndProtectedApiRetainsThePreMigrationContract()
    {
        var lines = Assemblies.SelectMany(DescribeAssembly).Order(StringComparer.Ordinal).ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", lines))));
        Assert.Equal("1E44A0B57BC40D0B094F23A9658283916B49AB3B635CF77FC174EFA275DE752A", hash);
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
                    $"{string.Join(';', (method is MethodInfo generic ? generic.GetGenericArguments() : []).Select(GenericParameter))} {Attributes(method.CustomAttributes)}";
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
    private static string Attributes(IEnumerable<CustomAttributeData> attributes) => string.Join(';', attributes.Select(static attribute => Normalize(attribute.ToString())).Order(StringComparer.Ordinal));
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
