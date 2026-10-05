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

        // Preserve S2V 19.2's own textual representation of the PHYS block. The legacy
        // reconstruction helpers consume this dump as phys.txt, so do not reformat it.
        var physBlock = resource.GetBlockByType(BlockType.PHYS);
        if (physBlock is not null)
        {
            var reconstructionDirectory = Path.Combine(
                Path.GetDirectoryName(vmdlPath)!,
                "reconstruction",
                Path.GetFileNameWithoutExtension(vmdlPath));
            Directory.CreateDirectory(reconstructionDirectory);

            var physPath = Path.Combine(reconstructionDirectory, "phys.txt");
            // S2V's text viewer renders each indentation level as four spaces. VRF's
            // IndentedTextWriter uses tabs internally, so normalize only indentation;
            // the PHYS values and KV3 structure remain untouched.
            var physText = physBlock.ToString().Replace("\t", "    ", StringComparison.Ordinal);
            File.WriteAllText(physPath, physText, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            // Reconstruct the source-only cloth nodes from PHYS and append them to the
            // RootNode.children array in the generated VMDL.
            var helperOutputs = LegacyPhysToolRunner.Run(physPath, reconstructionDirectory);
            AppendRootChildren(vmdlPath, helperOutputs);
        }

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
        var gltfPath = Path.Combine(Path.GetDirectoryName(vmdlPath)!, "blenderFiles", Path.GetFileNameWithoutExtension(vmdlPath) + ".gltf");
        Directory.CreateDirectory(Path.GetDirectoryName(gltfPath)!);
        var exporter = new GltfModelExporter(fileLoader)
        {
            ProgressReporter = new Progress<string>(_ => { }),
        };
        exporter.Export(resource, gltfPath);

        return new DecompileResult(vmdlPath, gltfPath);
    }

    private static void AppendRootChildren(string vmdlPath, IEnumerable<string> helperOutputs)
    {
        var snippets = helperOutputs
            .Where(File.Exists)
            .Where(path =>
            {
                var name = Path.GetFileName(path);
                return name.Equals("cloth_shapes.vmdl.txt", StringComparison.OrdinalIgnoreCase)
                    || name.Equals("cloth_effects_vmdl.txt", StringComparison.OrdinalIgnoreCase);
            })
            .Select(File.ReadAllText)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (snippets.Length == 0)
            return;

        var vmdl = File.ReadAllText(vmdlPath);
        var childrenToken = "children =";
        var childrenIndex = vmdl.IndexOf(childrenToken, StringComparison.Ordinal);
        if (childrenIndex < 0)
            throw new InvalidDataException("Could not locate RootNode.children in the generated VMDL.");

        var arrayStart = vmdl.IndexOf('[', childrenIndex + childrenToken.Length);
        if (arrayStart < 0)
            throw new InvalidDataException("Could not locate the RootNode.children array.");

        var arrayEnd = FindMatchingBracket(vmdl, arrayStart);
        var indentStart = vmdl.LastIndexOf('\n', arrayStart);
        var propertyIndent = indentStart < 0 ? string.Empty : vmdl[(indentStart + 1)..arrayStart];
        propertyIndent = new string(propertyIndent.TakeWhile(char.IsWhiteSpace).ToArray());
        var childIndent = propertyIndent + "    ";

        var insertion = string.Concat(snippets.Select(snippet =>
            Environment.NewLine + IndentSnippet(snippet.Trim(), childIndent) + ","));

        vmdl = vmdl.Insert(arrayEnd, insertion + Environment.NewLine + propertyIndent);
        File.WriteAllText(vmdlPath, vmdl, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static int FindMatchingBracket(string text, int openIndex)
    {
        var depth = 0;
        var inString = false;
        var escaped = false;

        for (var i = openIndex; i < text.Length; i++)
        {
            var ch = text[i];
            if (inString)
            {
                if (escaped) escaped = false;
                else if (ch == '\\') escaped = true;
                else if (ch == '"') inString = false;
                continue;
            }

            if (ch == '"') inString = true;
            else if (ch == '[') depth++;
            else if (ch == ']' && --depth == 0) return i;
        }

        throw new InvalidDataException("RootNode.children has no matching closing bracket.");
    }

    private static string IndentSnippet(string text, string indent)
        => string.Join(Environment.NewLine,
            text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
                .Split('\n')
                .Select(line => indent + line));

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
