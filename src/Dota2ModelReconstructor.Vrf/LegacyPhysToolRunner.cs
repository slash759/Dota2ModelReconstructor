using System.Diagnostics;

namespace Dota2ModelReconstructor.Vrf;

public static class LegacyPhysToolRunner
{
    private static readonly string[] ToolBaseNames = ["physvmdl", "clothEffect"];

    public static IReadOnlyList<string> Run(string sourcePhysPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePhysPath);
        if (!File.Exists(sourcePhysPath))
            throw new FileNotFoundException("phys.txt not found.", sourcePhysPath);

        var toolsDirectory = FindToolsDirectory();
        var toolPhysPath = Path.Combine(toolsDirectory, "phys.txt");

        // The helpers always live and execute in this directory. Only phys.txt and their
        // generated TXT outputs are temporary.
        var txtBefore = Directory.EnumerateFiles(toolsDirectory, "*.txt")
            .Select(Path.GetFileName)
            .Where(x => x is not null)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        try
        {
            File.Copy(sourcePhysPath, toolPhysPath, overwrite: true);

            foreach (var tool in ToolBaseNames)
                RunTool(toolsDirectory, tool);

            return Directory.EnumerateFiles(toolsDirectory, "*.txt")
                .Where(x => !Path.GetFileName(x).Equals("phys.txt", StringComparison.OrdinalIgnoreCase))
                .Where(x => !txtBefore.Contains(Path.GetFileName(x)))
                .ToArray();
        }
        finally
        {
            try { if (File.Exists(toolPhysPath)) File.Delete(toolPhysPath); }
            catch { /* Best-effort cleanup. */ }
        }
    }

    private static void RunTool(string toolsDirectory, string baseName)
    {
        var exe = Path.Combine(toolsDirectory, baseName + ".exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Legacy PHYS helper not found: {exe}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = toolsDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{baseName}.exe failed with exit code {process.ExitCode}.\n{stderrTask.Result}\n{stdoutTask.Result}".Trim());
    }

    private static string FindToolsDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "legacy", "phys"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "legacy", "phys")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "legacy", "phys")),
        };

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException("Could not locate tools/legacy/phys.");
    }
}
