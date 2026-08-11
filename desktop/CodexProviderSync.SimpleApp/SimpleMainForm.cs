using System.Runtime.InteropServices;

namespace CodexProviderSync.SimpleApp;

internal sealed class SimpleMainForm : Form
{
    private readonly SimpleSwitcherController _controller;
    private readonly Func<CancellationToken, Task<SimpleUserSettings>> _loadSettings;
    private readonly Func<SimpleUserSettings, CancellationToken, Task> _saveSettings;
    private readonly Action<string> _clipboardWriter;
    private bool _rendering;

    private readonly Label _currentProviderValue = new()
    {
        AutoSize = false,
        AutoEllipsis = true,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft
    };
    private readonly Label _sqliteStatusValue = new() { AutoSize = true };
    private readonly ComboBox _providerCombo = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Dock = DockStyle.Fill
    };
    private readonly Button _executeButton = new()
    {
        Text = "切换并同步",
        Height = 44,
        Dock = DockStyle.Fill
    };
    private readonly Button _refreshButton = new() { Text = "刷新", AutoSize = true };
    private readonly Button _copyButton = new() { Text = "复制结果", AutoSize = true };
    private readonly Label _stateLabel = new() { AutoSize = true };
    private readonly RichTextBox _detailsBox = new()
    {
        ReadOnly = true,
        Dock = DockStyle.Fill,
        DetectUrls = false
    };
    private readonly ToolTip _toolTip = new();

    internal SimpleMainForm(
        SimpleSwitcherController controller,
        SimpleSettingsStore settings,
        Func<CancellationToken, Task<SimpleUserSettings>>? settingsLoader = null,
        Func<SimpleUserSettings, CancellationToken, Task>? settingsSaver = null,
        Action<string>? clipboardWriter = null)
    {
        _controller = controller;
        _loadSettings = settingsLoader ?? settings.LoadAsync;
        _saveSettings = settingsSaver ?? settings.SaveAsync;
        _clipboardWriter = clipboardWriter ?? Clipboard.SetText;

        Text = "Codex Provider Switcher";
        Size = new Size(560, 420);
        MinimumSize = new Size(520, 380);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9F);

        _executeButton.BackColor = Color.FromArgb(220, 252, 231);
        _executeButton.ForeColor = Color.FromArgb(22, 101, 52);
        _executeButton.FlatStyle = FlatStyle.Flat;
        _executeButton.FlatAppearance.BorderColor = Color.FromArgb(134, 239, 172);
        _executeButton.FlatAppearance.BorderSize = 1;
        _executeButton.FlatAppearance.MouseOverBackColor = Color.FromArgb(187, 247, 208);
        _executeButton.UseVisualStyleBackColor = false;

        _detailsBox.BackColor = SystemColors.Window;
        _detailsBox.Font = new Font("Consolas", 9F);
        _detailsBox.WordWrap = true;

        Controls.Add(BuildLayout());

        _controller.SnapshotChanged += ControllerOnSnapshotChanged;
        _providerCombo.SelectedIndexChanged += ProviderComboOnSelectedIndexChanged;
        _refreshButton.Click += RefreshButtonOnClick;
        _executeButton.Click += ExecuteButtonOnClick;
        _copyButton.Click += CopyButtonOnClick;
        Shown += FormOnShown;
        FormClosing += FormOnFormClosing;

        Render(_controller.Snapshot);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _controller.SnapshotChanged -= ControllerOnSnapshotChanged;
            _toolTip.Dispose();
        }
        base.Dispose(disposing);
    }

    private Control BuildLayout()
    {
        TableLayoutPanel layout = new()
        {
            ColumnCount = 1,
            RowCount = 6,
            Dock = DockStyle.Fill,
            Padding = new Padding(14)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        TableLayoutPanel statusLayout = new()
        {
            ColumnCount = 4,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 10)
        };
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        statusLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        statusLayout.Controls.Add(StatusCaption("当前 Provider"), 0, 0);
        statusLayout.Controls.Add(_currentProviderValue, 1, 0);
        statusLayout.Controls.Add(StatusCaption("SQLite"), 2, 0);
        statusLayout.Controls.Add(_sqliteStatusValue, 3, 0);

        TableLayoutPanel selectionLayout = new()
        {
            AutoSize = true,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 0, 8)
        };
        selectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        selectionLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Label providerCaption = StatusCaption("Provider");
        providerCaption.Margin = new Padding(0, 5, 10, 0);
        _providerCombo.Margin = new Padding(0, 0, 8, 0);
        _refreshButton.Margin = new Padding(0);
        selectionLayout.Controls.Add(providerCaption, 0, 0);
        selectionLayout.Controls.Add(_providerCombo, 1, 0);
        selectionLayout.Controls.Add(_refreshButton, 2, 0);

        _executeButton.Margin = new Padding(0, 4, 0, 4);
        _stateLabel.Margin = new Padding(0, 6, 0, 5);
        _detailsBox.Margin = new Padding(0);

        FlowLayoutPanel copyLayout = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        _copyButton.Margin = new Padding(0);
        copyLayout.Controls.Add(_copyButton);

        layout.Controls.Add(statusLayout, 0, 0);
        layout.Controls.Add(selectionLayout, 0, 1);
        layout.Controls.Add(_executeButton, 0, 2);
        layout.Controls.Add(_stateLabel, 0, 3);
        layout.Controls.Add(_detailsBox, 0, 4);
        layout.Controls.Add(copyLayout, 0, 5);
        return layout;
    }

    private static Label StatusCaption(string text) => new()
    {
        AutoSize = true,
        Text = text + "：",
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(0, 0, 8, 0)
    };

    private async void FormOnShown(object? sender, EventArgs eventArgs)
    {
        SimpleUserSettings settings;
        try
        {
            settings = await _loadSettings(CancellationToken.None);
        }
        catch
        {
            settings = SimpleUserSettings.Default;
        }
        if (IsDisposed || Disposing)
        {
            return;
        }
        RestoreWindowBounds(settings.WindowBounds);
        await RefreshSafelyAsync(settings.LastProvider);
    }

    private void RestoreWindowBounds(WindowBoundsState? saved)
    {
        if (saved is null || saved.Width <= 0 || saved.Height <= 0)
        {
            return;
        }

        Rectangle bounds = new(saved.X, saved.Y, saved.Width, saved.Height);
        if (!Screen.AllScreens.Any(screen => screen.WorkingArea.IntersectsWith(bounds)))
        {
            return;
        }

        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        if (saved.Maximized)
        {
            WindowState = FormWindowState.Maximized;
        }
    }

    private void ProviderComboOnSelectedIndexChanged(object? sender, EventArgs eventArgs)
    {
        if (!_rendering && _providerCombo.SelectedItem is string provider)
        {
            _controller.SelectProvider(provider);
        }
    }

    private async void RefreshButtonOnClick(object? sender, EventArgs eventArgs) =>
        await RefreshSafelyAsync(_providerCombo.SelectedItem as string);

    private async Task RefreshSafelyAsync(string? preferredProvider)
    {
        try
        {
            await _controller.RefreshAsync(preferredProvider);
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
            if (!IsDisposed && !Disposing)
            {
                Render(_controller.Snapshot);
            }
        }
    }

    private async void ExecuteButtonOnClick(object? sender, EventArgs eventArgs)
    {
        try
        {
            await _controller.ExecuteAsync();
        }
        catch (OperationCanceledException)
        {
            return;
        }

        SimpleSwitcherSnapshot snapshot = _controller.Snapshot;
        if (snapshot.Activity is SimpleActivity.Blocked or SimpleActivity.RecoveryRequired)
        {
            MessageBox.Show(
                this,
                string.IsNullOrWhiteSpace(snapshot.Details)
                    ? snapshot.Message
                    : snapshot.Message + Environment.NewLine + Environment.NewLine + snapshot.Details,
                "Codex Provider Switcher",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void CopyButtonOnClick(object? sender, EventArgs eventArgs)
    {
        string text = string.Join(
            Environment.NewLine + Environment.NewLine,
            new[] { _stateLabel.Text, _detailsBox.Text }
                .Where(value => !string.IsNullOrWhiteSpace(value)));
        if (!string.IsNullOrWhiteSpace(text))
        {
            try
            {
                _clipboardWriter(text);
            }
            catch (ExternalException)
            {
                _stateLabel.Text = "复制失败，请重试。";
                _stateLabel.ForeColor = Color.Firebrick;
            }
        }
    }

    private void ControllerOnSnapshotChanged(object? sender, EventArgs eventArgs)
    {
        if (IsDisposed || Disposing)
        {
            return;
        }
        if (InvokeRequired)
        {
            if (!IsHandleCreated)
            {
                return;
            }
            try
            {
                BeginInvoke((Action)(() =>
                {
                    if (!IsDisposed && !Disposing)
                    {
                        Render(_controller.Snapshot);
                    }
                }));
            }
            catch (InvalidOperationException)
            {
            }
            return;
        }
        if (!IsDisposed && !Disposing)
        {
            Render(_controller.Snapshot);
        }
    }

    private void Render(SimpleSwitcherSnapshot snapshot)
    {
        _rendering = true;
        try
        {
            _currentProviderValue.Text = snapshot.CurrentProviderId ?? "—";
            _toolTip.SetToolTip(_currentProviderValue, snapshot.CurrentProviderId ?? string.Empty);
            _sqliteStatusValue.Text = SqliteStatus(snapshot);

            string[] providerIds = snapshot.Providers.Select(item => item.Id).ToArray();
            if (!_providerCombo.Items.Cast<string>().SequenceEqual(providerIds, StringComparer.Ordinal))
            {
                _providerCombo.BeginUpdate();
                try
                {
                    _providerCombo.Items.Clear();
                    _providerCombo.Items.AddRange(providerIds);
                }
                finally
                {
                    _providerCombo.EndUpdate();
                }
            }
            _providerCombo.SelectedItem = snapshot.SelectedProviderId;

            _stateLabel.Text = snapshot.Message;
            _stateLabel.ForeColor = StateColor(snapshot.Activity);
            _detailsBox.Text = FormatDetails(snapshot);
            _providerCombo.Enabled = snapshot.Activity is not SimpleActivity.Loading and not SimpleActivity.Executing;
            _refreshButton.Enabled = snapshot.CanRefresh;
            _executeButton.Enabled = snapshot.CanExecute;
            _copyButton.Enabled = !string.IsNullOrWhiteSpace(snapshot.Message)
                || !string.IsNullOrWhiteSpace(_detailsBox.Text);
            UseWaitCursor = snapshot.Activity is SimpleActivity.Loading or SimpleActivity.Executing;
        }
        finally
        {
            _rendering = false;
        }
    }

    private static string SqliteStatus(SimpleSwitcherSnapshot snapshot)
    {
        if (snapshot.Activity == SimpleActivity.Loading)
        {
            return "读取中";
        }
        return snapshot.SqliteSupported switch
        {
            true => "可用",
            false => "不支持",
            null => "未知"
        };
    }

    private static Color StateColor(SimpleActivity activity) => activity switch
    {
        SimpleActivity.Success => Color.FromArgb(22, 101, 52),
        SimpleActivity.Loading or SimpleActivity.Executing or SimpleActivity.Incomplete => Color.DarkOrange,
        SimpleActivity.Blocked or SimpleActivity.Failed or SimpleActivity.RecoveryRequired => Color.Firebrick,
        _ => SystemColors.ControlText
    };

    private static string FormatDetails(SimpleSwitcherSnapshot snapshot)
    {
        List<string> lines = [];
        if (snapshot.LastResult is { } result)
        {
            lines.Add("Provider: " + result.TargetProvider);
            lines.Add("会话文件: " + result.ChangedRolloutFiles);
            lines.Add("SQLite 行: " + result.SqliteRowsUpdated);
            lines.Add("跳过文件: " + result.SkippedRolloutFiles);
            lines.Add("备份: " + result.BackupDirectory);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.Details))
        {
            lines.Add(snapshot.Details);
        }
        if (!string.IsNullOrWhiteSpace(snapshot.EncryptedContentWarning))
        {
            lines.Add(snapshot.EncryptedContentWarning);
        }
        return string.Join(Environment.NewLine, lines);
    }

    private void FormOnFormClosing(object? sender, FormClosingEventArgs eventArgs)
    {
        if (_controller.Snapshot.Activity == SimpleActivity.Executing)
        {
            eventArgs.Cancel = true;
            _stateLabel.Text = "操作正在进行，请在操作完成后再关闭。";
            _stateLabel.ForeColor = Color.DarkOrange;
            return;
        }

        Rectangle bounds = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
        SimpleUserSettings settings = new(
            _providerCombo.SelectedItem as string ?? _controller.Snapshot.SelectedProviderId,
            new WindowBoundsState
            {
                X = bounds.X,
                Y = bounds.Y,
                Width = bounds.Width,
                Height = bounds.Height,
                Maximized = WindowState == FormWindowState.Maximized
            });
        try
        {
            _saveSettings(settings, CancellationToken.None).GetAwaiter().GetResult();
        }
        catch
        {
        }
    }
}
