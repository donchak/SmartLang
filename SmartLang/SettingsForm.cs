namespace SmartLang;

public sealed class SettingsForm: Form {
    private readonly ComboBox _primaryLanguage = new();
    private readonly ComboBox _secondaryLanguage = new();
    private readonly ComboBox _primaryShortcut = new();
    private readonly ComboBox _allLayoutsShortcut = new();
    private readonly CheckBox _startWithWindows = new();
    private readonly CheckBox _administratorAppSupport = new();
    private readonly Label _administratorStatus = new();
    private readonly Label _status = new();
    private IReadOnlyList<LanguageOption> _languages = [];
    private Func<AppSettings, string?>? _saveRequested;
    private Action? _restartAdministratorSupport;
    private bool _allowClose;

    public SettingsForm(Icon applicationIcon) {
        Text = "SmartLang Settings";
        Icon = (Icon)applicationIcon.Clone();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 390);
        MinimumSize = Size;
        MaximumSize = Size;

        ConfigureCombo(_primaryLanguage);
        ConfigureCombo(_secondaryLanguage);
        ConfigureCombo(_primaryShortcut);
        ConfigureCombo(_allLayoutsShortcut);

        var primaryShortcutOptions = new[]
        {
            new ShortcutOption(ShortcutKind.CtrlShift, "Ctrl + Shift"),
            new ShortcutOption(ShortcutKind.WinSpace, "Win + Space")
        };
        var allLayoutsShortcutOptions = new[]
        {
            new ShortcutOption(ShortcutKind.None, "None"),
            new ShortcutOption(ShortcutKind.CtrlShift, "Ctrl + Shift"),
            new ShortcutOption(ShortcutKind.WinSpace, "Win + Space")
        };
        _primaryShortcut.DataSource = primaryShortcutOptions;
        _primaryShortcut.DisplayMember = nameof(ShortcutOption.DisplayName);
        _allLayoutsShortcut.DataSource = allLayoutsShortcutOptions;
        _allLayoutsShortcut.DisplayMember = nameof(ShortcutOption.DisplayName);

        _startWithWindows.Text = "Start SmartLang when I sign in to Windows";
        _startWithWindows.AutoSize = true;
        _administratorAppSupport.Text = "Support applications running as administrator";
        _administratorAppSupport.AutoSize = true;

        _administratorStatus.AutoSize = false;
        _administratorStatus.TextAlign = ContentAlignment.MiddleLeft;
        _administratorStatus.Dock = DockStyle.Fill;

        _status.AutoSize = false;
        _status.ForeColor = Color.Firebrick;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.Dock = DockStyle.Fill;

        var restartAdministratorSupportButton = new Button {
            Text = "Restart administrator support",
            AutoSize = true
        };
        restartAdministratorSupportButton.Click += (_, _) =>
            _restartAdministratorSupport?.Invoke();

        var saveButton = new Button {
            Text = "Save",
            AutoSize = true
        };
        saveButton.Click += (_, _) => Save();

        var cancelButton = new Button {
            Text = "Cancel",
            AutoSize = true,
            DialogResult = DialogResult.Cancel
        };
        cancelButton.Click += (_, _) => Hide();

        AcceptButton = saveButton;
        CancelButton = cancelButton;

        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true
        };
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(cancelButton);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 9
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for(var row = 0; row < 7; row++) {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        AddRow(layout, 0, "First primary language:", _primaryLanguage);
        AddRow(layout, 1, "Second primary language:", _secondaryLanguage);
        AddRow(layout, 2, "Switch primary languages:", _primaryShortcut);
        AddRow(layout, 3, "Cycle all layouts:", _allLayoutsShortcut);
        layout.Controls.Add(_startWithWindows, 0, 4);
        layout.SetColumnSpan(_startWithWindows, 2);
        layout.Controls.Add(_administratorAppSupport, 0, 5);
        layout.SetColumnSpan(_administratorAppSupport, 2);
        layout.Controls.Add(_administratorStatus, 0, 6);
        layout.Controls.Add(restartAdministratorSupportButton, 1, 6);
        layout.Controls.Add(_status, 0, 7);
        layout.SetColumnSpan(_status, 2);
        layout.Controls.Add(buttons, 0, 8);
        layout.SetColumnSpan(buttons, 2);

        Controls.Add(layout);
        FormClosing += HandleFormClosing;
    }

    public void SetSaveHandler(Func<AppSettings, string?> saveRequested) {
        _saveRequested = saveRequested;
    }

    public void SetRestartAdministratorSupportHandler(Action restartRequested) {
        _restartAdministratorSupport = restartRequested;
    }

    public void LoadSettings(
        AppSettings settings,
        IReadOnlyList<LanguageOption> languages,
        string? validationMessage) {
        _languages = languages;

        _primaryLanguage.DataSource = languages.ToArray();
        _primaryLanguage.DisplayMember = nameof(LanguageOption.DisplayName);
        _secondaryLanguage.DataSource = languages.ToArray();
        _secondaryLanguage.DisplayMember = nameof(LanguageOption.DisplayName);

        SelectLanguage(_primaryLanguage, settings.PrimaryLanguageTag, fallbackIndex: 0);
        SelectLanguage(
            _secondaryLanguage,
            settings.SecondaryLanguageTag,
            fallbackIndex: languages.Count > 1 ? 1 : 0);
        SelectShortcut(_primaryShortcut, settings.PrimaryShortcut);
        SelectShortcut(_allLayoutsShortcut, settings.AllLayoutsShortcut);
        _startWithWindows.Checked = settings.StartWithWindows;
        _administratorAppSupport.Checked = settings.AdministratorAppSupport;
        _status.Text = validationMessage ?? string.Empty;
    }

    public void SetAdministratorSupportStatus(string status, bool isError) {
        _administratorStatus.Text = status;
        _administratorStatus.ForeColor = isError
            ? Color.Firebrick
            : SystemColors.ControlText;
    }

    public void AllowClose() {
        _allowClose = true;
    }

    private void Save() {
        if(_primaryLanguage.SelectedItem is not LanguageOption primary ||
            _secondaryLanguage.SelectedItem is not LanguageOption secondary ||
            _primaryShortcut.SelectedItem is not ShortcutOption primaryShortcut ||
            _allLayoutsShortcut.SelectedItem is not ShortcutOption allLayoutsShortcut) {
            _status.Text = "Complete all settings before saving.";
            return;
        }

        var settings = new AppSettings {
            PrimaryLanguageTag = primary.LanguageTag,
            SecondaryLanguageTag = secondary.LanguageTag,
            PrimaryShortcut = primaryShortcut.Kind,
            AllLayoutsShortcut = allLayoutsShortcut.Kind,
            StartWithWindows = _startWithWindows.Checked,
            AdministratorAppSupport = _administratorAppSupport.Checked
        };

        var validationMessage = SettingsValidator.Validate(settings, _languages);
        if(validationMessage is not null) {
            _status.Text = validationMessage;
            return;
        }

        var saveError = _saveRequested?.Invoke(settings);
        if(saveError is not null) {
            _status.Text = saveError;
            return;
        }

        Hide();
    }

    private void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs) {
        if(_allowClose) {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    private static void ConfigureCombo(ComboBox comboBox) {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Dock = DockStyle.Fill;
    }

    private static void AddRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Control control) {
        var label = new Label {
            Text = labelText,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    private static void SelectLanguage(
        ComboBox comboBox,
        string languageTag,
        int fallbackIndex) {
        for(var index = 0; index < comboBox.Items.Count; index++) {
            if(comboBox.Items[index] is LanguageOption language &&
                string.Equals(
                    language.LanguageTag,
                    languageTag,
                    StringComparison.OrdinalIgnoreCase)) {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count == 0
            ? -1
            : Math.Min(fallbackIndex, comboBox.Items.Count - 1);
    }

    private static void SelectShortcut(ComboBox comboBox, ShortcutKind shortcut) {
        for(var index = 0; index < comboBox.Items.Count; index++) {
            if(comboBox.Items[index] is ShortcutOption option &&
                option.Kind == shortcut) {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    private sealed record ShortcutOption(ShortcutKind Kind, string DisplayName);
}
