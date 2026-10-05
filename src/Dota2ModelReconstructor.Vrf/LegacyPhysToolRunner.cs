using System.Diagnostics;
using System.Security.Cryptography;

namespace Dota2ModelReconstructor.Vrf;

public static class LegacyPhysToolRunner
{
    private static readonly string[] ToolBaseNames = ["physvmdl", "clothEffect"];
    private static readonly TimeSpan ToolTimeout = TimeSpan.FromSeconds(30);

    public static IReadOnlyList<string> Run(string sourcePhysPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePhysPath);
        if (!File.Exists(sourcePhysPath))
            throw new FileNotFoundException("phys.txt not found.", sourcePhysPath);

        var toolsDirectory = FindToolsDirectory();
        var toolPhysPath = Path.Combine(toolsDirectory, "phys.txt");
        var generatedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            File.Copy(sourcePhysPath, toolPhysPath, overwrite: true);

            foreach (var tool in ToolBaseNames)
            {
                var before = SnapshotTxtFiles(toolsDirectory);
                var result = RunTool(toolsDirectory, tool);
                var after = SnapshotTxtFiles(toolsDirectory);
                var changedByTool = new List<string>();

                foreach (var (path, fingerprint) in after)
                {
                    if (Path.GetFileName(path).Equals("phys.txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!before.TryGetValue(path, out var previous) || previous != fingerprint)
                    {
                        changedByTool.Add(path);
                        generatedFiles.Add(path);
                    }
                }

                // These legacy console apps generate their TXT successfully and only then call
                // Console.ReadKey(). With redirected input ReadKey throws InvalidOperationException,
                // producing a non-zero exit code even though the requested output is already valid.
                // Treat that post-generation crash as success, but never hide a real failure that
                // produced no output.
                if (result.ExitCode != 0 && changedByTool.Count == 0)
                    throw new InvalidOperationException(
                        $"{tool}.exe failed with exit code {result.ExitCode}.\n{result.StandardError}\n{result.StandardOutput}".Trim());
            }

            return generatedFiles.ToArray();
        }
        catch
        {
            CleanupTemporaryFiles(toolsDirectory);
            throw;
        }
    }

    public static void CleanupGeneratedFiles(IEnumerable<string> generatedFiles)
    {
        foreach (var path in generatedFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* Best-effort cleanup. */ }
        }

        try
        {
            var toolsDirectory = FindToolsDirectory();
            var physPath = Path.Combine(toolsDirectory, "phys.txt");
            if (File.Exists(physPath)) File.Delete(physPath);
        }
        catch { /* Best-effort cleanup. */ }
    }

    private static void CleanupTemporaryFiles(string toolsDirectory)
    {
        foreach (var temporaryName in new[] { "phys.txt", "cloth_shapes.vmdl.txt", "cloth_effects_vmdl.txt" })
        {
            try
            {
                var temporaryPath = Path.Combine(toolsDirectory, temporaryName);
                if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            }
            catch { /* Best-effort cleanup. */ }
        }
    }

    private static ToolResult RunTool(string toolsDirectory, string baseName)
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
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            },
        };

        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        // The legacy helpers pause for a key/Enter after generating their output.
        // Feed input up front so a Console.ReadLine/ReadKey-style pause cannot block
        // the pipeline and prevent the next helper (notably clothEffect) from running.
        try
        {
            process.StandardInput.WriteLine();
            process.StandardInput.Flush();
            process.StandardInput.Close();
        }
        catch
        {
            // Some helpers may close stdin themselves; the timeout below is the fallback.
        }

        if (!process.WaitForExit((int)ToolTimeout.TotalMilliseconds))
        {
            try { process.Kill(entireProcessTree: true); }
            catch { /* Best-effort termination. */ }

            process.WaitForExit();
            Task.WaitAll(stdoutTask, stderrTask);

            throw new TimeoutException(
                $"{baseName}.exe did not exit within {ToolTimeout.TotalSeconds:0} seconds. " +
                "It was terminated so the PHYS pipeline cannot remain blocked.");
        }

        Task.WaitAll(stdoutTask, stderrTask);

        return new ToolResult(process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private sealed record ToolResult(int ExitCode, string StandardOutput, string StandardError);

    private static Dictionary<string, string> SnapshotTxtFiles(string directory)
    {
        var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in Directory.EnumerateFiles(directory, "*.txt"))
        {
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var hash = Convert.ToHexString(SHA256.HashData(stream));
            snapshot[path] = hash;
        }

        return snapshot;
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
