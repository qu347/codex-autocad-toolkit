using AiCad.Bridge;
using AiCad.Contracts;
using AiCad.Mechanical;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var options = BridgeOptions.CreateDefault();
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(options);
builder.Services.AddSingleton(provider => new SessionDiscovery(options.SessionDirectory));
builder.Services.AddSingleton<ICadClient>(provider =>
{
    var discovery = provider.GetRequiredService<SessionDiscovery>();
    return new PipeCadClient(discovery.FindActive, options.RpcTimeout, options.MaxMessageBytes);
});
builder.Services.AddSingleton<RoundedPlatePlanFactory>();
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

await builder.Build().RunAsync();
