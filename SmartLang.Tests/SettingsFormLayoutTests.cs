using System.Drawing;
using System.Windows.Forms;

namespace SmartLang.Tests;

public sealed class SettingsFormLayoutTests {
    [Fact]
    public void FooterShowsVersionAndBothButtons() {
        using var form = new SettingsForm(SystemIcons.Application, "0.7.1");
        form.CreateControl();
        form.PerformLayout();

        var version = FindControl(form, "SmartLang v0.7.1");
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
}
