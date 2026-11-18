using System.Globalization;
using Inceptus.DiagramEngine.Blazor.Demo;
using Inceptus.DiagramEngine.Blazor;
using Inceptus.DiagramEngine.Bpmn.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.Services.AddInceptusBpmnModeler();
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddSingleton<IBpmnModelerStartupDocumentProvider>(BpmnPoolDemoStartupDocumentProvider.Instance);

var host = builder.Build();
var uiCulture = DemoUiCulture.Resolve(host.Services.GetRequiredService<NavigationManager>().Uri);
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.DefaultThreadCurrentUICulture = uiCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentUICulture = uiCulture;
await host.RunAsync();
