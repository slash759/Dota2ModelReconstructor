using System.Diagnostics;

namespace Dota2ModelReconstructor.Vrf;

public static class LegacyMorphToolRunner
{
    private static readonly string[] ToolBaseNames = ["flex", "flexRules"];
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public sealed record MorphOutputs(string ControlsPath, string FlexRulesPath);

    public static MorphOutputs Run(string sourceCompiledPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceCompiledPath);
        if (!File.Exists(sourceCompiledPath))
            throw new FileNotFoundException("compiled.txt not found.", sourceCompiledPath);

        var toolsDirectory = FindToolsDirectory();
        var compiledPath = Path.Combine(toolsDirectory, "compiled.txt");
        var controlsPath = Path.Combine(toolsDirectory, "controls.txt");
        var flexRulesPath = Path.Combine(toolsDirectory, "FlexRules.txt");

        CleanupGeneratedFiles();
        File.Copy(sourceCompiledPath, compiledPath, overwrite: true);

        try
        {
            foreach (var tool in ToolBaseNames)
                RunTool(toolsDirectory, tool);

            if (!File.Exists(controlsPath))
                throw new InvalidDataException("flex.exe did not generate controls.txt.");
            if (!File.Exists(flexRulesPath))
                throw new InvalidDataException("flexRules.exe did not generate FlexRules.txt.");

            return new MorphOutputs(controlsPath, flexRulesPath);
        }
        catch
        {
            CleanupGeneratedFiles();
            throw;
        }
    }

    public static void CleanupGeneratedFiles()
    {
        try
        {
            var toolsDirectory = FindToolsDirectory();
            foreach (var name in new[] { "compiled.txt", "controls.txt", "FlexRules.txt" })
            {
                try
                {
                    var path = Path.Combine(toolsDirectory, name);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch { /* Best-effort cleanup. */ }
            }
        }
        catch { /* Best-effort cleanup. */ }
    }

    private static void RunTool(string toolsDirectory, string baseName)
    {
        var exe = Path.Combine(toolsDirectory, baseName + ".exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Legacy morph helper not found: {exe}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = toolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        try
        {
            process.StandardInput.WriteLine();
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }
        catch { }

        if (!process.WaitForExit((int)ToolTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);
            throw new TimeoutException($"{baseName}.exe did not exit within {ToolTimeout.TotalSeconds:0} seconds.");
        }

        Task.WaitAll(stdoutTask, stderrTask);

        // As with the PHYS helpers, tolerate a post-output interactive-console failure.
        var expectedOutput = baseName.Equals("flex", StringComparison.OrdinalIgnoreCase)
            ? Path.Combine(toolsDirectory, "controls.txt")
            : Path.Combine(toolsDirectory, "FlexRules.txt");

        if (process.ExitCode != 0 && !File.Exists(expectedOutput))
            throw new InvalidOperationException(
                $"{baseName}.exe failed with exit code {process.ExitCode}.\n{stderrTask.Result}\n{stdoutTask.Result}".Trim());
    }

    private static string FindToolsDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "legacy", "morph"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "legacy", "morph")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "legacy", "morph")),
        };

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException("Could not locate tools/legacy/morph.");
    }
}
