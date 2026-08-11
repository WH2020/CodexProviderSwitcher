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

    [Fact]
    public void InitialSnapshotDisplaysSqliteAsLoading()
    {
        using SimpleMainForm form = CreateForm();

        Assert.Equal("读取中", Field<Label>(form, "_sqliteStatusValue").Text);
    }

    [Theory]
    [InlineData(true, "可用")]
    [InlineData(false, "不支持")]
    public async Task ExplicitSqliteSupportControlsDisplayedStatus(bool supported, string expected)
    {
        SimpleSwitcherController controller = Controller(Status(
            current: "openai",
            configured: ["openai"],
            sqliteSupported: supported));
        await controller.RefreshAsync();
        using SimpleMainForm form = Form(controller);

        Assert.Equal(supported, controller.Snapshot.SqliteSupported);
        Assert.Equal(expected, Field<Label>(form, "_sqliteStatusValue").Text);
    }

    [Fact]
    public async Task FirstFailedRefreshDisplaysSqliteAsUnknown()
    {
        SimpleSwitcherController controller = new(
            new ThrowingStatusProviderService(),
            new FakeProcessProbe(),
            @"C:\fixture\.codex");
        await Assert.ThrowsAsync<InvalidOperationException>(() => controller.RefreshAsync());
        using SimpleMainForm form = Form(controller);

        Assert.Null(controller.Snapshot.SqliteSupported);
        Assert.Equal("未知", Field<Label>(form, "_sqliteStatusValue").Text);
    }

    [Fact]
    public async Task LongProviderIsEllipsizedWithoutEscapingTheStatusRow()
    {
        string provider = new('p', 80);
        SimpleSwitcherController controller = Controller(Status(
            current: provider,
            configured: [provider],
            sqliteSupported: true));
        await controller.RefreshAsync();
        using SimpleMainForm form = Form(controller);
        form.Show();
        form.Size = form.MinimumSize;
        PerformLayoutRecursively(form);

        Label current = Field<Label>(form, "_currentProviderValue");
        Label sqliteCaption = Descendants(form)
            .OfType<Label>()
            .Single(label => label.Text == "SQLite：");
        Rectangle currentBounds = BoundsIn(form, current);
        Rectangle sqliteBounds = BoundsIn(form, sqliteCaption);

        Assert.False(current.AutoSize);
        Assert.True(current.AutoEllipsis);
        Assert.Equal(AutoScaleMode.Dpi, form.AutoScaleMode);
        Assert.Equal(provider, Field<ToolTip>(form, "_toolTip").GetToolTip(current));
        Assert.True(form.ClientRectangle.Contains(currentBounds));
        Assert.False(currentBounds.IntersectsWith(sqliteBounds));

        form.Scale(new SizeF(1.25F, 1.25F));
        form.Size = new Size(650, 475);
        PerformLayoutRecursively(form);

        currentBounds = BoundsIn(form, current);
        sqliteBounds = BoundsIn(form, sqliteCaption);
        Assert.True(form.ClientRectangle.Contains(currentBounds));
        Assert.False(currentBounds.IntersectsWith(sqliteBounds));
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

    private static SimpleSwitcherController Controller(CodexProviderSync.Core.StatusSnapshot status) => new(
        new FakeSimpleProviderService(status),
        new FakeProcessProbe(),
        @"C:\fixture\.codex");

    private static SimpleMainForm Form(SimpleSwitcherController controller)
    {
        string settingsPath = Path.Combine(
            Path.GetTempPath(),
            "codex-switcher-form-" + Guid.NewGuid().ToString("N"),
            "settings.json");
        return new SimpleMainForm(controller, new SimpleSettingsStore(settingsPath));
    }

    private static Rectangle BoundsIn(Control root, Control child) =>
        root.RectangleToClient(child.RectangleToScreen(child.ClientRectangle));

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

    private sealed class ThrowingStatusProviderService : ISimpleProviderService
    {
        public Task<CodexProviderSync.Core.StatusSnapshot> GetStatusAsync(
            string codexHome,
            CancellationToken cancellationToken = default) =>
            Task.FromException<CodexProviderSync.Core.StatusSnapshot>(
                new InvalidOperationException("status failed"));

        public Task<CodexProviderSync.Core.SyncResult> ExecuteAsync(
            CodexProviderSync.Application.ApplicationWriteIntent intent,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
