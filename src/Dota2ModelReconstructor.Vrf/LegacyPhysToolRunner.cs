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
                RunTool(toolsDirectory, tool);
                var after = SnapshotTxtFiles(toolsDirectory);

                foreach (var (path, fingerprint) in after)
                {
                    if (Path.GetFileName(path).Equals("phys.txt", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (!before.TryGetValue(path, out var previous) || previous != fingerprint)
                        generatedFiles.Add(path);
                }
            }

            return generatedFiles.ToArray();
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

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{baseName}.exe failed with exit code {process.ExitCode}.\n{stderrTask.Result}\n{stdoutTask.Result}".Trim());
    }

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
