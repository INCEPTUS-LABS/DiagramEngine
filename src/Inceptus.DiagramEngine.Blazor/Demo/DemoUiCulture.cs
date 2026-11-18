using System.Globalization;
using System.Web;

namespace Inceptus.DiagramEngine.Blazor.Demo;

/// <summary>Startup-only selection of existing modeler resources; numeric formatting remains invariant.</summary>
internal static class DemoUiCulture
{
    public static bool IsSupported(string? name) => name is "en" or "de" or "es" or "fr" or "pl";

    public static CultureInfo Resolve(string uri)
    {
        var values = HttpUtility.ParseQueryString(new Uri(uri, UriKind.Absolute).Query).GetValues("uiCulture");
        var name = values is { Length: 1 } && IsSupported(values[0]) ? values[0] : "en";
        return CultureInfo.GetCultureInfo(name);
    }
}
