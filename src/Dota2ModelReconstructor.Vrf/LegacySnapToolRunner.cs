using System.Diagnostics;

namespace Dota2ModelReconstructor.Vrf;

public static class LegacySnapToolRunner
{
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public static string Run(string sourceSnapPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceSnapPath);
        if (!File.Exists(sourceSnapPath))
            throw new FileNotFoundException("Snap input file not found.", sourceSnapPath);

        var toolsDirectory = FindToolsDirectory();
        var inputPath = Path.Combine(toolsDirectory, "snap.txt");
        var outputPath = Path.Combine(toolsDirectory, "snap_blender.py");

        try
        {
            if (File.Exists(inputPath)) File.Delete(inputPath);
            if (File.Exists(outputPath)) File.Delete(outputPath);
            File.Copy(sourceSnapPath, inputPath, overwrite: true);

            var exe = Path.Combine(toolsDirectory, "snap.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException($"Legacy SNAP helper not found: {exe}");

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
            try { process.StandardInput.WriteLine(); process.StandardInput.Close(); } catch { }

            if (!process.WaitForExit((int)ToolTimeout.TotalMilliseconds))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                throw new TimeoutException("snap.exe timed out.");
            }

            Task.WaitAll(stdoutTask, stderrTask);
            if (!File.Exists(outputPath))
                throw new InvalidOperationException(
                    $"snap.exe did not generate snap_blender.py.\n{stderrTask.Result}\n{stdoutTask.Result}".Trim());

            return outputPath;
        }
        finally
        {
            try { if (File.Exists(inputPath)) File.Delete(inputPath); } catch { }
        }
    }

    private static string FindToolsDirectory()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", "legacy", "snap"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "tools", "legacy", "snap")),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "tools", "legacy", "snap")),
        };

        return candidates.FirstOrDefault(Directory.Exists)
            ?? throw new DirectoryNotFoundException("Could not locate tools/legacy/snap.");
    }
}
