using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace Dota2ModelReconstructor.Vrf;

public sealed class AddonModelDecompiler
{
    public DecompileResult Decompile(Resource resource, IFileLoader fileLoader, string modelPath, string addonRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(addonRoot);

        addonRoot = Path.GetFullPath(addonRoot);
        Directory.CreateDirectory(addonRoot);

        var normalizedModelPath = NormalizeRelative(modelPath);
        var sourceVmdlPath = normalizedModelPath.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
            ? normalizedModelPath[..^2]
            : normalizedModelPath;

        // Keep the original in-resource name. ModelExtract uses it to generate the exact
        // source_filename paths for referenced meshes and animations.
        resource.FileName = normalizedModelPath;

        var extract = new ModelExtract(resource, fileLoader);
        using var content = extract.ToContentFile();

        var vmdlPath = SafeCombine(addonRoot, Path.Combine("decompiled_by_pinkie", sourceVmdlPath));
        Directory.CreateDirectory(Path.GetDirectoryName(vmdlPath)!);
        if (content.Data is null)
            throw new InvalidDataException("S2V did not produce VMDL data.");
        File.WriteAllBytes(vmdlPath, content.Data);

        // S2V exposes the full DMX destinations through ModelExtract. The ContentFile
        // subfiles are generated in the same order: render meshes, hulls, physics meshes,
        // then animations.
        var destinations = extract.RenderMeshesToExtract.Select(x => x.FileName)
            .Concat(extract.PhysHullsToExtract.Select(x => x.FileName))
            .Concat(extract.PhysMeshesToExtract.Select(x => x.FileName))
            .Concat(extract.AnimationsToExtract.Select(x => x.FileName))
            .ToArray();

        if (destinations.Length != content.SubFiles.Count)
            throw new InvalidDataException("S2V DMX output list did not match its destination list.");

        for (var i = 0; i < content.SubFiles.Count; i++)
        {
            var subFile = content.SubFiles[i];
            if (subFile.Extract is null) continue;

            var destination = SafeCombine(addonRoot, NormalizeRelative(destinations[i]));
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.WriteAllBytes(destination, subFile.Extract());
        }

        // Keep the GLTF next to the decompiled VMDL; DMX files instead live at the
        // source_filename locations expected by ModelDoc.
        var gltfPath = Path.ChangeExtension(vmdlPath, ".gltf");
        var exporter = new GltfModelExporter(fileLoader)
        {
            ProgressReporter = new Progress<string>(_ => { }),
        };
        exporter.Export(resource, gltfPath);

        return new DecompileResult(vmdlPath, gltfPath);
    }

    private static string NormalizeRelative(string path)
        => path.Replace('\\', '/').TrimStart('/').Replace('/', Path.DirectorySeparatorChar);

    private static string SafeCombine(string root, string relative)
    {
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(Path.Combine(fullRoot, relative));
        if (!fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Unsafe output path: {relative}");
        return fullPath;
    }
}
