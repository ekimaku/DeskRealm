using System.Drawing;
using System.Windows.Forms;
using DeskRealm.App.Services;

namespace DeskRealm.App.UI;

internal sealed class InitialDesktopImportForm : Form
{
    private readonly ComboBox _desktopCombo;
    private readonly CheckBox _moveFilesCheck;
    private readonly CheckBox _saveLayoutCheck;

    public Guid SelectedDesktopId { get; private set; }
    public bool MoveFiles => _moveFilesCheck.Checked;
    public bool SaveLayout => _saveLayoutCheck.Checked;

    public InitialDesktopImportForm(IReadOnlyList<VirtualDesktopInfo> desktops, Guid currentDesktopId)
    {
        if (desktops.Count == 0)
        {
            throw new InvalidOperationException("Aucun bureau virtuel Windows détecté pour l'import Desktop initial.");
        }

        Text = "DeskRealm — Import Desktop initial";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        Width = 560;
        Height = 390;

        var title = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 16,
            Width = 510,
            Height = 42,
            Text = "Importer le Desktop Windows actuel dans DeskRealm ?",
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold)
        };

        var intro = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 58,
            Width = 510,
            Height = 74,
            Text = "DeskRealm peut transformer ton bureau Windows actuel en premier realm : les fichiers du Desktop original sont déplacés vers le realm choisi et la position actuelle des icônes est enregistrée comme layout initial.",
        };

        var desktopLabel = new Label
        {
            AutoSize = true,
            Left = 16,
            Top = 145,
            Text = "Affecter le Desktop actuel au bureau virtuel :"
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

        _moveFilesCheck = new CheckBox
        {
            Left = 16,
            Top = 208,
            Width = 510,
            Height = 24,
            Checked = true,
            Text = "Déplacer les fichiers/raccourcis du Desktop original vers ce realm"
        };

        _saveLayoutCheck = new CheckBox
        {
            Left = 16,
            Top = 236,
            Width = 510,
            Height = 24,
            Checked = true,
            Text = "Enregistrer la position actuelle des icônes comme layout initial"
        };

        var warning = new Label
        {
            AutoSize = false,
            Left = 16,
            Top = 272,
            Width = 510,
            Height = 38,
            Text = "DeskRealm ne déplace pas son dossier Realms ni desktop.ini. En cas de conflit de nom dans le realm cible, l'import est refusé explicitement."
        };

        var importButton = new Button
        {
            Left = 270,
            Top = 322,
            Width = 120,
            Height = 30,
            Text = "Importer",
            DialogResult = DialogResult.OK
        };
        importButton.Click += (_, _) =>
        {
            if (_desktopCombo.SelectedItem is not DesktopChoice choice)
            {
                MessageBox.Show("Sélectionne un bureau virtuel cible.", "DeskRealm", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            Text = "Ignorer",
            DialogResult = DialogResult.Cancel
        };

        Controls.Add(title);
        Controls.Add(intro);
        Controls.Add(desktopLabel);
        Controls.Add(_desktopCombo);
        Controls.Add(_moveFilesCheck);
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
