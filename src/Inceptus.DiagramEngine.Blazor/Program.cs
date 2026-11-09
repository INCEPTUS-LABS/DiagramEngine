using System.Globalization;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var uiCulture = CultureInfo.GetCultureInfo("en");
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = uiCulture;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddInceptusBpmnModeler();
builder.Services.AddSingleton<IBpmnModelerStartupDocumentProvider>(BpmnPoolDemoStartupDocumentProvider.Instance);

await builder.Build().RunAsync();
