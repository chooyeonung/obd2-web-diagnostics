using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using ObdWebApp;
using ObdWebApp.Services;
using ObdWebApp.ViewModels;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

// OBD 진단 서비스 계층 (Model/Service)
builder.Services.AddSingleton<ObdService>();
builder.Services.AddSingleton<SimulatorTransport>();
builder.Services.AddSingleton<BleTransport>();

// ViewModel 계층
builder.Services.AddSingleton<DashboardViewModel>();

await builder.Build().RunAsync();
