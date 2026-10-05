using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Dota2ModelReconstructor.Vrf;

public sealed class VpkModelArchive : IDisposable
{
    private readonly Package package = new();
    public string FileName { get; }
    public IReadOnlyList<string> Models { get; }

    public VpkModelArchive(string fileName)
    {
        FileName = Path.GetFullPath(fileName);
        package.OptimizeEntriesForBinarySearch(StringComparison.OrdinalIgnoreCase);
        package.Read(FileName);

        Models = package.Entries?
            .SelectMany(group => group.Value)
            .Select(entry => entry.GetFullPath())
            .Where(path => path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];
    }

    public DecompileResult Decompile(string modelPath, string addonRoot)
    {
        var entry = package.FindEntry(modelPath)
            ?? throw new FileNotFoundException($"Model '{modelPath}' was not found inside the VPK.");

        package.ReadEntry(entry, out var bytes, validateCrc: false);
        using var stream = new MemoryStream(bytes, writable: false);
        using var resource = new Resource { FileName = modelPath };
        resource.Read(stream);

        using var loader = new GameFileLoader(package, FileName);
        return new AddonModelDecompiler().Decompile(resource, loader, modelPath, addonRoot);
    }

    public void Dispose() => package.Dispose();
}
