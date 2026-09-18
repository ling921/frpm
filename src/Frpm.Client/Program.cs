using Frpm.Client.Generated;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddAuthorizationCore();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddAuthenticationStateDeserialization();

builder.Services.AddScoped(_ => new HttpClient()
{
    BaseAddress = new Uri(builder.HostEnvironment.BaseAddress),
    Timeout = TimeSpan.FromMinutes(35)
});

builder.Services.AddMudServices();
builder.Services.AddRemoteServiceClients(options => options.EnableBrowserRequestStreaming = true);

await builder.Build().RunAsync();
