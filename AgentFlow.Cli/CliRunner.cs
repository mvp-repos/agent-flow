using System;
using AgentFlow.Cli.Commands;

namespace AgentFlow.Cli
{
    public sealed class CliRunner
    {
        // Services
        private readonly string[]          _args;
        private readonly RunCommandHandler _run;

        public CliRunner(string[] args, RunCommandHandler run)
        {
            _args = args ?? Array.Empty<string>();
            _run  = run;
        }

        /// <summary>
        /// Asynchronously runs the CLI command based on the provided arguments. It checks for 
        /// the presence of commands and flags, executes the appropriate command handler, and handles 
        /// unknown commands by displaying a help message.
        /// </summary>
        /// <returns>
        /// Returns an integer exit code indicating the result of the command execution: 0 for success, 1 for 
        /// unknown command or error. The method also handles the display of help information when requested or when an error occurs.
        /// </returns>
        public async Task<int> RunAsync()
        {
            if (_args.Length == 0 || _args[0] is "-h" or "--help")
            {
                PrintHelp();
                return 0;
            }

            var cmd = _args[0];

            if (string.Equals(cmd, "init", StringComparison.OrdinalIgnoreCase))
            {
                var baseDir        = AppContext.BaseDirectory;
                var templatePath   = Path.Combine(baseDir, "appsettings.template.json");
                var configPath     = Path.Combine(baseDir, "appsettings.json");
                var cursorRulesDir = Path.Combine(baseDir, "cursor-rules");

                if (!File.Exists(templatePath))
                {
                    Console.WriteLine($"Template file not found: {templatePath}");
                    return 1;
                }

                if (!File.Exists(configPath))
                {
                    File.Copy(templatePath, configPath);
                    Console.WriteLine($"Created: {configPath}");
                }
                else
                {
                    Console.WriteLine($"Already exists: {configPath}");
                }

                if (Directory.Exists(cursorRulesDir))
                {
                    Console.WriteLine($"Cursor rules: {cursorRulesDir} (edit run-prd.ps1, prompt-prefix.md as needed)");
                }
                else
                {
                    Console.WriteLine($"Cursor rules folder not found: {cursorRulesDir} (build copies docs/templates/cursor-rules here)");
                }

                Console.WriteLine();
                Console.WriteLine("Next steps:");
                Console.WriteLine("1. Edit appsettings.json");
                Console.WriteLine("2. Optionally edit cursor-rules\\prompt-prefix.md and cursor-rules\\run-prd.ps1");
                Console.WriteLine("3. Run AgentFlow.exe run --provider ado --workitem <id> [--dry-run]");
                return 0;
            }

            if (!string.Equals(cmd, "run", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine($"Unknown command: {cmd}");
                PrintHelp();
                return 1;
            }

            return await _run.ExecuteAsync(_args.Skip(1).ToArray());
        }

        /// <summary>
        /// Prints the help message to the console, providing usage instructions and examples for the AgentFlow CLI. This method 
        /// is called when the user requests help or when an unknown command is entered, guiding users on how to properly use 
        /// the CLI to run workflows with specified providers and work items.
        /// </summary>
        private static void PrintHelp()
        {
            Console.WriteLine("""
                AgentFlow CLI (v0)

                Commands:
                  init
                  run --provider <fake|ado|azuredevops> --workitem <id> [--dry-run]

                Examples:
                  AgentFlow.exe init
                  AgentFlow.exe run --provider fake --workitem 123 --dry-run
                  AgentFlow.exe run --provider ado --workitem 12345 --dry-run
                  AgentFlow.exe run --provider azuredevops --workitem 12345 --dry-run
                """);
        }
    }
}
