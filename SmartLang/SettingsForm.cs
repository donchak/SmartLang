namespace SmartLang;

public sealed class SettingsForm: Form {
    readonly ComboBox primaryLanguage = new();
    readonly ComboBox secondaryLanguage = new();
    readonly ComboBox switchingMode = new();
    readonly ComboBox primaryShortcut = new();
    readonly ComboBox allLayoutsShortcut = new();
    readonly Label primaryShortcutLabel = new();
    readonly Label allLayoutsShortcutLabel = new();
    readonly CheckBox startWithWindows = new();
    readonly CheckBox administratorAppSupport = new();
    readonly Label administratorStatus = new();
    readonly Label status = new();
    IReadOnlyList<LanguageOption> languages = [];
    Func<AppSettings, string?>? saveRequested;
    Action? restartAdministratorSupport;
    Action? shutdownAdministratorSupport;
    bool allowClose;

    public SettingsForm(Icon applicationIcon, string applicationVersion) {
        Text = "SmartLang Settings";
        Icon = (Icon)applicationIcon.Clone();
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = true;
        ClientSize = new Size(560, 466);
        MinimumSize = Size;
        MaximumSize = Size;

        ConfigureCombo(primaryLanguage);
        ConfigureCombo(secondaryLanguage);
        ConfigureCombo(switchingMode);
        ConfigureCombo(primaryShortcut);
        ConfigureCombo(allLayoutsShortcut);

        var switchingModeOptions = new[]
        {
            new SwitchingModeOption(SwitchingMode.PrimaryLanguages, "Primary languages"),
            new SwitchingModeOption(SwitchingMode.RecentLanguages, "Recently used languages")
        };
        var primaryShortcutOptions = new[]
        {
            new ShortcutOption(ShortcutKind.CtrlShift, "Ctrl + Shift"),
            new ShortcutOption(ShortcutKind.AltShift, "Alt + Shift"),
            new ShortcutOption(ShortcutKind.WinSpace, "Win + Space")
        };
        var allLayoutsShortcutOptions = new[]
        {
            new ShortcutOption(ShortcutKind.None, "None"),
            new ShortcutOption(ShortcutKind.CtrlShift, "Ctrl + Shift"),
            new ShortcutOption(ShortcutKind.AltShift, "Alt + Shift"),
            new ShortcutOption(ShortcutKind.WinSpace, "Win + Space")
        };
        switchingMode.DataSource = switchingModeOptions;
        switchingMode.DisplayMember = nameof(SwitchingModeOption.DisplayName);
        primaryShortcut.DataSource = primaryShortcutOptions;
        primaryShortcut.DisplayMember = nameof(ShortcutOption.DisplayName);
        allLayoutsShortcut.DataSource = allLayoutsShortcutOptions;
        allLayoutsShortcut.DisplayMember = nameof(ShortcutOption.DisplayName);
        switchingMode.SelectedIndexChanged += (_, _) => ApplySwitchingModeUi();

        startWithWindows.Text = "Start SmartLang when I sign in to Windows";
        startWithWindows.AutoSize = true;
        administratorAppSupport.Text = "Support applications running as administrator";
        administratorAppSupport.AutoSize = true;

        administratorStatus.AutoSize = false;
        administratorStatus.TextAlign = ContentAlignment.MiddleLeft;
        administratorStatus.Dock = DockStyle.Fill;

        status.AutoSize = false;
        status.ForeColor = Color.Firebrick;
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.Dock = DockStyle.Fill;

        var restartAdministratorSupportButton = new Button {
            Text = "Restart administrator support",
            AutoSize = true
        };
        restartAdministratorSupportButton.Click += (_, _) =>
            restartAdministratorSupport?.Invoke();

        var shutdownAdministratorSupportButton = new Button {
            Text = "Shutdown administrator support",
            AutoSize = true
        };
        shutdownAdministratorSupportButton.Click += (_, _) =>
            shutdownAdministratorSupport?.Invoke();

        var administratorSupportButtons = new FlowLayoutPanel {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false
        };
        administratorSupportButtons.Controls.Add(restartAdministratorSupportButton);
        administratorSupportButtons.Controls.Add(shutdownAdministratorSupportButton);

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

        var version = new Label {
            Text = $"SmartLang v{applicationVersion}",
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = SystemColors.GrayText
        };

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill,
            Padding = new Padding(16),
            ColumnCount = 2,
            RowCount = 10
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for(var row = 0; row < 7; row++) {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        }

        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        AddRow(layout, 0, "First primary language:", primaryLanguage);
        AddRow(layout, 1, "Second primary language:", secondaryLanguage);
        AddRow(layout, 2, "Switching mode:", switchingMode);
        AddRow(layout, 3, primaryShortcutLabel, "Switch primary languages:", primaryShortcut);
        AddRow(layout, 4, allLayoutsShortcutLabel, "Cycle all layouts:", allLayoutsShortcut);
        layout.Controls.Add(startWithWindows, 0, 5);
        layout.SetColumnSpan(startWithWindows, 2);
        layout.Controls.Add(administratorAppSupport, 0, 6);
        layout.SetColumnSpan(administratorAppSupport, 2);
        layout.Controls.Add(administratorStatus, 0, 7);
        layout.Controls.Add(administratorSupportButtons, 1, 7);
        layout.Controls.Add(status, 0, 8);
        layout.SetColumnSpan(status, 2);
        layout.Controls.Add(version, 0, 9);
        layout.Controls.Add(buttons, 1, 9);

        Controls.Add(layout);
        FormClosing += HandleFormClosing;
    }

    public void SetSaveHandler(Func<AppSettings, string?> saveRequested) {
        this.saveRequested = saveRequested;
    }

    public void SetRestartAdministratorSupportHandler(Action restartRequested) {
        restartAdministratorSupport = restartRequested;
    }

    public void SetShutdownAdministratorSupportHandler(Action shutdownRequested) {
        shutdownAdministratorSupport = shutdownRequested;
    }

    public void LoadSettings(
        AppSettings settings,
        IReadOnlyList<LanguageOption> languages,
        string? validationMessage) {
        this.languages = languages;

        primaryLanguage.DataSource = languages.ToArray();
        primaryLanguage.DisplayMember = nameof(LanguageOption.DisplayName);
        secondaryLanguage.DataSource = languages.ToArray();
        secondaryLanguage.DisplayMember = nameof(LanguageOption.DisplayName);

        SelectLanguage(primaryLanguage, settings.PrimaryLanguageTag, fallbackIndex: 0);
        SelectLanguage(secondaryLanguage, settings.SecondaryLanguageTag, fallbackIndex: languages.Count > 1 ? 1 : 0);
        SelectSwitchingMode(switchingMode, settings.SwitchingMode);
        SelectShortcut(primaryShortcut, settings.PrimaryShortcut);
        SelectShortcut(allLayoutsShortcut, settings.AllLayoutsShortcut);
        startWithWindows.Checked = settings.StartWithWindows;
        administratorAppSupport.Checked = settings.AdministratorAppSupport;
        status.Text = validationMessage ?? string.Empty;
        ApplySwitchingModeUi();
    }

    public void SetAdministratorSupportStatus(string status, bool isError) {
        administratorStatus.Text = status;
        administratorStatus.ForeColor = isError ? Color.Firebrick : SystemColors.ControlText;
    }

    public void AllowClose() {
        allowClose = true;
    }

    void Save() {
        if(primaryLanguage.SelectedItem is not LanguageOption primary ||
            secondaryLanguage.SelectedItem is not LanguageOption secondary ||
            this.switchingMode.SelectedItem is not SwitchingModeOption switchingMode ||
            this.primaryShortcut.SelectedItem is not ShortcutOption primaryShortcut ||
            this.allLayoutsShortcut.SelectedItem is not ShortcutOption allLayoutsShortcut) {
            status.Text = "Complete all settings before saving.";
            return;
        }

        var settings = new AppSettings {
            PrimaryLanguageTag = primary.LanguageTag,
            SecondaryLanguageTag = secondary.LanguageTag,
            SwitchingMode = switchingMode.Mode,
            PrimaryShortcut = primaryShortcut.Kind,
            AllLayoutsShortcut = allLayoutsShortcut.Kind,
            StartWithWindows = startWithWindows.Checked,
            AdministratorAppSupport = administratorAppSupport.Checked
        };

        var validationMessage = SettingsValidator.Validate(settings, languages);
        if(validationMessage is not null) {
            status.Text = validationMessage;
            return;
        }

        var saveError = saveRequested?.Invoke(settings);
        if(saveError is not null) {
            status.Text = saveError;
            return;
        }

        Hide();
    }

    void HandleFormClosing(object? sender, FormClosingEventArgs eventArgs) {
        if(allowClose) {
            return;
        }

        eventArgs.Cancel = true;
        Hide();
    }

    static void ConfigureCombo(ComboBox comboBox) {
        comboBox.DropDownStyle = ComboBoxStyle.DropDownList;
        comboBox.Dock = DockStyle.Fill;
    }

    static void AddRow(
        TableLayoutPanel layout,
        int row,
        string labelText,
        Control control) {
        AddRow(layout, row, new Label(), labelText, control);
    }

    static void AddRow(
        TableLayoutPanel layout,
        int row,
        Label label,
        string labelText,
        Control control) {
        label.Text = labelText;
        label.Dock = DockStyle.Fill;
        label.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(label, 0, row);
        layout.Controls.Add(control, 1, row);
    }

    void ApplySwitchingModeUi() {
        var recentMode = switchingMode.SelectedItem is SwitchingModeOption option &&
            option.Mode == SwitchingMode.RecentLanguages;

        primaryShortcutLabel.Text = recentMode ? "Switch languages:" : "Switch primary languages:";
        allLayoutsShortcut.Enabled = !recentMode;
        allLayoutsShortcutLabel.Enabled = !recentMode;
    }

    static void SelectLanguage(
        ComboBox comboBox,
        string languageTag,
        int fallbackIndex) {
        for(var index = 0; index < comboBox.Items.Count; index++) {
            if(comboBox.Items[index] is LanguageOption language &&
                string.Equals(language.LanguageTag, languageTag, StringComparison.OrdinalIgnoreCase)) {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        comboBox.SelectedIndex = comboBox.Items.Count == 0 ? -1 : Math.Min(fallbackIndex, comboBox.Items.Count - 1);
    }

    static void SelectShortcut(ComboBox comboBox, ShortcutKind shortcut) {
        for(var index = 0; index < comboBox.Items.Count; index++) {
            if(comboBox.Items[index] is ShortcutOption option &&
                option.Kind == shortcut) {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    static void SelectSwitchingMode(ComboBox comboBox, SwitchingMode mode) {
        for(var index = 0; index < comboBox.Items.Count; index++) {
            if(comboBox.Items[index] is SwitchingModeOption option &&
                option.Mode == mode) {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    sealed record SwitchingModeOption(SwitchingMode Mode, string DisplayName);

    sealed record ShortcutOption(ShortcutKind Kind, string DisplayName);
}
