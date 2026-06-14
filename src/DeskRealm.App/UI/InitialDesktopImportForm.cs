using System.Drawing;
using System.Windows.Forms;
using DeskRealm.App.Services;

namespace DeskRealm.App.UI;

internal sealed class InitialDesktopImportForm : Form
{
    private readonly ComboBox _desktopCombo;
    private readonly CheckBox _saveLayoutCheck;

    public Guid SelectedDesktopId { get; private set; }
    public bool LinkOriginalDesktop => true;
    public bool SaveLayout => _saveLayoutCheck.Checked;

    public InitialDesktopImportForm(IReadOnlyList<VirtualDesktopInfo> desktops, Guid currentDesktopId)
    {
        if (desktops.Count == 0)
        {
            throw new InvalidOperationException("No Windows virtual desktop was detected for the initial Desktop import.");
        }

        Text = "DeskRealm — Initial Desktop import";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Width = 560;
        Height = 390;

        var titleFontPrototype = SystemFonts.MessageBoxFont ?? Font ?? new Font(FontFamily.GenericSansSerif, 9F);
        var title = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 16,
            Width = 510,
            Height = 42,
            Text = "Import the current Windows Desktop into DeskRealm?",
            Font = new Font(titleFontPrototype, FontStyle.Bold)
        };

        var intro = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 58,
            Width = 510,
            Height = 74,
            Text = "DeskRealm can associate your current Windows Desktop with a realm without moving your files. If DeskRealm is closed, your original Desktop stays intact and comes back normally.",
        };

        var desktopLabel = new Label
        {
            AutoSize = true,
            Left = 16,
            Top = 145,
            Text = "Assign the current Desktop to virtual desktop:"
        };

        _desktopCombo = new ComboBox
        {
            Left = 16,
            Top = 168,
            Width = 510,
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        foreach (var desktop in desktops.OrderBy(d => d.Number))
        {
            _desktopCombo.Items.Add(new DesktopChoice(desktop));
        }

        var selectedIndex = 0;
        for (var i = 0; i < _desktopCombo.Items.Count; i++)
        {
            if (_desktopCombo.Items[i] is DesktopChoice choice && choice.Desktop.Id == currentDesktopId)
            {
                selectedIndex = i;
                break;
            }
        }
        _desktopCombo.SelectedIndex = selectedIndex;

        _saveLayoutCheck = new CheckBox
        {
            Left = 16,
            Top = 208,
            Width = 510,
            Height = 24,
            Checked = true,
            Text = "Save current icon positions as the initial layout"
        };

        var warning = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 244,
            Width = 510,
            Height = 66,
            Text = "Safe mode: no file is moved. The selected realm points to the original Windows Desktop. Other realms stay isolated inside the DeskRealm folder."
        };

        var importButton = new Button
        {
            Left = 270,
            Top = 322,
            Width = 120,
            Height = 30,
            Text = "Associate",
            DialogResult = DialogResult.OK
        };
        importButton.Click += (_, _) =>
        {
            if (_desktopCombo.SelectedItem is not DesktopChoice choice)
            {
                MessageBox.Show("Select a target virtual desktop.", "DeskRealm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                DialogResult = DialogResult.None;
                return;
            }

            SelectedDesktopId = choice.Desktop.Id;
        };

        var skipButton = new Button
        {
            Left = 402,
            Top = 322,
            Width = 124,
            Height = 30,
            Text = "Skip",
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(title);
        Controls.Add(intro);
        Controls.Add(desktopLabel);
        Controls.Add(_desktopCombo);
        Controls.Add(_saveLayoutCheck);
        Controls.Add(warning);
        Controls.Add(importButton);
        Controls.Add(skipButton);

        AcceptButton = importButton;
        CancelButton = skipButton;
    }

    private sealed class DesktopChoice
    {
        public DesktopChoice(VirtualDesktopInfo desktop) => Desktop = desktop;
        public VirtualDesktopInfo Desktop { get; }

        public override string ToString() => $"#{Desktop.Number} — {Desktop.Name} {Desktop.Id:B}";
    }
}
