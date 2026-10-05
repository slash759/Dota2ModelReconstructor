using Dota2ModelReconstructor.Vrf;
using System.Diagnostics;

namespace Dota2ModelReconstructor.Ui;

public sealed class MainForm : Form
{
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox search = new() { Dock = DockStyle.Top, PlaceholderText = "Buscar modelo..." };
    private readonly Label sourceLabel = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 38, Text = "Arrastra un .vmdl_c o pak01_dir.vpk aquí" };
    private readonly Label selectedLabel = new() { Dock = DockStyle.Top, AutoEllipsis = true, Height = 42, Text = "Modelo: ninguno" };
    private readonly TextBox outputText = new() { Dock = DockStyle.Top, ReadOnly = true };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button openModel = new() { Text = "Abrir VMDL_C" };
    private readonly Button openVpk = new() { Text = "Abrir VPK" };
    private readonly Button chooseOutput = new() { Text = "Seleccionar addon..." };
    private readonly Button decompile = new() { Text = "DESCOMPILAR", Height = 48, Dock = DockStyle.Top, Enabled = false };
    private readonly Button openOutput = new() { Text = "Abrir salida", Enabled = false };
    private readonly Button generateCapsules = new() { Text = "Probar PHYS / Generar cápsulas" };

    private VpkModelArchive? archive;
    private string? directModel;
    private string? selectedVpkModel;

    public MainForm()
    {
        Text = "Dota 2 Model Reconstructor - S2V 19.2";
        Width = 1050;
        Height = 680;
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
        toolbar.Controls.AddRange([openModel, openVpk, chooseOutput, openOutput, generateCapsules]);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(tree);
        left.Controls.Add(search);
        left.Controls.Add(new Label { Text = "Modelos del VPK", Dock = DockStyle.Top, Height = 24 });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        right.Controls.Add(log);
        right.Controls.Add(decompile);
        right.Controls.Add(new Label { Text = "Addon de destino (content\\dota_addons\\...)", Dock = DockStyle.Top, Height = 22 });
        right.Controls.Add(outputText);
        right.Controls.Add(selectedLabel);
        right.Controls.Add(sourceLabel);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        Controls.Add(split);
        Controls.Add(toolbar);

        openModel.Click += (_, _) => BrowseModel();
        openVpk.Click += (_, _) => BrowseVpk();
        chooseOutput.Click += (_, _) => BrowseOutput();
        openOutput.Click += (_, _) => OpenOutputFolder();
        generateCapsules.Click += async (_, _) => await GenerateCapsulesAsync();
        decompile.Click += async (_, _) => await DecompileAsync();
        tree.AfterSelect += (_, e) => SelectTreeModel(e.Node);
        search.TextChanged += (_, _) => RebuildTree(search.Text);
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        AppendLog("Motor: Source 2 Viewer / ValveResourceFormat 19.2 vendorizado");
        AppendLog("Powered by Source 2 Viewer (ValveResourceFormat) - https://s2v.app");
        AppendLog("Puedes abrir/arrastrar un .vmdl_c o navegar pak01_dir.vpk.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) archive?.Dispose();
        base.Dispose(disposing);
    }

    private void BrowseModel()
    {
        using var d = new OpenFileDialog { Filter = "Compiled model (*.vmdl_c)|*.vmdl_c", Title = "Abrir VMDL_C" };
        if (d.ShowDialog(this) == DialogResult.OK) LoadDirectModel(d.FileName);
    }

    private void BrowseVpk()
    {
        using var d = new OpenFileDialog { Filter = "Valve package (*.vpk)|*.vpk", Title = "Abrir VPK de Dota 2" };
        if (d.ShowDialog(this) == DialogResult.OK) LoadVpk(d.FileName);
    }

    private void BrowseOutput()
    {
        using var d = new FolderBrowserDialog { Description = "Selecciona la carpeta raíz del addon de Dota 2" };
        if (d.ShowDialog(this) == DialogResult.OK) outputText.Text = d.SelectedPath;
    }

    private void LoadDirectModel(string path)
    {
        archive?.Dispose();
        archive = null;
        selectedVpkModel = null;
        directModel = Path.GetFullPath(path);
        tree.Nodes.Clear();
        sourceLabel.Text = $"Archivo: {directModel}";
        selectedLabel.Text = $"Modelo: {Path.GetFileName(directModel)}";
        decompile.Enabled = true;
        AppendLog($"Abierto: {directModel}");
    }

    private void LoadVpk(string path)
    {
        try
        {
            Cursor = Cursors.WaitCursor;
            archive?.Dispose();
            archive = new VpkModelArchive(path);
            directModel = null;
            selectedVpkModel = null;
            sourceLabel.Text = $"VPK: {archive.FileName}";
            selectedLabel.Text = "Modelo: selecciona un .vmdl_c";
            RebuildTree(search.Text);
            decompile.Enabled = false;
            AppendLog($"VPK abierto: {archive.FileName}");
            AppendLog($"Modelos VMDL_C encontrados: {archive.Models.Count:N0}");
        }
        catch (Exception ex)
        {
            archive?.Dispose();
            archive = null;
            MessageBox.Show(this, ex.Message, "No se pudo abrir el VPK", MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppendLog($"ERROR VPK: {ex}");
        }
        finally { Cursor = Cursors.Default; }
    }

    private void RebuildTree(string filter)
    {
        tree.BeginUpdate();
        tree.Nodes.Clear();
        if (archive is null) { tree.EndUpdate(); return; }

        var paths = string.IsNullOrWhiteSpace(filter)
            ? archive.Models
            : archive.Models.Where(x => x.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        foreach (var path in paths)
            AddPath(path);

        tree.EndUpdate();
    }

    private void AddPath(string path)
    {
        var parts = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        TreeNodeCollection nodes = tree.Nodes;
        TreeNode? node = null;

        for (var i = 0; i < parts.Length; i++)
        {
            node = nodes.Cast<TreeNode>().FirstOrDefault(n => n.Text.Equals(parts[i], StringComparison.OrdinalIgnoreCase));
            if (node is null)
            {
                node = new TreeNode(parts[i]);
                nodes.Add(node);
            }
            nodes = node.Nodes;
        }

        if (node is not null) node.Tag = path;
    }

    private void SelectTreeModel(TreeNode node)
    {
        if (node.Tag is not string path || !path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)) return;
        selectedVpkModel = path;
        selectedLabel.Text = $"Modelo: {path}";
        decompile.Enabled = archive is not null;
    }

    private async Task DecompileAsync()
    {
        if (string.IsNullOrWhiteSpace(outputText.Text)) BrowseOutput();
        if (string.IsNullOrWhiteSpace(outputText.Text)) return;

        ToggleUi(false);
        openOutput.Enabled = false;
        AppendLog("Descompilando...");

        try
        {
            DecompileResult result;
            if (directModel is not null)
            {
                var input = directModel;
                var addon = outputText.Text;
                result = await Task.Run(() =>
                {
                    using var stream = File.OpenRead(input);
                    using var resource = new ValveResourceFormat.Resource { FileName = input };
                    resource.Read(stream);
                    using var loader = new ValveResourceFormat.IO.GameFileLoader(null, input);
                    var modelPath = Path.GetFileName(input);
                    return new AddonModelDecompiler().Decompile(resource, loader, modelPath, addon);
                });
            }
            else if (archive is not null && selectedVpkModel is not null)
            {
                var model = selectedVpkModel;
                var addon = outputText.Text;
                result = await Task.Run(() => archive.Decompile(model, addon));
            }
            else return;

            AppendLog($"VMDL: {result.VmdlPath}");
            AppendLog($"GLTF: {result.GltfPath}");
            AppendLog("OK");
            openOutput.Enabled = true;
            MessageBox.Show(this, "VMDL, GLTF y DMX generados dentro del addon.", "Terminado", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex}");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { ToggleUi(true); }
    }

    private async Task GenerateCapsulesAsync()
    {
        using var d = new OpenFileDialog
        {
            Filter = "PHYS dump (*.txt)|*.txt",
            Title = "Selecciona phys.txt",
            FileName = "phys.txt"
        };
        if (d.ShowDialog(this) != DialogResult.OK) return;

        generateCapsules.Enabled = false;
        AppendLog($"Probando helpers PHYS con: {d.FileName}");

        try
        {
            var results = await Task.Run(() => LegacyPhysToolRunner.Run(d.FileName));
            if (results.Count == 0)
                AppendLog("Los EXE terminaron, pero no se detectaron TXT nuevos.");
            else
                foreach (var result in results) AppendLog($"TXT generado: {result}");

            MessageBox.Show(this,
                results.Count == 0 ? "Los helpers terminaron sin TXT nuevos." : $"Helpers terminados. TXT generados: {results.Count}",
                "Prueba PHYS", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR PHYS: {ex}");
            MessageBox.Show(this, ex.Message, "Error PHYS", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { generateCapsules.Enabled = true; }
    }

    private void ToggleUi(bool enabled)
    {
        openModel.Enabled = enabled;
        openVpk.Enabled = enabled;
        chooseOutput.Enabled = enabled;
        tree.Enabled = enabled;
        search.Enabled = enabled;
        decompile.Enabled = enabled && (directModel is not null || selectedVpkModel is not null);
    }

    private void OpenOutputFolder()
    {
        if (Directory.Exists(outputText.Text))
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outputText.Text}\"") { UseShellExecute = true });
    }

    private void OnDragEnter(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy;
    }

    private void OnDragDrop(object? sender, DragEventArgs e)
    {
        if (e.Data?.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        var path = files[0];
        if (path.EndsWith(".vmdl_c", StringComparison.OrdinalIgnoreCase)) LoadDirectModel(path);
        else if (path.EndsWith(".vpk", StringComparison.OrdinalIgnoreCase)) LoadVpk(path);
    }

    private void AppendLog(string value) => log.AppendText(value + Environment.NewLine);
}
