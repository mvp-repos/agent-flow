// See https://aka.ms/new-console-template for more information

using AgentFlow.Cli.DI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace AgentFlow.Cli;

internal sealed class Program
{
    private static async Task<int> Main(string[] args)
    {
        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAgentFlowCli(args)
            .Build();

        var runner = host.Services.GetRequiredService<CliRunner>();
        return await runner.RunAsync();
    }
}
