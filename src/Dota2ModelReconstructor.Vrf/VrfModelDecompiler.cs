using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Dota2ModelReconstructor.Vrf;

public sealed class VrfModelDecompiler
{
    public DecompileResult Decompile(string inputPath, string outputDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        inputPath = Path.GetFullPath(inputPath);
        if (!File.Exists(inputPath))
            throw new FileNotFoundException("The compiled VMDL was not found.", inputPath);
        if (!inputPath.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Input must be a .vmdl_c file.", nameof(inputPath));

        using var stream = File.OpenRead(inputPath);
        using var resource = new Resource { FileName = inputPath };
        resource.Read(stream);
        using var fileLoader = new GameFileLoader(null, inputPath);
        return DecompileResource(resource, fileLoader, Path.GetFileName(inputPath), outputDirectory);
    }

    public DecompileResult DecompileResource(Resource resource, IFileLoader fileLoader, string sourceName, string outputDirectory)
    {
        if (resource.ResourceType != ResourceType.Model)
            throw new InvalidDataException($"Expected a model resource, got {resource.ResourceType}.");

        outputDirectory = Path.GetFullPath(outputDirectory);
        Directory.CreateDirectory(outputDirectory);

        var baseName = Path.GetFileNameWithoutExtension(sourceName);
        if (baseName.EndsWith(".vmdl", StringComparison.OrdinalIgnoreCase))
            baseName = baseName[..^5];

        var vmdlPath = Path.Combine(outputDirectory, baseName + ".vmdl");
        var gltfPath = Path.Combine(outputDirectory, baseName + ".gltf");

        WriteVmdl(resource, fileLoader, vmdlPath);

        var exporter = new GltfModelExporter(fileLoader)
        {
            ProgressReporter = new Progress<string>(_ => { }),
        };
        exporter.Export(resource, gltfPath);

        return new DecompileResult(vmdlPath, gltfPath);
    }

    private static void WriteVmdl(Resource resource, IFileLoader fileLoader, string targetPath)
    {
        using var content = FileExtract.Extract(resource, fileLoader);
        if (content.Data is null)
            throw new InvalidDataException("VRF did not produce VMDL text for this model.");

        File.WriteAllBytes(targetPath, content.Data);
        WriteSubFiles(content, Path.GetDirectoryName(targetPath)!);
    }

    private static void WriteSubFiles(ContentFile content, string directory)
    {
        foreach (var subFile in content.SubFiles)
        {
            if (subFile.Extract is null) continue;
            var path = Path.Combine(directory, subFile.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, subFile.Extract());
        }

        foreach (var additional in content.AdditionalFiles)
        {
            if (additional.Data is null || string.IsNullOrWhiteSpace(additional.FileName)) continue;
            var path = Path.Combine(directory, additional.FileName);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, additional.Data);
            WriteSubFiles(additional, Path.GetDirectoryName(path)!);
        }
    }
}

public sealed record DecompileResult(string VmdlPath, string GltfPath);
