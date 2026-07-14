using System.Drawing;
using System.Windows.Forms;

namespace SmartLang.Tests;

public sealed class SettingsFormLayoutTests {
    [Fact]
    public void FooterShowsVersionAndBothButtons() {
        using var form = new SettingsForm(SystemIcons.Application, "0.8.1");
        form.CreateControl();
        form.PerformLayout();

        var version = FindControl(form, "SmartLang v0.8.1");
        var save = FindControl(form, "Save");
        var cancel = FindControl(form, "Cancel");

        AssertVisibleSize(version);
        AssertVisibleSize(save);
        AssertVisibleSize(cancel);
        AssertContainedByParent(version);
        AssertContainedByParent(save);
        AssertContainedByParent(cancel);
        Assert.Same(version.Parent, save.Parent?.Parent);
        Assert.Same(version.Parent, cancel.Parent?.Parent);
    }

    [Fact]
    public void RecentLanguageModeRelabelsPrimaryShortcutAndDisablesAllLayoutsShortcut() {
        using var form = new SettingsForm(SystemIcons.Application, "0.8.1");
        form.LoadSettings(
            new AppSettings {
                PrimaryLanguageTag = "en-US",
                SecondaryLanguageTag = "fr-FR",
                SwitchingMode = SwitchingMode.RecentLanguages,
                PrimaryShortcut = ShortcutKind.CtrlShift,
                AllLayoutsShortcut = ShortcutKind.WinSpace
            },
            [new LanguageOption("en-US", "English"), new LanguageOption("fr-FR", "French")],
            validationMessage: null);
        form.CreateControl();
        form.PerformLayout();

        var primaryShortcutLabel = FindControl(form, "Switch languages:");
        var allLayoutsShortcutLabel = FindControl(form, "Cycle all layouts:");
        var allLayoutsShortcut = GetRowControl(allLayoutsShortcutLabel, column: 1);

        Assert.True(primaryShortcutLabel.Enabled);
        Assert.False(allLayoutsShortcutLabel.Enabled);
        Assert.False(allLayoutsShortcut.Enabled);
    }

    [Fact]
    public void AdministratorSupportButtonsAreVisibleAndInvokeHandlers() {
        using var form = new SettingsForm(SystemIcons.Application, "0.9.0");
        var restartRequested = false;
        var shutdownRequested = false;
        form.SetRestartAdministratorSupportHandler(() => restartRequested = true);
        form.SetShutdownAdministratorSupportHandler(() => shutdownRequested = true);
        form.Show();
        form.PerformLayout();

        var restart = Assert.IsType<Button>(FindControl(form, "Restart administrator support"));
        var shutdown = Assert.IsType<Button>(FindControl(form, "Shutdown administrator support"));

        AssertVisibleSize(restart);
        AssertVisibleSize(shutdown);
        AssertContainedByParent(restart);
        AssertContainedByParent(shutdown);
        Assert.Same(restart.Parent, shutdown.Parent);

        restart.PerformClick();
        shutdown.PerformClick();

        Assert.True(restartRequested);
        Assert.True(shutdownRequested);
    }

    static Control FindControl(Control root, string text) {
        foreach(Control control in root.Controls) {
            if(control.Text == text) {
                return control;
            }

            var match = FindControlOrDefault(control, text);
            if(match is not null) {
                return match;
            }
        }

        throw new InvalidOperationException($"Control '{text}' was not found.");
    }

    static Control? FindControlOrDefault(Control root, string text) {
        foreach(Control control in root.Controls) {
            if(control.Text == text) {
                return control;
            }

            var match = FindControlOrDefault(control, text);
            if(match is not null) {
                return match;
            }
        }

        return null;
    }

    static void AssertVisibleSize(Control control) {
        Assert.True(control.Width > 0, $"{control.Text} has no width.");
        Assert.True(control.Height > 0, $"{control.Text} has no height.");
    }

    static void AssertContainedByParent(Control control) {
        Assert.NotNull(control.Parent);
        Assert.True(control.Parent.ClientRectangle.Contains(control.Bounds), $"{control.Text} is clipped.");
    }

    static Control GetRowControl(Control rowControl, int column) {
        var layout = Assert.IsType<TableLayoutPanel>(rowControl.Parent);
        var position = layout.GetPositionFromControl(rowControl);
        return layout.GetControlFromPosition(column, position.Row)
            ?? throw new InvalidOperationException($"Control at column {column}, row {position.Row} was not found.");
    }
}
