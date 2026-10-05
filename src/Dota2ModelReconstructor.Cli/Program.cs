using Dota2ModelReconstructor.Vrf;

if (args.Length is < 1 or > 2)
{
    Console.WriteLine("Dota2ModelReconstructor - VMDL_C decompiler");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  Dota2ModelReconstructor <model.vmdl_c> [output-directory]");
    return 1;
}

var input = Path.GetFullPath(args[0]);
var output = args.Length == 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(Path.GetDirectoryName(input)!, Path.GetFileNameWithoutExtension(input) + "_decompiled");

try
{
    Console.WriteLine($"Input : {input}");
    Console.WriteLine($"Output: {output}");
    Console.WriteLine("VRF   : 19.2.6339");

    var result = new VrfModelDecompiler().Decompile(input, output);

    Console.WriteLine();
    Console.WriteLine($"VMDL : {result.VmdlPath}");
    Console.WriteLine($"GLTF : {result.GltfPath}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine($"ERROR: {ex.Message}");
    return 2;
}
