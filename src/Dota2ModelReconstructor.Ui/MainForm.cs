using Dota2ModelReconstructor.Vrf;
using System.Diagnostics;

namespace Dota2ModelReconstructor.Ui;

public sealed class MainForm : Form
{
    private readonly TreeView tree = new() { Dock = DockStyle.Fill, HideSelection = false };
    private readonly TextBox outputText = new() { Dock = DockStyle.Top, ReadOnly = true };
    private readonly TextBox log = new() { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly Button openModel = new() { Text = "Open VMDL", AutoSize = true };
    private readonly Button openVpk = new() { Text = "Open VPK", AutoSize = true };
    private readonly Button chooseOutput = new() { Text = "Select Addon Folder", AutoSize = true };
    private readonly Button decompile = new() { Text = "DECOMPILE", Height = 48, Dock = DockStyle.Top, Enabled = false };
    private readonly Button openOutput = new() { Text = "Open Addon Folder", Enabled = false, AutoSize = true };
    private readonly Button decompileSnap = new() { Text = "Decompile SNAP", AutoSize = true };

    private VpkModelArchive? archive;
    private string? directModel;
    private string? selectedVpkModel;

    public MainForm()
    {
        Text = "DOTA 2 Model Reconstructor";
        Width = 1050;
        Height = 680;
        MinimumSize = new Size(800, 500);
        StartPosition = FormStartPosition.CenterScreen;
        AllowDrop = true;

        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
        toolbar.Controls.AddRange([openModel, openVpk, chooseOutput, openOutput]);

        var extraToolbar = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42, Padding = new Padding(6) };
        extraToolbar.Controls.Add(new Label { Text = "Extra", AutoSize = true, Margin = new Padding(0, 7, 8, 0) });
        extraToolbar.Controls.Add(decompileSnap);

        var left = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8) };
        left.Controls.Add(tree);
        left.Controls.Add(new Label { Text = "VPK Models", Dock = DockStyle.Top, Height = 24 });

        var right = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        right.Controls.Add(decompile);
        right.Controls.Add(new Label { Text = "Addon Folder", Dock = DockStyle.Top, Height = 22 });
        right.Controls.Add(outputText);

        var split = new SplitContainer { Dock = DockStyle.Fill, SplitterDistance = 430 };
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(right);

        var logPanel = new Panel { Dock = DockStyle.Bottom, Height = 145, Padding = new Padding(8) };
        logPanel.Controls.Add(log);

        Controls.Add(split);
        Controls.Add(logPanel);
        Controls.Add(extraToolbar);
        Controls.Add(toolbar);

        openModel.Click += (_, _) => BrowseModel();
        openVpk.Click += (_, _) => BrowseVpk();
        chooseOutput.Click += (_, _) => BrowseOutput();
        openOutput.Click += (_, _) => OpenOutputFolder();
        decompileSnap.Click += async (_, _) => await DecompileSnapAsync();
        decompile.Click += async (_, _) => await DecompileAsync();
        tree.AfterSelect += (_, e) => SelectTreeModel(e.Node);
        DragEnter += OnDragEnter;
        DragDrop += OnDragDrop;

        AppendLog("Powered by Source 2 Viewer (ValveResourceFormat)");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) archive?.Dispose();
        base.Dispose(disposing);
    }

    private void BrowseModel()
    {
        using var d = new OpenFileDialog { Filter = "Compiled model (*.vmdl_c)|*.vmdl_c", Title = "Open VMDL_C" };
        if (d.ShowDialog(this) == DialogResult.OK) LoadDirectModel(d.FileName);
    }

    private void BrowseVpk()
    {
        using var d = new OpenFileDialog { Filter = "Valve package (*.vpk)|*.vpk", Title = "Open Dota 2 VPK" };
        if (d.ShowDialog(this) == DialogResult.OK) LoadVpk(d.FileName);
    }

    private void BrowseOutput()
    {
        using var d = new FolderBrowserDialog { Description = "Select the Dota 2 addon root folder" };
        if (d.ShowDialog(this) == DialogResult.OK) outputText.Text = d.SelectedPath;
    }

    private void LoadDirectModel(string path)
    {
        archive?.Dispose();
        archive = null;
        selectedVpkModel = null;
        directModel = Path.GetFullPath(path);
        tree.Nodes.Clear();
        decompile.Enabled = true;
        AppendLog($"Opened: {directModel}");
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
                    RebuildTree(string.Empty);
            decompile.Enabled = false;
            AppendLog($"VPK opened: {archive.FileName}");
            AppendLog($"VMDL_C models found: {archive.Models.Count:N0}");
        }
        catch (Exception ex)
        {
            archive?.Dispose();
            archive = null;
            MessageBox.Show(this, ex.Message, "Could not open VPK", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
        decompile.Enabled = archive is not null;
    }

    private async Task DecompileAsync()
    {
        if (string.IsNullOrWhiteSpace(outputText.Text)) BrowseOutput();
        if (string.IsNullOrWhiteSpace(outputText.Text)) return;

        ToggleUi(false);
        openOutput.Enabled = false;
        AppendLog("Decompiling...");

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

            AppendLog("Decompile completed successfully.");
            openOutput.Enabled = true;
            MessageBox.Show(this, "Model reconstruction completed.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"ERROR: {ex}");
            MessageBox.Show(this, ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { ToggleUi(true); }
    }

    private async Task DecompileSnapAsync()
    {
        using var input = new OpenFileDialog
        {
            Filter = "SNAP text (*.txt)|*.txt|Text files (*.txt)|*.txt",
            Title = "Select SNAP text file"
        };
        if (input.ShowDialog(this) != DialogResult.OK) return;

        using var output = new SaveFileDialog
        {
            Filter = "Blender Python script (*.py)|*.py",
            FileName = "snap_blender.py",
            Title = "Save Blender SNAP script"
        };
        if (output.ShowDialog(this) != DialogResult.OK) return;

        decompileSnap.Enabled = false;
        AppendLog("Generating Blender SNAP script...");
        try
        {
            var generated = await Task.Run(() => LegacySnapToolRunner.Run(input.FileName));
            File.Copy(generated, output.FileName, overwrite: true);
            AppendLog("SNAP script generated successfully.");
            MessageBox.Show(this, "Blender SNAP script generated.", "Done", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog($"SNAP ERROR: {ex}");
            MessageBox.Show(this, ex.Message, "SNAP Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { decompileSnap.Enabled = true; }
    }

    private void ToggleUi(bool enabled)
    {
        openModel.Enabled = enabled;
        openVpk.Enabled = enabled;
        chooseOutput.Enabled = enabled;
        decompileSnap.Enabled = enabled;
        tree.Enabled = enabled;
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
