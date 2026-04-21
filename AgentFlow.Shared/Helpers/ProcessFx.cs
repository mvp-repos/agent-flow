using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace AgentFlow.Shared.Helpers;

/// <summary>
/// Shared helpers for running and controlling child processes (e.g. <c>dotnet</c>) and formatting execution errors.
/// </summary>
public static class ProcessFx
{
    /// <summary>
    /// Best-effort termination when the run is cancelled so the child process does not keep running in the background.
    /// </summary>
    /// <param name="process">The child process to terminate.</param>
    public static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
                process.Kill(entireProcessTree: true);
        }
        catch
        {
            // Ignore: process may already have exited or platform may restrict kill
        }
    }

    /// <summary>
    /// Formats a caught exception into a multi-line log suitable for notifications and work item comments.
    /// </summary>
    /// <param name="operationLabel">Short label for the operation (e.g. <c>dotnet build</c> or <c>dotnet test</c>).</param>
    /// <param name="ex">Exception thrown while starting or running the process.</param>
    /// <returns>
    /// Multi-line text suitable for notifications and work item comments.
    /// </returns>
    public static string FormatExecutionError(string operationLabel, Exception ex)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[AgentFlow] {operationLabel} could not complete.");
        sb.AppendLine($"{ex.GetType().Name}: {ex.Message}");
        if (ex is Win32Exception wx)
            sb.AppendLine($"Native error code: {wx.NativeErrorCode}");
        if (ex.InnerException != null)
            sb.AppendLine($"Inner ({ex.InnerException.GetType().Name}): {ex.InnerException.Message}");
        if (ex is FileNotFoundException or Win32Exception { NativeErrorCode: 2 })
            sb.AppendLine("Hint: ensure the .NET SDK is installed and `dotnet` is on PATH.");
        return sb.ToString().TrimEnd();
    }
}

