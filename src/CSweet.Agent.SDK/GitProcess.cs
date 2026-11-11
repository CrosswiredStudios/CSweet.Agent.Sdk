using System.Diagnostics;
using System.Text;

namespace CSweet.Agent.SDK;

internal sealed record GitProcessResult(int ExitCode, string Output, string Error);

/// <summary>Runs the git program with an argument list (never a shell) and bounded captured output.</summary>
internal static class GitProcess
{
    private const int MaximumCapturedCharacters = 64 * 1024;

    internal static async Task<GitProcessResult> RunAsync(
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? environment,
        CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["LC_ALL"] = "C";
        if (environment is not null)
            foreach (var item in environment) start.Environment[item.Key] = item.Value;

        using var process = new Process { StartInfo = start };
        try
        {
            process.Start();
        }
        catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new GitWorkspaceCommandException("git is not installed in this runtime.", -1, string.Empty);
        }
        process.StandardInput.Close();
        var output = ReadBoundedAsync(process.StandardOutput, cancellationToken);
        var error = ReadBoundedAsync(process.StandardError, cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); }
            catch (Exception exception) when (exception is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            throw;
        }
        return new GitProcessResult(process.ExitCode, await output, await error);
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var captured = new StringBuilder();
        var buffer = new char[4096];
        int read;
        // Keep draining after the cap so a talkative child never blocks on a full pipe.
        while ((read = await reader.ReadAsync(buffer, cancellationToken)) > 0)
        {
            var room = MaximumCapturedCharacters - captured.Length;
            if (room > 0) captured.Append(buffer, 0, Math.Min(room, read));
        }
        return captured.ToString();
    }
}
