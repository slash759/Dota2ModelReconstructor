using Dota2ModelReconstructor.Vrf;
using System.Diagnostics;

namespace Dota2ModelReconstructor.Ui;

public sealed class MainForm : Form
{
    private readonly TextBox inputText = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly TextBox outputText = new() { Dock = DockStyle.Fill, ReadOnly = true };
    private readonly Button browseInput = new() { Text = "Seleccionar VMDL_C...", AutoSize = true };
    private readonly Button browseOutput = new() { Text = "Carpeta de salida...", AutoSize = true };
    private readonly Button decompile = new() { Text = "DESCOMPILAR", Height = 46, Dock = DockStyle.Top };
    private readonly Button openOutput = new() { Text = "Abrir carpeta de salida", AutoSize = true, Enabled = false };
    private readonly TextBox log = new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Vertical
    };

    public MainForm()
    {
        Text = "Dota 2 Model Reconstructor";
        Width = 820;
        Height = 520;
        MinimumSize = new Size(680, 420);
        StartPosition = FormStartPosition.CenterScreen;

        var grid = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        grid.Controls.Add(new Label { Text = "Modelo compilado (.vmdl_c)", AutoSize = true }, 0, 0);
        grid.SetColumnSpan(grid.Controls[^1], 2);
        grid.Controls.Add(inputText, 0, 1);
        grid.Controls.Add(browseInput, 1, 1);

        grid.Controls.Add(new Label { Text = "Carpeta de salida", AutoSize = true, Margin = new Padding(0, 12, 0, 3) }, 0, 2);
        grid.SetColumnSpan(grid.Controls[^1], 2);
        grid.Controls.Add(outputText, 0, 3);
        grid.Controls.Add(browseOutput, 1, 3);

        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true };
        actions.Controls.Add(openOutput);
        grid.Controls.Add(actions, 0, 4);
        grid.Controls.Add(decompile, 1, 4);

        grid.Controls.Add(log, 0, 5);
        grid.SetColumnSpan(log, 2);
        Controls.Add(grid);

        browseInput.Click += SelectInput;
        browseOutput.Click += SelectOutput;
        decompile.Click += async (_, _) => await DecompileAsync();
        openOutput.Click += (_, _) => OpenOutputFolder();

        AppendLog("S2V / ValveResourceFormat compatibility target: 19.2");
        AppendLog("Etapa temporal: VMDL_C -> VMDL + GLTF");
    }

    private void SelectInput(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "Source 2 compiled model (*.vmdl_c)|*.vmdl_c|All files (*.*)|*.*",
            Title = "Seleccionar modelo compilado"
        };

        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        inputText.Text = dialog.FileName;
        if (string.IsNullOrWhiteSpace(outputText.Text))
        {
            var dir = Path.GetDirectoryName(dialog.FileName)!;
            var name = Path.GetFileNameWithoutExtension(dialog.FileName);
            outputText.Text = Path.Combine(dir, name + "_decompiled");
        }
    }

    private void SelectOutput(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog { Description = "Seleccionar carpeta de salida" };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            outputText.Text = dialog.SelectedPath;
    }

    private async Task DecompileAsync()
    {
        if (!File.Exists(inputText.Text))
        {
            MessageBox.Show(this, "Selecciona un archivo .vmdl_c válido.", "Falta el modelo",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (string.IsNullOrWhiteSpace(outputText.Text))
        {
            MessageBox.Show(this, "Selecciona una carpeta de salida.", "Falta la salida",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        ToggleUi(false);
        log.Clear();
        AppendLog($"Entrada: {inputText.Text}");
        AppendLog($"Salida : {outputText.Text}");
        AppendLog("Descompilando...");

        try
        {
            var input = inputText.Text;
            var output = outputText.Text;
            var result = await Task.Run(() => new VrfModelDecompiler().Decompile(input, output));

            AppendLog("");
            AppendLog("OK");
            AppendLog($"VMDL: {result.VmdlPath}");
            AppendLog($"GLTF: {result.GltfPath}");
            openOutput.Enabled = true;

            MessageBox.Show(this, "Descompilación terminada.", "Dota 2 Model Reconstructor",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("");
            AppendLog("ERROR:");
            AppendLog(ex.ToString());
            MessageBox.Show(this, ex.Message, "Error al descompilar",
                MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            ToggleUi(true);
        }
    }

    private void ToggleUi(bool enabled)
    {
        browseInput.Enabled = enabled;
        browseOutput.Enabled = enabled;
        decompile.Enabled = enabled;
    }

    private void OpenOutputFolder()
    {
        if (!Directory.Exists(outputText.Text)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{outputText.Text}\"") { UseShellExecute = true });
    }

    private void AppendLog(string text) => log.AppendText(text + Environment.NewLine);
}
