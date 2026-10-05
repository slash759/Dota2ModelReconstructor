using System.Diagnostics;

namespace Dota2ModelReconstructor.Vrf;

internal static class LegacyPhysToolRunner
{
    private static readonly string[] ToolBaseNames = ["physvmdl", "clothEffect"];

    public static void Run(string physPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(physPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var toolsDirectory = FindToolsDirectory();
        Directory.CreateDirectory(outputDirectory);

        // Work on a temporary copy: the checked-in legacy tools are never modified and
        // concurrent decompilations cannot overwrite each other's phys.txt/results.
        var workDirectory = Path.Combine(Path.GetTempPath(), "Dota2ModelReconstructor", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDirectory);

        try
        {
            foreach (var file in Directory.EnumerateFiles(toolsDirectory))
                File.Copy(file, Path.Combine(workDirectory, Path.GetFileName(file)), overwrite: true);

            File.Copy(physPath, Path.Combine(workDirectory, "phys.txt"), overwrite: true);

            var initialFiles = Directory.EnumerateFiles(workDirectory)
                .Select(Path.GetFileName)
                .Where(x => x is not null)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var tool in ToolBaseNames)
                RunTool(workDirectory, tool);

            // Keep every TXT produced by the helpers except their temporary input.
            foreach (var result in Directory.EnumerateFiles(workDirectory, "*.txt"))
            {
                var name = Path.GetFileName(result);
                if (name.Equals("phys.txt", StringComparison.OrdinalIgnoreCase))
                    continue;

                // A pre-existing TXT from the tool bundle is not a generated result.
                if (initialFiles.Contains(name))
                    continue;

                File.Copy(result, Path.Combine(outputDirectory, name), overwrite: true);
            }
        }
        finally
        {
            try { Directory.Delete(workDirectory, recursive: true); }
            catch { /* Best-effort cleanup; never hide the actual tool result/error. */ }
        }
    }

    private static void RunTool(string workDirectory, string baseName)
    {
        var exe = Path.Combine(workDirectory, baseName + ".exe");
        if (!File.Exists(exe))
            throw new FileNotFoundException($"Legacy PHYS helper not found: {exe}");

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = workDirectory,
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
