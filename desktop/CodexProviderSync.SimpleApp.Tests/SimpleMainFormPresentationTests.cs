using CodexProviderSync.SimpleApp;
using System.Reflection;
using static CodexProviderSync.SimpleApp.Tests.SimpleSwitcherTestData;

namespace CodexProviderSync.SimpleApp.Tests;

public sealed class SimpleMainFormPresentationTests
{
    [Fact]
    public void FormContainsOnlyTheApprovedActions()
    {
        using SimpleMainForm form = CreateForm();

        Assert.Equal("Codex Provider Switcher", form.Text);
        Assert.Equal("切换并同步", Field<Button>(form, "_executeButton").Text);
        Assert.Equal("刷新", Field<Button>(form, "_refreshButton").Text);
        Assert.Equal("复制结果", Field<Button>(form, "_copyButton").Text);
        Assert.Equal(ComboBoxStyle.DropDownList, Field<ComboBox>(form, "_providerCombo").DropDownStyle);
        Assert.True(Field<RichTextBox>(form, "_detailsBox").ReadOnly);
    }

    [Fact]
    public void FormDoesNotExposeOutOfScopeControls()
    {
        using SimpleMainForm form = CreateForm();
        string[] forbidden =
        [
            "auth.json",
            "API Key",
            "base_url",
            "恢复备份",
            "清理旧备份",
            "检查更新",
            "监控"
        ];
        string allText = string.Join(
            Environment.NewLine,
            Descendants(form).Select(control => control.Text));

        Assert.All(forbidden, value => Assert.DoesNotContain(value, allText));
    }

    [Fact]
    public void MinimumWindowSizeKeepsAllPrimaryControlsVisible()
    {
        using SimpleMainForm form = CreateForm();
        Assert.Equal(new Size(520, 380), form.MinimumSize);
        form.Show();
        form.Size = form.MinimumSize;
        PerformLayoutRecursively(form);

        Control[] primaryControls =
        [
            Field<ComboBox>(form, "_providerCombo"),
            Field<Button>(form, "_executeButton"),
            Field<Button>(form, "_refreshButton"),
            Field<RichTextBox>(form, "_detailsBox")
        ];
        Assert.All(primaryControls, control => Assert.True(control.Visible));
        Assert.All(primaryControls, control =>
        {
            Rectangle bounds = form.RectangleToClient(control.RectangleToScreen(control.ClientRectangle));
            Assert.True(form.ClientRectangle.Contains(bounds), $"{control.Name} lies outside the client area: {bounds}");
        });
        for (int left = 0; left < primaryControls.Length; left++)
        {
            for (int right = left + 1; right < primaryControls.Length; right++)
            {
                Rectangle leftBounds = form.RectangleToClient(
                    primaryControls[left].RectangleToScreen(primaryControls[left].ClientRectangle));
                Rectangle rightBounds = form.RectangleToClient(
                    primaryControls[right].RectangleToScreen(primaryControls[right].ClientRectangle));
                Assert.False(leftBounds.IntersectsWith(rightBounds));
            }
        }
    }

    private static SimpleMainForm CreateForm()
    {
        FakeSimpleProviderService service = new(Status(
            current: "openai",
            configured: ["openai", "custom"],
            sqliteSupported: true));
        SimpleSwitcherController controller = new(
            service,
            new FakeProcessProbe(),
            @"C:\fixture\.codex");
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            "codex-switcher-form-" + Guid.NewGuid().ToString("N"),
            "settings.json");
        return new SimpleMainForm(controller, new SimpleSettingsStore(settingsPath));
    }

    private static T Field<T>(object target, string name) where T : class =>
        Assert.IsType<T>(target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target));

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }

    private static void PerformLayoutRecursively(Control root)
    {
        root.PerformLayout();
        foreach (Control child in root.Controls)
        {
            PerformLayoutRecursively(child);
        }
    }
}
