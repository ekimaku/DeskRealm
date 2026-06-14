using DeskRealm.App.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DeskRealm.App.UI;

internal sealed class DeskRealmMainForm : Form
{
    private static readonly Color ShellBackground = Color.FromArgb(5, 16, 25);
    private static readonly Color ShellPanel = Color.FromArgb(9, 32, 47);
    private static readonly Color ShellPanelSoft = Color.FromArgb(13, 49, 58);
    private static readonly Color ShellPanelDisabled = Color.FromArgb(31, 40, 49);
    private static readonly Color ShellBorder = Color.FromArgb(24, 92, 107);
    private static readonly Color ShellBorderStrong = Color.FromArgb(43, 214, 222);
    private static readonly Color ShellText = Color.FromArgb(231, 251, 255);
    private static readonly Color ShellMuted = Color.FromArgb(142, 183, 198);
    private static readonly Color ShellAccent = Color.FromArgb(45, 224, 220);
    private static readonly Color ShellAmber = Color.FromArgb(222, 146, 67);
    private static readonly Color ShellDanger = Color.FromArgb(210, 111, 94);

    private const int WindowResizeGrip = 8;
    private const int WmNcHitTest = 0x0084;
    private const int HtLeft = 10;
    private const int HtRight = 11;
    private const int HtTop = 12;
    private const int HtTopLeft = 13;
    private const int HtTopRight = 14;
    private const int HtBottom = 15;
    private const int HtBottomLeft = 16;
    private const int HtBottomRight = 17;
    private const int WmNcLButtonDown = 0x00A1;
    private const int HtCaption = 0x0002;

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);

    private const int VkShift = 0x10;
    private const int VkControl = 0x11;
    private const int VkAlt = 0x12;
    private const int VkLWin = 0x5B;
    private const int VkRWin = 0x5C;

    private readonly DesktopSwitchService _switchService;
    private readonly RealmConfigService _configService;
    private readonly StartupService _startupService;
    private readonly Action _reloadHotkeys;
    private readonly Action _exitApplication;
    private readonly Action _firstRunCompleted;
    private readonly FileLogger _logger;

    private readonly TextBox _statusBox = new()
    {
        Multiline = true,
        ReadOnly = true,
        ScrollBars = ScrollBars.Vertical,
        Font = new Font("Consolas", 9.5F),
        Dock = DockStyle.Fill,
        BorderStyle = BorderStyle.None
    };

    private readonly CheckBox _hotkeysEnabledCheck = new()
    {
        Text = "Enable global DeskRealm hotkeys",
        AutoSize = true
    };

    private readonly Dictionary<int, TextBox> _hotkeyTextBoxes = new();
    private readonly CheckBox _startWithWindowsCheck = new() { Text = "Start DeskRealm with Windows", AutoSize = true };
    private readonly CheckBox _enabledCheck = new() { Text = "Enable realm switching automation", AutoSize = true };
    private readonly ComboBox _desktopCombo = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 470 };
    private readonly CheckBox _saveInitialLayoutCheck = new() { Text = "Save current icon layout", AutoSize = true, Checked = true };
    private readonly Label _lockStateLabel = new() { AutoSize = false, Height = 48, Width = 780 };
    private readonly Panel _firstRunPanel = new() { Dock = DockStyle.Top, Height = 255, Padding = new Padding(14) };
    private readonly Label _firstRunStateLabel = new() { AutoSize = false, Height = 40, Dock = DockStyle.Bottom };
    private readonly FlowLayoutPanel _iconLayoutRows = new()
    {
        Dock = DockStyle.Fill,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        AutoScroll = true,
        Padding = new Padding(2),
        BackColor = Color.FromArgb(7, 28, 41)
    };
    private readonly Label _iconLayoutSummaryLabel = new() { AutoSize = false, Dock = DockStyle.Fill, Height = 42 };
    private readonly HashSet<string> _expandedRealmKeys = new(StringComparer.OrdinalIgnoreCase);
    private readonly ToolTip _toolTip = new();
    private readonly Panel _pageHost = new() { Dock = DockStyle.Fill, BackColor = ShellBackground };
    private readonly Dictionary<string, Control> _pages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ModernButton> _navigationButtons = new(StringComparer.OrdinalIgnoreCase);
    private string _activePageKey = "Overview";
    private int? _capturingHotkeyDesktopNumber;
    private TextBox? _capturingHotkeyBox;
    private string _capturingHotkeyOriginalText = string.Empty;

    private bool _allowRealClose;

    private static Font CreateUiFont(float size)
    {
        return new Font(SystemFonts.MessageBoxFont!.FontFamily, size);
    }

    private static Font CreateBoldUiFont()
    {
        return new Font(SystemFonts.MessageBoxFont!, FontStyle.Bold);
    }

    public DeskRealmMainForm(
        DesktopSwitchService switchService,
        RealmConfigService configService,
        StartupService startupService,
        Action reloadHotkeys,
        Action exitApplication,
        Action firstRunCompleted,
        FileLogger logger)
    {
        _switchService = switchService;
        _configService = configService;
        _startupService = startupService;
        _reloadHotkeys = reloadHotkeys;
        _exitApplication = exitApplication;
        _firstRunCompleted = firstRunCompleted;
        _logger = logger;

        Text = "DeskRealm";
        Icon = DeskRealmIcon.Load(_logger);
        FormBorderStyle = FormBorderStyle.None;
        DoubleBuffered = true;
        StartPosition = FormStartPosition.CenterScreen;
        Width = 1120;
        Height = 780;
        MinimumSize = new Size(960, 680);
        Font = CreateUiFont(9.5F);
        KeyPreview = true;
        ShowInTaskbar = true;
        BackColor = ShellBackground;
        ForeColor = ShellText;

        Controls.Add(BuildRootLayout());
        ApplyTheme(this);
        Load += (_, _) => RefreshAll();
    }

    public void ShowFirstRunPanel(bool visible)
    {
        _firstRunPanel.Visible = visible;
        if (visible)
        {
            RefreshDesktopChoices();
        }

        RefreshAll();
    }

    public void PrepareForApplicationExit()
    {
        _allowRealClose = true;
    }

    public void RefreshAll()
    {
        RefreshOptionsFromConfig();
        RefreshHotkeysFromConfig();
        RefreshLockState();
        RefreshIconLayoutTree();
        RefreshStatus();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!_allowRealClose && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            return;
        }

        base.OnFormClosing(e);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmNcHitTest)
        {
            base.WndProc(ref m);
            if (WindowState == FormWindowState.Maximized)
            {
                return;
            }

            var cursor = PointToClient(Cursor.Position);
            var left = cursor.X <= WindowResizeGrip;
            var right = cursor.X >= ClientSize.Width - WindowResizeGrip;
            var top = cursor.Y <= WindowResizeGrip;
            var bottom = cursor.Y >= ClientSize.Height - WindowResizeGrip;

            if (left && top)
            {
                m.Result = (IntPtr)HtTopLeft;
                return;
            }

            if (right && top)
            {
                m.Result = (IntPtr)HtTopRight;
                return;
            }

            if (left && bottom)
            {
                m.Result = (IntPtr)HtBottomLeft;
                return;
            }

            if (right && bottom)
            {
                m.Result = (IntPtr)HtBottomRight;
                return;
            }

            if (left)
            {
                m.Result = (IntPtr)HtLeft;
                return;
            }

            if (right)
            {
                m.Result = (IntPtr)HtRight;
                return;
            }

            if (top)
            {
                m.Result = (IntPtr)HtTop;
                return;
            }

            if (bottom)
            {
                m.Result = (IntPtr)HtBottom;
                return;
            }

            return;
        }

        base.WndProc(ref m);
    }

    private void BeginWindowDrag()
    {
        ReleaseCapture();
        _ = SendMessage(Handle, WmNcLButtonDown, HtCaption, 0);
    }

    private Control BuildRootLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 6,
            Padding = new Padding(18, 10, 18, 18),
            BackColor = ShellBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        root.Controls.Add(BuildWindowChrome(), 0, 0);
        BuildFirstRunPanel();
        root.Controls.Add(_firstRunPanel, 0, 1);
        root.Controls.Add(BuildTopHeader(), 0, 2);
        root.Controls.Add(BuildNavigation(), 0, 3);

        _pages.Clear();
        _pageHost.Controls.Clear();
        AddPage("Overview", BuildWelcomePage());
        AddPage("Hotkeys", BuildHotkeysPage());
        AddPage("Icon Layout", BuildIconLayoutPage());
        AddPage("Actions", BuildActionsPage());
        AddPage("Status", BuildStatusPage());
        root.Controls.Add(_pageHost, 0, 4);
        ShowPage(_activePageKey);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(0, 14, 0, 0),
            BackColor = ShellBackground
        };

        var quit = CreateActionButton("Quit DeskRealm", 154, ShellDanger);
        quit.Click += (_, _) => _exitApplication();
        var hide = CreateActionButton("Hide to tray", 154, ShellBorderStrong);
        hide.Click += (_, _) => Hide();
        footer.Controls.Add(quit);
        footer.Controls.Add(hide);
        root.Controls.Add(footer, 0, 5);

        return root;
    }

    private Control BuildWindowChrome()
    {
        var chrome = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 34,
            ColumnCount = 3,
            RowCount = 1,
            BackColor = ShellBackground,
            Margin = new Padding(0, 0, 0, 6)
        };
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        chrome.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var caption = new Label
        {
            Text = "DeskRealm",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = ShellMuted,
            Font = CreateBoldUiFont(),
            Padding = new Padding(4, 0, 0, 0)
        };
        caption.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                BeginWindowDrag();
            }
        };
        chrome.MouseDown += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                BeginWindowDrag();
            }
        };
        chrome.Controls.Add(caption, 0, 0);

        var minimize = CreateChromeButton("−");
        minimize.Click += (_, _) => WindowState = FormWindowState.Minimized;
        chrome.Controls.Add(minimize, 1, 0);

        var close = CreateChromeButton("×");
        close.AccentColor = ShellDanger;
        close.MutedAccentColor = Color.FromArgb(100, 54, 48);
        close.Click += (_, _) => Hide();
        chrome.Controls.Add(close, 2, 0);

        return chrome;
    }

    private ModernButton CreateChromeButton(string label)
    {
        return new ModernButton
        {
            Text = label,
            Width = 40,
            Height = 28,
            Radius = 11,
            FillColor = ShellBackground,
            HoverFillColor = Color.FromArgb(13, 49, 58),
            PressedFillColor = Color.FromArgb(18, 71, 81),
            AccentColor = ShellBorderStrong,
            MutedAccentColor = ShellBackground,
            ForeColor = ShellText,
            Font = CreateBoldUiFont(),
            Margin = new Padding(4, 0, 0, 0)
        };
    }

    private Control BuildTopHeader()
    {
        var header = new RoundedPanel
        {
            Dock = DockStyle.Top,
            Height = 112,
            FillColor = Color.FromArgb(7, 25, 38),
            BorderColor = ShellBorder,
            Radius = 24,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 12)
        };

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            BackColor = header.FillColor
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var copy = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = header.FillColor
        };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        copy.Controls.Add(new Label
        {
            Text = "DeskRealm",
            AutoSize = true,
            Font = new Font(CreateBoldUiFont().FontFamily, 22F, FontStyle.Bold),
            ForeColor = ShellText,
            Margin = new Padding(0, 0, 0, 4)
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            Text = "Quiet virtual-desktop realms · icon layouts · protected local workflow",
            AutoSize = true,
            ForeColor = ShellMuted,
            Margin = new Padding(2, 0, 0, 0)
        }, 0, 1);
        layout.Controls.Add(copy, 0, 0);

        var pill = new Label
        {
            Text = "v0.5.9 UX",
            AutoSize = false,
            Width = 128,
            Height = 32,
            TextAlign = ContentAlignment.MiddleCenter,
            ForeColor = ShellAccent,
            BackColor = Color.FromArgb(10, 45, 53),
            Margin = new Padding(0, 12, 0, 0)
        };
        layout.Controls.Add(pill, 1, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Control BuildNavigation()
    {
        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            Height = 48,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoScroll = false,
            BackColor = ShellBackground,
            Margin = new Padding(0, 0, 0, 10)
        };

        AddNavigationButton(nav, "Overview");
        AddNavigationButton(nav, "Hotkeys");
        AddNavigationButton(nav, "Icon Layout");
        AddNavigationButton(nav, "Actions");
        AddNavigationButton(nav, "Status");
        return nav;
    }

    private void AddNavigationButton(Control parent, string key)
    {
        var button = new ModernButton
        {
            Text = key,
            Width = key == "Icon Layout" ? 150 : 126,
            Height = 36,
            Radius = 18,
            FillColor = Color.FromArgb(8, 28, 41),
            HoverFillColor = Color.FromArgb(12, 58, 68),
            PressedFillColor = Color.FromArgb(16, 78, 88),
            AccentColor = ShellAccent,
            MutedAccentColor = ShellBorder,
            ForeColor = ShellText,
            Font = CreateBoldUiFont(),
            Margin = new Padding(0, 0, 10, 0)
        };
        button.Click += (_, _) => ShowPage(key);
        _navigationButtons[key] = button;
        parent.Controls.Add(button);
    }

    private void AddPage(string key, Control page)
    {
        page.Dock = DockStyle.Fill;
        page.Visible = false;
        _pages[key] = page;
        _pageHost.Controls.Add(page);
    }

    private void ShowPage(string key)
    {
        if (!_pages.ContainsKey(key))
        {
            key = "Overview";
        }

        _activePageKey = key;
        foreach (var pair in _pages)
        {
            pair.Value.Visible = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var pair in _navigationButtons)
        {
            pair.Value.Selected = string.Equals(pair.Key, key, StringComparison.OrdinalIgnoreCase);
            pair.Value.Invalidate();
        }
    }

    private void BuildFirstRunPanel()
    {
        _firstRunPanel.BorderStyle = BorderStyle.None;
        _firstRunPanel.Visible = false;
        _firstRunPanel.BackColor = ShellPanel;
        _firstRunPanel.ForeColor = ShellText;

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 7,
            BackColor = ShellPanel
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label
        {
            Text = "First run — choose what happens to your current Desktop",
            Font = CreateBoldUiFont(),
            AutoSize = true,
            ForeColor = ShellAccent,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);

        layout.Controls.Add(new Label
        {
            Text = "DeskRealm can associate the current Windows Desktop with one virtual desktop realm without moving your files. " +
                   "If you skip it, DeskRealm creates shortcuts to the original Desktop inside managed realms so it remains easy to find.",
            AutoSize = false,
            Height = 44,
            Dock = DockStyle.Fill,
            ForeColor = ShellText
        }, 0, 1);

        layout.Controls.Add(new Label { Text = "Associate the current Desktop with virtual desktop:", AutoSize = true, ForeColor = ShellMuted }, 0, 2);
        layout.Controls.Add(_desktopCombo, 0, 3);
        layout.Controls.Add(_saveInitialLayoutCheck, 0, 4);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, Dock = DockStyle.Fill, BackColor = ShellPanel };
        var assign = CreateActionButton("Associate current Desktop", 210, ShellBorderStrong);
        assign.Click += (_, _) => SafeAction("Associate initial Desktop", AssignInitialDesktop);
        var skip = CreateActionButton("Skip + create shortcuts", 210, ShellAmber);
        skip.Click += (_, _) => SafeAction("Skip initial Desktop import", SkipInitialDesktopImportWithShortcuts);
        buttons.Controls.Add(assign);
        buttons.Controls.Add(skip);
        layout.Controls.Add(buttons, 0, 5);

        _firstRunStateLabel.Text = "Strict mode: if association or shortcut creation fails, DeskRealm shows the error instead of continuing silently.";
        _firstRunStateLabel.ForeColor = ShellMuted;
        layout.Controls.Add(_firstRunStateLabel, 0, 6);

        _firstRunPanel.Controls.Add(layout);
    }

    private Control BuildWelcomePage()
    {
        var page = CreateTabPage("Overview");
        var text = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Vertical,
            Font = CreateUiFont(10F),
            BorderStyle = BorderStyle.None,
            BackColor = Color.FromArgb(7, 28, 41),
            ForeColor = ShellText,
            Text =
                "DeskRealm separates the visible Desktop content according to the active Windows virtual desktop." + Environment.NewLine + Environment.NewLine +
                "• You switch Windows virtual desktop." + Environment.NewLine +
                "• DeskRealm detects the current desktop." + Environment.NewLine +
                "• The Windows Desktop known folder is redirected to the realm assigned to that desktop." + Environment.NewLine +
                "• Icon layouts can be saved, restored and locked per realm/layout." + Environment.NewLine + Environment.NewLine +
                "DeskRealm stays discreet by design: closing this window hides it to the tray. " +
                "To really stop the app, use Quit DeskRealm from this UI or from the tray menu." + Environment.NewLine + Environment.NewLine +
                "Default hotkeys since v0.5.9:" + Environment.NewLine +
                "• Desktop 1: Win+Shift+X" + Environment.NewLine +
                "• Desktop 2: Win+Shift+C" + Environment.NewLine +
                "• Desktop 3: Win+Shift+B" + Environment.NewLine +
                "• Desktop 4: Win+Shift+N" + Environment.NewLine + Environment.NewLine +
                "These shortcuts avoid Win+Shift+W and Win+Shift+V, which are often already used by Windows or other tools."
        };
        page.Controls.Add(text);
        return page;
    }

    private Control BuildHotkeysPage()
    {
        var page = CreateTabPage("Hotkeys");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(14),
            BackColor = ShellBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        root.Controls.Add(_hotkeysEnabledCheck, 0, 0);

        var grid = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 9,
            AutoSize = true,
            Padding = new Padding(0, 12, 0, 12),
            BackColor = ShellBackground
        };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 320));
        grid.Controls.Add(CreateHeaderLabel("Desktop"), 0, 0);
        grid.Controls.Add(CreateHeaderLabel("Hotkey"), 1, 0);

        for (var i = 1; i <= 8; i++)
        {
            grid.Controls.Add(new Label { Text = $"#{i}", AutoSize = true, Margin = new Padding(0, 8, 0, 0), ForeColor = ShellText }, 0, i);
            var box = new TextBox
            {
                Width = 280,
                PlaceholderText = "click, then press Win/Ctrl/Alt/Shift + key",
                ReadOnly = true,
                Cursor = Cursors.Hand
            };
            ConfigureHotkeyCaptureBox(i, box);
            _hotkeyTextBoxes[i] = box;
            grid.Controls.Add(box, 1, i);
        }

        root.Controls.Add(grid, 0, 1);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.LeftToRight, AutoSize = true, BackColor = ShellBackground };
        var save = CreateActionButton("Save + reload", 150, ShellBorderStrong);
        save.Click += (_, _) => SafeAction("Save hotkeys", SaveHotkeysFromUi);
        var reset = CreateActionButton("Reset v0.5.9 defaults", 190, ShellAmber);
        reset.Click += (_, _) => SafeAction("Reset hotkeys", ResetHotkeysToDefaults);
        buttons.Controls.Add(save);
        buttons.Controls.Add(reset);
        root.Controls.Add(buttons, 0, 2);

        root.Controls.Add(new Label
        {
            Text = "Click a hotkey field, hold one or two modifiers (Win/Ctrl/Alt/Shift), then press one main key. Capture saves immediately on the first main key. Releasing all modifiers before a main key cancels the capture. Click Save + reload to apply. Duplicates and invalid shortcuts are rejected explicitly.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            ForeColor = ShellMuted
        }, 0, 3);

        page.Controls.Add(root);
        return page;
    }

    private Control BuildIconLayoutPage()
    {
        var page = CreateTabPage("Icon Layout");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(14),
            BackColor = ShellBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            AutoSize = true,
            BackColor = ShellBackground
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.Controls.Add(new Label
        {
            Text = "Icon Layout Locks",
            AutoSize = true,
            Font = CreateBoldUiFont(),
            ForeColor = ShellAccent,
            Margin = new Padding(0, 0, 0, 8)
        }, 0, 0);
        var refresh = CreateActionButton("Refresh", 110, ShellBorderStrong);
        refresh.Click += (_, _) => SafeAction("Refresh icon layout locks", RefreshIconLayoutTree);
        header.Controls.Add(refresh, 1, 0);
        root.Controls.Add(header, 0, 0);

        _iconLayoutSummaryLabel.Text = "Lock a layout to protect known icon positions. Lock a realm to protect every layout assigned to that realm.";
        _iconLayoutSummaryLabel.ForeColor = ShellMuted;
        root.Controls.Add(_iconLayoutSummaryLabel, 0, 1);

        var holder = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = Color.FromArgb(7, 28, 41),
            BorderColor = ShellBorder,
            Radius = 22,
            Padding = new Padding(12)
        };
        holder.Controls.Add(_iconLayoutRows);
        root.Controls.Add(holder, 0, 2);

        page.Controls.Add(root);
        return page;
    }

    private Control BuildActionsPage()
    {
        var page = CreateTabPage("Actions");
        var root = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = new Padding(14),
            AutoScroll = true,
            BackColor = ShellBackground
        };

        root.Controls.Add(_enabledCheck);
        root.Controls.Add(new Label
        {
            Text = "When disabled, DeskRealm pauses automatic realm switching and ignores DeskRealm desktop hotkeys. It does not quit the app, delete assignments, or remove any Desktop files.",
            AutoSize = false,
            Width = 760,
            Height = 42,
            ForeColor = ShellMuted,
            Margin = new Padding(0, 2, 0, 8)
        });
        root.Controls.Add(_startWithWindowsCheck);

        var apply = CreateActionButton("Apply options", 180, ShellBorderStrong);
        apply.Click += (_, _) => SafeAction("Apply options", ApplyOptions);
        root.Controls.Add(apply);

        root.Controls.Add(CreateSectionLabel("DeskRealm actions"));
        AddActionButton(root, "Refresh now", () => _switchService.SwitchNow());
        AddActionButton(root, "Sync names now", () => _switchService.SyncRealmNamesNow());
        AddActionButton(root, "Save icon layout now", ConfirmAndSaveIconLayoutNow);
        AddActionButton(root, "Restore icon layout now", () => _switchService.RestoreIconLayoutNow());
        AddActionButton(root, "Restore original Desktop", () => _switchService.RestoreOriginalDesktop());
        AddActionButton(root, "Create original Desktop shortcuts", () => _switchService.CreateOriginalDesktopShortcutsInManagedRealms());

        root.Controls.Add(CreateSectionLabel("Open"));
        AddActionButton(root, "Open realms", () => OpenPath(_switchService.Config.RealmsRoot!));
        AddActionButton(root, "Open config", () => OpenPath(AppPaths.ConfigPath));
        AddActionButton(root, "Open logs", () => OpenPath(AppPaths.LogFilePath));

        page.Controls.Add(root);
        return page;
    }

    private Control BuildStatusPage()
    {
        var page = CreateTabPage("Status");
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(14),
            BackColor = ShellBackground
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var refresh = CreateActionButton("Refresh status", 140, ShellBorderStrong);
        refresh.Click += (_, _) => RefreshStatus();
        root.Controls.Add(refresh, 0, 0);
        root.Controls.Add(_statusBox, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private Panel CreateTabPage(string text)
    {
        return new Panel
        {
            BackColor = ShellBackground,
            ForeColor = ShellText,
            Padding = new Padding(0)
        };
    }

    private Label CreateHeaderLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = CreateBoldUiFont(), ForeColor = ShellAccent };
    }

    private Label CreateSectionLabel(string text)
    {
        return new Label { Text = text, AutoSize = true, Font = CreateBoldUiFont(), ForeColor = ShellAccent, Margin = new Padding(0, 18, 0, 4) };
    }

    private ModernButton CreateActionButton(string label, int width, Color borderColor)
    {
        return new ModernButton
        {
            Text = label,
            Width = width,
            Height = 36,
            Radius = 16,
            FillColor = ShellPanelSoft,
            HoverFillColor = Color.FromArgb(18, 71, 81),
            PressedFillColor = Color.FromArgb(20, 92, 102),
            AccentColor = borderColor,
            MutedAccentColor = Color.FromArgb(Math.Max(0, borderColor.R - 35), Math.Max(0, borderColor.G - 75), Math.Max(0, borderColor.B - 75)),
            ForeColor = ShellText,
            Font = CreateBoldUiFont(),
            Margin = new Padding(4)
        };
    }

    private void AddActionButton(Control parent, string label, Action action)
    {
        var button = CreateActionButton(label, 270, ShellBorderStrong);
        button.Click += (_, _) => SafeAction(label, action);
        parent.Controls.Add(button);
    }

    private void RefreshIconLayoutTree()
    {
        if (_iconLayoutRows.IsDisposed)
        {
            return;
        }

        _iconLayoutRows.SuspendLayout();
        try
        {
            _iconLayoutRows.Controls.Clear();
            var realms = _switchService.GetIconLayoutLockSnapshot();
            var variantCount = realms.Sum(r => r.Layouts.Sum(l => l.Variants.Count));
            var lockedRealms = realms.Count(r => r.IsLocked);
            var lockedVariants = realms.Sum(r => r.Layouts.Sum(l => l.Variants.Count(v => v.IsVariantLocked)));
            _iconLayoutSummaryLabel.Text = $"{realms.Count} realm(s), {variantCount} layout variant(s), {lockedRealms} locked realm(s), {lockedVariants} locked variant(s). Existing icon positions stay protected; new icons can still be captured once.";

            foreach (var realm in realms)
            {
                if (realm.ContainsCurrent)
                {
                    _expandedRealmKeys.Add(realm.RealmKey);
                }

                _iconLayoutRows.Controls.Add(BuildRealmLockCard(realm));
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Icon layout tree refresh failed", ex);
            _iconLayoutRows.Controls.Add(new Label
            {
                Text = "Icon Layout state unavailable: " + ex.Message,
                ForeColor = ShellDanger,
                AutoSize = true
            });
        }
        finally
        {
            _iconLayoutRows.ResumeLayout(true);
        }
    }

    private Control BuildRealmLockCard(IconLayoutRealmSnapshot realm)
    {
        const int headerHeight = 60;
        const int layoutRowHeight = 76;
        const int cardVerticalPadding = 24;

        var expanded = _expandedRealmKeys.Contains(realm.RealmKey);
        var variantCount = realm.Layouts.Sum(layout => layout.Variants.Count);
        var expandedHeight = headerHeight + 12 + (variantCount * layoutRowHeight) + cardVerticalPadding;
        var card = new RoundedPanel
        {
            Width = Math.Max(920, _iconLayoutRows.ClientSize.Width - 34),
            Height = expanded ? Math.Max(126, expandedHeight) : 86,
            FillColor = realm.IsLocked ? Color.FromArgb(22, 34, 43) : Color.FromArgb(8, 38, 52),
            BorderColor = realm.ContainsCurrent ? ShellBorderStrong : ShellBorder,
            Radius = 18,
            BorderThickness = realm.ContainsCurrent ? 2 : 1,
            Margin = new Padding(0, 0, 0, 12),
            Padding = new Padding(14)
        };

        var cardLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = expanded ? 2 : 1,
            BackColor = card.FillColor
        };
        cardLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, headerHeight));
        if (expanded)
        {
            cardLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        }

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Height = headerHeight,
            ColumnCount = 4,
            BackColor = card.FillColor,
            Padding = new Padding(0, 4, 0, 4)
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var arrow = CreateActionButton(expanded ? "⌄" : "›", 42, ShellBorderStrong);
        arrow.Height = 34;
        arrow.Margin = new Padding(0, 8, 10, 8);
        arrow.Click += (_, _) =>
        {
            if (!_expandedRealmKeys.Remove(realm.RealmKey))
            {
                _expandedRealmKeys.Add(realm.RealmKey);
            }
            RefreshIconLayoutTree();
        };
        header.Controls.Add(arrow, 0, 0);

        var currentMarker = realm.ContainsCurrent ? "  • CURRENT" : string.Empty;
        var titleStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = card.FillColor,
            Margin = new Padding(4, 0, 0, 0)
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        titleStack.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var title = new Label
        {
            Text = $"Realm {realm.RealmNumber} — {realm.RealmName}{currentMarker}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = CreateBoldUiFont(),
            ForeColor = realm.IsLocked ? Color.FromArgb(164, 186, 195) : ShellText,
            Margin = new Padding(0)
        };
        _toolTip.SetToolTip(title, realm.RealmPath);
        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(new Label
        {
            Text = $"{variantCount} layout variant(s) · {realm.Layouts.Sum(l => l.Variants.Count(v => v.EffectiveLocked))} protected",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = ShellMuted,
            Margin = new Padding(0)
        }, 0, 1);
        header.Controls.Add(titleStack, 1, 0);

        var realmPill = CreatePill(realm.IsLocked ? "REALM LOCKED" : "REALM OPEN", realm.IsLocked ? ShellAmber : ShellAccent, 126);
        header.Controls.Add(realmPill, 2, 0);

        var lockButton = CreateActionButton(realm.IsLocked ? "Unlock" : "Lock", 86, realm.IsLocked ? ShellAmber : ShellBorderStrong);
        lockButton.Margin = new Padding(8, 8, 0, 8);
        _toolTip.SetToolTip(lockButton, realm.IsLocked ? "Unlock this realm" : "Lock this realm");
        lockButton.Click += (_, _) => SafeAction(realm.IsLocked ? "Unlock realm" : "Lock realm", () =>
        {
            var target = realm.Layouts.FirstOrDefault()?.DesktopId
                ?? throw new InvalidOperationException("Realm has no layout to lock.");
            if (realm.IsLocked)
            {
                _switchService.UnlockRealmLayoutsForDesktop(target);
            }
            else
            {
                _switchService.LockRealmLayoutsForDesktop(target);
            }

            RefreshIconLayoutTree();
        });
        header.Controls.Add(lockButton, 3, 0);
        cardLayout.Controls.Add(header, 0, 0);

        if (expanded)
        {
            var children = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = Math.Max(1, variantCount),
                Padding = new Padding(50, 6, 0, 0),
                BackColor = card.FillColor
            };
            children.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

            var rowIndex = 0;
            foreach (var desktopLayout in realm.Layouts.OrderBy(l => l.DesktopNumber))
            {
                foreach (var variant in desktopLayout.Variants)
                {
                    children.RowStyles.Add(new RowStyle(SizeType.Absolute, layoutRowHeight));
                    children.Controls.Add(BuildLayoutLockRow(desktopLayout, variant, realm.IsLocked, rowIndex + 1), 0, rowIndex);
                    rowIndex++;
                }
            }

            cardLayout.Controls.Add(children, 0, 1);
        }

        card.Controls.Add(cardLayout);
        return card;
    }

    private Control BuildLayoutLockRow(IconLayoutEntrySnapshot desktopLayout, IconLayoutVariantSnapshot variant, bool realmLocked, int layoutNumber)
    {
        var inheritedLock = realmLocked || desktopLayout.IsLayoutLocked;
        var rowPanel = new RoundedPanel
        {
            Dock = DockStyle.Fill,
            FillColor = inheritedLock ? Color.FromArgb(27, 37, 45) : Color.FromArgb(10, 49, 61),
            BorderColor = variant.IsCurrentTopology ? ShellBorderStrong : Color.FromArgb(23, 83, 96),
            BorderThickness = variant.IsCurrentTopology ? 2 : 1,
            Radius = 15,
            Margin = new Padding(0, 4, 0, 6),
            Padding = new Padding(12, 6, 12, 6)
        };

        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 1,
            BackColor = rowPanel.FillColor
        };
        row.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var currentText = variant.IsCurrentTopology ? "  • CURRENT" : string.Empty;
        var textStack = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            BackColor = rowPanel.FillColor,
            Margin = new Padding(0)
        };
        textStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        textStack.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        textStack.Controls.Add(new Label
        {
            Text = $"Layout {layoutNumber} — {desktopLayout.DesktopName}{currentText}",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.BottomLeft,
            Font = variant.IsCurrentTopology ? CreateBoldUiFont() : CreateUiFont(9.5F),
            ForeColor = inheritedLock ? Color.FromArgb(126, 146, 154) : ShellText,
            UseMnemonic = false
        }, 0, 0);
        var savedAt = variant.SavedAt.HasValue ? " · saved " + variant.SavedAt.Value.ToString("yyyy-MM-dd HH:mm") : string.Empty;
        textStack.Controls.Add(new Label
        {
            Text = variant.HasSavedLayout ? $"{variant.Summary} · {BuildVariantDisplaySummary(variant)} · {variant.IconCount} icon(s){savedAt}" : variant.Summary,
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = inheritedLock ? Color.FromArgb(107, 128, 138) : ShellMuted,
            UseMnemonic = false
        }, 0, 1);
        row.Controls.Add(textStack, 0, 0);

        if (variant.HasSavedLayout)
        {
            var deleteButton = CreateActionButton("Delete", 82, ShellDanger);
            deleteButton.Height = 32;
            deleteButton.Margin = new Padding(8, 6, 4, 6);
            deleteButton.Enabled = !inheritedLock;
            _toolTip.SetToolTip(deleteButton, inheritedLock
                ? realmLocked ? "Disabled because the parent realm is locked" : "Disabled because this desktop layout is globally locked"
                : "Delete this saved layout variant after confirmation");
            deleteButton.Click += (_, _) => SafeAction("Delete layout variant", () => ConfirmAndDeleteIconLayoutVariant(desktopLayout, variant));
            row.Controls.Add(deleteButton, 1, 0);
        }
        else
        {
            row.Controls.Add(CreatePill("EMPTY", ShellDanger, 82), 1, 0);
        }

        row.Controls.Add(CreatePill(variant.EffectiveLocked ? "LOCKED" : "OPEN", variant.EffectiveLocked ? ShellAmber : ShellAccent, 88), 2, 0);

        var buttonText = variant.IsVariantLocked ? "Unlock" : "Lock";
        var lockButton = CreateActionButton(buttonText, 84, variant.IsVariantLocked ? ShellAmber : ShellBorderStrong);
        lockButton.Height = 32;
        lockButton.Margin = new Padding(8, 6, 0, 6);
        lockButton.Enabled = !inheritedLock;
        _toolTip.SetToolTip(lockButton, inheritedLock
            ? realmLocked ? "Disabled because the parent realm is locked" : "Disabled because this desktop layout is globally locked"
            : variant.IsVariantLocked ? "Unlock this layout variant" : "Lock this layout variant");
        lockButton.Click += (_, _) => SafeAction(variant.IsVariantLocked ? "Unlock layout variant" : "Lock layout variant", () =>
        {
            if (variant.IsVariantLocked)
            {
                _switchService.UnlockIconLayoutVariant(desktopLayout.DesktopId, variant.DisplayTopologyKey);
            }
            else
            {
                _switchService.LockIconLayoutVariant(desktopLayout.DesktopId, variant.DisplayTopologyKey);
            }

            RefreshIconLayoutTree();
        });
        row.Controls.Add(lockButton, 3, 0);
        rowPanel.Controls.Add(row);
        return rowPanel;
    }

    private void ConfirmAndDeleteIconLayoutVariant(IconLayoutEntrySnapshot desktopLayout, IconLayoutVariantSnapshot variant)
    {
        if (!variant.HasSavedLayout)
        {
            throw new InvalidOperationException("Cannot delete an icon layout variant that has not been saved yet.");
        }

        var lockedWarning = variant.EffectiveLocked
            ? Environment.NewLine + Environment.NewLine + "This variant is currently locked. Deleting it will remove the saved layout variant and its variant lock entry."
            : string.Empty;

        var result = MessageBox.Show(
            $"Delete this icon layout variant?{Environment.NewLine}{Environment.NewLine}" +
            $"Layout: {desktopLayout.DesktopName}{Environment.NewLine}" +
            $"Variant: {variant.Summary}{Environment.NewLine}" +
            $"Displays: {BuildVariantDisplaySummary(variant)}{Environment.NewLine}" +
            $"Icons: {variant.IconCount}{lockedWarning}{Environment.NewLine}{Environment.NewLine}" +
            "This only deletes the saved layout variant file entry. It does not delete Desktop icons or files.",
            "DeskRealm — delete layout variant",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);

        if (result != DialogResult.Yes)
        {
            _logger.Info("Icon layout variant deletion cancelled from UI.");
            return;
        }

        _switchService.DeleteIconLayoutVariant(desktopLayout.DesktopId, variant.DisplayTopologyKey);
        RefreshLockState();
        RefreshIconLayoutTree();
        RefreshStatus();
    }

    private static string BuildVariantDisplaySummary(IconLayoutVariantSnapshot variant)
    {
        if (variant.Displays.Count == 0)
        {
            return ShortTopologyKey(variant.DisplayTopologyKey);
        }

        var displays = variant.Displays
            .Select((display, index) =>
            {
                var label = FormatDisplayLabel(display.DeviceName, index);
                var size = display.WorkingWidth > 0 && display.WorkingHeight > 0
                    ? $"{display.WorkingWidth}x{display.WorkingHeight}"
                    : "working area unknown";
                return $"{label}: {size}{(display.Primary ? " ✅" : string.Empty)}";
            });

        var scaleSummary = string.Join("/", variant.Displays
            .Select(display => display.ScalePercent)
            .Where(scale => scale > 0)
            .Distinct()
            .OrderBy(scale => scale)
            .Select(scale => scale + "%"));

        return string.IsNullOrWhiteSpace(scaleSummary)
            ? string.Join(" · ", displays)
            : string.Join(" · ", displays) + $" · scale {scaleSummary}";
    }

    private static string FormatDisplayLabel(string deviceName, int index)
    {
        var normalized = (deviceName ?? string.Empty).Replace(@"\\.\", string.Empty).Trim();
        if (normalized.StartsWith("DISPLAY", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = normalized["DISPLAY".Length..];
            return int.TryParse(suffix, out var displayNumber) ? $"Display {displayNumber}" : normalized;
        }

        return string.IsNullOrWhiteSpace(normalized) ? $"Display {index + 1}" : normalized;
    }

    private static string ShortTopologyKey(string topologyKey)
    {
        if (string.IsNullOrWhiteSpace(topologyKey))
        {
            return "unknown topology";
        }

        return topologyKey.Length <= 18 ? topologyKey : topologyKey[..18] + "…";
    }

    private static Control CreatePill(string text, Color accentColor, int width)
    {
        return new PillLabel
        {
            Text = text,
            Width = width,
            Height = 24,
            FillColor = Color.FromArgb(8, 27, 39),
            BorderColor = Color.FromArgb(Math.Max(0, accentColor.R - 40), Math.Max(0, accentColor.G - 70), Math.Max(0, accentColor.B - 70)),
            TextColor = accentColor,
            Font = CreateUiFont(8.5F),
            Margin = new Padding(8, 7, 4, 7)
        };
    }

    private void ConfirmAndSaveIconLayoutNow()
    {
        var locked = _switchService.IsCurrentLayoutOrRealmLocked();
        if (locked)
        {
            var result = MessageBox.Show(
                "This layout or its realm is locked. A manual save will replace the protected positions with the current Desktop state.\n\nOverwrite the locked layout?",
                "DeskRealm — locked layout",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning,
                MessageBoxDefaultButton.Button2);

            if (result != DialogResult.Yes)
            {
                _logger.Info("Locked icon layout manual overwrite cancelled from UI.");
                return;
            }
        }

        _switchService.SaveIconLayoutNow(overwriteLockedLayout: locked);
        RefreshLockState();
        RefreshIconLayoutTree();
    }

    private void AssignInitialDesktop()
    {
        if (_desktopCombo.SelectedItem is not DesktopChoice choice)
        {
            throw new InvalidOperationException("Select a target virtual desktop before associating the initial Desktop.");
        }

        _switchService.ImportOriginalDesktopToVirtualDesktop(choice.Desktop.Id, linkOriginalDesktop: true, saveLayout: _saveInitialLayoutCheck.Checked);
        _firstRunPanel.Visible = false;
        _firstRunStateLabel.Text = "Initial Desktop associated.";
        _firstRunCompleted();
        RefreshAll();
        MessageBox.Show(
            "Initial Desktop associated without moving files.",
            "DeskRealm — first run",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void SkipInitialDesktopImportWithShortcuts()
    {
        var count = _switchService.SkipInitialDesktopImportAndCreateOriginalDesktopShortcuts();
        _firstRunPanel.Visible = false;
        _firstRunStateLabel.Text = $"Import skipped. Shortcuts created: {count}.";
        _firstRunCompleted();
        RefreshAll();
        MessageBox.Show(
            $"Association skipped. DeskRealm created {count} shortcut(s) to the original Desktop inside managed realms.",
            "DeskRealm — first run",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ConfigureHotkeyCaptureBox(int desktopNumber, TextBox box)
    {
        box.Enter += (_, _) => BeginHotkeyCapture(desktopNumber, box);
        box.MouseDown += (_, _) =>
        {
            if (!box.Focused)
            {
                box.Focus();
            }

            BeginHotkeyCapture(desktopNumber, box);
        };
        box.PreviewKeyDown += (_, e) => e.IsInputKey = true;
        box.KeyDown += (_, e) => HandleHotkeyCapture(desktopNumber, box, e);
        box.KeyUp += (_, e) => HandleHotkeyCaptureKeyUp(desktopNumber, box, e);
        box.KeyPress += (_, e) => e.Handled = true;
        _toolTip.SetToolTip(box, "Click, hold one or two modifiers, then press one main key. Releasing modifiers before a main key cancels capture.");
    }

    private void BeginHotkeyCapture(int desktopNumber, TextBox box)
    {
        _capturingHotkeyDesktopNumber = desktopNumber;
        _capturingHotkeyBox = box;
        _capturingHotkeyOriginalText = box.Text;
        box.Text = "Press modifiers + main key…";
        box.SelectionStart = 0;
        box.SelectionLength = box.TextLength;
    }

    private void CompleteHotkeyCapture()
    {
        _capturingHotkeyDesktopNumber = null;
        _capturingHotkeyBox = null;
        _capturingHotkeyOriginalText = string.Empty;
    }

    private void CancelHotkeyCapture(TextBox box)
    {
        box.Text = _capturingHotkeyOriginalText;
        box.SelectionStart = box.TextLength;
        CompleteHotkeyCapture();
    }

    private void HandleHotkeyCapture(int desktopNumber, TextBox box, KeyEventArgs e)
    {
        if (_capturingHotkeyDesktopNumber != desktopNumber || !ReferenceEquals(_capturingHotkeyBox, box))
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (e.KeyCode == Keys.Escape)
        {
            CancelHotkeyCapture(box);
            return;
        }

        if ((e.KeyCode == Keys.Back || e.KeyCode == Keys.Delete) && ReadActiveModifierFlags() == 0)
        {
            box.Text = string.Empty;
            CompleteHotkeyCapture();
            return;
        }

        var modifiers = ReadActiveModifierFlags();
        if (IsModifierOnlyKey(e.KeyCode))
        {
            if (HotkeyParser.CountModifiers(modifiers) > 2)
            {
                box.Text = "Use one or two modifiers + key…";
            }
            else
            {
                box.Text = modifiers == 0
                    ? "Press modifiers + main key…"
                    : HotkeyParser.FormatHotkeyText(modifiers, 0).Replace("+None", "+…", StringComparison.OrdinalIgnoreCase);
            }

            box.SelectionStart = box.TextLength;
            return;
        }

        if (modifiers == 0)
        {
            box.Text = "Add Win/Ctrl/Alt/Shift first…";
            box.SelectionStart = box.TextLength;
            return;
        }

        if (HotkeyParser.CountModifiers(modifiers) > 2)
        {
            box.Text = "Use one or two modifiers + key…";
            box.SelectionStart = box.TextLength;
            return;
        }

        var captured = HotkeyParser.FormatHotkeyText(modifiers, (uint)e.KeyCode);
        var binding = HotkeyParser.Parse(desktopNumber, captured);
        box.Text = binding.Text;
        box.SelectionStart = box.TextLength;
        CompleteHotkeyCapture();
    }

    private void HandleHotkeyCaptureKeyUp(int desktopNumber, TextBox box, KeyEventArgs e)
    {
        if (_capturingHotkeyDesktopNumber != desktopNumber || !ReferenceEquals(_capturingHotkeyBox, box))
        {
            return;
        }

        e.SuppressKeyPress = true;
        e.Handled = true;

        if (IsModifierOnlyKey(e.KeyCode) && ReadActiveModifierFlags() == 0)
        {
            CancelHotkeyCapture(box);
        }
    }

    private static bool IsModifierOnlyKey(Keys key)
    {
        return key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin or Keys.Control or Keys.Shift or Keys.Alt;
    }

    private static uint ReadActiveModifierFlags()
    {
        uint modifiers = 0;
        if (IsVirtualKeyDown(VkLWin) || IsVirtualKeyDown(VkRWin))
        {
            modifiers |= HotkeyParser.ModWin;
        }

        if (IsVirtualKeyDown(VkControl))
        {
            modifiers |= HotkeyParser.ModControl;
        }

        if (IsVirtualKeyDown(VkAlt))
        {
            modifiers |= HotkeyParser.ModAlt;
        }

        if (IsVirtualKeyDown(VkShift))
        {
            modifiers |= HotkeyParser.ModShift;
        }

        return modifiers;
    }

    private static bool IsVirtualKeyDown(int virtualKey)
    {
        return (GetKeyState(virtualKey) & unchecked((short)0x8000)) != 0;
    }

    private void SaveHotkeysFromUi()
    {
        var next = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _hotkeyTextBoxes.OrderBy(p => p.Key))
        {
            var value = pair.Value.Text.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            var binding = HotkeyParser.Parse(pair.Key, value);
            if (!seen.Add(binding.Text))
            {
                throw new InvalidOperationException($"Duplicate hotkey: {binding.Text}.");
            }

            next[pair.Key.ToString()] = binding.Text;
        }

        if (_hotkeysEnabledCheck.Checked && next.Count == 0)
        {
            throw new InvalidOperationException("Hotkeys cannot be enabled with an empty binding list.");
        }

        _switchService.Config.DesktopHotkeysEnabled = _hotkeysEnabledCheck.Checked;
        _switchService.Config.DesktopHotkeys = next;
        _configService.Save(_switchService.Config);
        _reloadHotkeys();
        RefreshAll();
    }

    private void ResetHotkeysToDefaults()
    {
        _switchService.Config.DesktopHotkeys = RealmConfig.CreateDefaultDesktopHotkeys();
        _switchService.Config.DesktopHotkeysEnabled = true;
        _configService.Save(_switchService.Config);
        _reloadHotkeys();
        RefreshAll();
    }

    private void ApplyOptions()
    {
        _switchService.SetEnabled(_enabledCheck.Checked);

        var startupEnabled = _startupService.IsEnabledForCurrentExecutable();
        if (_startWithWindowsCheck.Checked && !startupEnabled)
        {
            _startupService.Enable();
        }
        else if (!_startWithWindowsCheck.Checked && startupEnabled)
        {
            _startupService.Disable();
        }

        _switchService.Config.StartWithWindows = _startWithWindowsCheck.Checked;
        _configService.Save(_switchService.Config);
        RefreshAll();
    }

    private void RefreshDesktopChoices()
    {
        _desktopCombo.Items.Clear();
        var desktops = _switchService.GetVirtualDesktopsSnapshot();
        var current = _switchService.GetCurrentVirtualDesktopId();
        var selectedIndex = 0;
        foreach (var desktop in desktops.OrderBy(d => d.Number))
        {
            var index = _desktopCombo.Items.Add(new DesktopChoice(desktop));
            if (desktop.Id == current)
            {
                selectedIndex = index;
            }
        }

        if (_desktopCombo.Items.Count > 0)
        {
            _desktopCombo.SelectedIndex = selectedIndex;
        }
    }

    private void RefreshOptionsFromConfig()
    {
        _enabledCheck.Checked = _switchService.Config.Enabled;
        _startWithWindowsCheck.Checked = _startupService.IsEnabledForCurrentExecutable();
        _hotkeysEnabledCheck.Checked = _switchService.Config.DesktopHotkeysEnabled;
    }

    private void RefreshHotkeysFromConfig()
    {
        CompleteHotkeyCapture();

        foreach (var box in _hotkeyTextBoxes.Values)
        {
            box.Text = string.Empty;
        }

        foreach (var pair in _switchService.Config.DesktopHotkeys)
        {
            if (int.TryParse(pair.Key, out var desktopNumber) && _hotkeyTextBoxes.TryGetValue(desktopNumber, out var box))
            {
                box.Text = pair.Value;
            }
        }
    }

    private void RefreshLockState()
    {
        try
        {
            _lockStateLabel.Text = _switchService.GetCurrentLockStatusText() + Environment.NewLine +
                                   "Locked autosave protects existing icon positions and only merges newly detected icons once.";
        }
        catch (Exception ex)
        {
            _lockStateLabel.Text = "Lock state unavailable: " + ex.Message;
        }
    }

    public void RefreshStatus()
    {
        var status = _switchService.GetStatus();
        _statusBox.Text =
            $"Realm automation     : {(status.Enabled ? "enabled" : "paused")}{Environment.NewLine}" +
            $"Current virtual desk : {status.CurrentDesktopName}{Environment.NewLine}" +
            $"Current GUID         : {status.CurrentDesktopGuid}{Environment.NewLine}" +
            $"Realm path           : {status.CurrentRealmPath}{Environment.NewLine}" +
            $"Known Desktop path   : {status.KnownFolderDesktopPath}{Environment.NewLine}" +
            $"Last switch          : {status.LastSwitchAt}{Environment.NewLine}" +
            $"Last message         : {status.LastMessage}{Environment.NewLine}" +
            $"Sync names           : {_switchService.Config.SyncRealmNamesWithVirtualDesktopNames}{Environment.NewLine}" +
            $"Icon layout persist  : {_switchService.Config.IconLayoutPersistenceEnabled}{Environment.NewLine}" +
            $"Icon layout locked   : {ReadBoolStatus(() => _switchService.IsCurrentLayoutLocked())}{Environment.NewLine}" +
            $"Icon variant locked  : {ReadBoolStatus(() => _switchService.IsCurrentLayoutVariantLocked())}{Environment.NewLine}" +
            $"Realm layout locked  : {ReadBoolStatus(() => _switchService.IsCurrentRealmLocked())}{Environment.NewLine}" +
            $"Icon settle delay    : {_switchService.Config.IconLayoutSettleDelayMs} ms{Environment.NewLine}" +
            $"Desktop hotkeys      : {_switchService.Config.DesktopHotkeysEnabled}{Environment.NewLine}" +
            $"Hotkey bindings      : {string.Join(", ", _switchService.Config.DesktopHotkeys.OrderBy(p => p.Key).Select(p => $"#{p.Key}={p.Value}"))}{Environment.NewLine}" +
            $"Start with Windows   : {_switchService.Config.StartWithWindows}{Environment.NewLine}" +
            $"First-run completed  : {_switchService.Config.InitialDesktopImportPromptCompleted}{Environment.NewLine}" +
            $"Locked layouts       : {_switchService.Config.LockedIconLayouts.Count}{Environment.NewLine}" +
            $"Locked variants      : {_switchService.Config.LockedIconLayoutVariants.Count}{Environment.NewLine}" +
            $"Locked realms        : {_switchService.Config.LockedRealms.Count}{Environment.NewLine}" +
            $"{Environment.NewLine}" +
            $"Assignments          :{Environment.NewLine}{status.Assignments}{Environment.NewLine}" +
            $"{Environment.NewLine}" +
            $"Config               : {AppPaths.ConfigPath}{Environment.NewLine}" +
            $"Logs                 : {AppPaths.LogFilePath}{Environment.NewLine}" +
            $"Icon layouts         : {IconLayoutPersistenceService.LayoutRoot}{Environment.NewLine}";
    }

    private static string ReadBoolStatus(Func<bool> read)
    {
        try
        {
            return read().ToString();
        }
        catch (Exception ex)
        {
            return "unavailable: " + ex.Message;
        }
    }

    private void SafeAction(string name, Action action)
    {
        try
        {
            action();
            RefreshStatus();
        }
        catch (Exception ex)
        {
            _logger.Error($"UI action failed: {name}", ex);
            MessageBox.Show(ex.Message, "DeskRealm — error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static void OpenPath(string path)
    {
        if (File.Exists(path))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            return;
        }

        if (Directory.Exists(path))
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return;
        }

        throw new FileNotFoundException($"Path not found: {path}", path);
    }

    private void ApplyTheme(Control root)
    {
        foreach (Control control in root.Controls)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.BackColor = Color.FromArgb(4, 19, 29);
                    textBox.ForeColor = ShellText;
                    break;
                case ComboBox comboBox:
                    comboBox.BackColor = Color.FromArgb(4, 19, 29);
                    comboBox.ForeColor = ShellText;
                    break;
                case CheckBox checkBox:
                    checkBox.BackColor = control.Parent?.BackColor ?? ShellBackground;
                    checkBox.ForeColor = ShellText;
                    break;
                case Label label when label.ForeColor == SystemColors.ControlText:
                    label.ForeColor = ShellText;
                    break;
            }

            if (control.HasChildren)
            {
                ApplyTheme(control);
            }
        }
    }

    private sealed class DesktopChoice
    {
        public DesktopChoice(VirtualDesktopInfo desktop) => Desktop = desktop;
        public VirtualDesktopInfo Desktop { get; }
        public override string ToString() => $"#{Desktop.Number} — {Desktop.Name}";
    }
}
