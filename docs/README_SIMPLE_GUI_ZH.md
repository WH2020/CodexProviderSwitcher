# Codex Provider Switcher（精简切换器）

`CodexProviderSwitcher.exe` 只做两件事：切换已配置的 Provider，并同步历史会话可见性。它适合在 Provider 已经由现有配置准备好时，快速完成一次安全的切换和同步。

## 使用流程

1. 手动关闭 Codex，然后打开精简切换器；它不会替你结束或重启 Codex。
2. 确认“当前 Provider”；软件启动时始终选择这个实时当前值，不会自动恢复上次的切换目标。
3. 从下拉框选择目标 Provider，点击“切换并同步”。目标不同时，会显示 `当前 → 目标` 确认；选择与当前相同的 Provider 时，会直接执行重新同步。
4. 结果为 **Success** 后，可以重新打开 Codex。
5. 结果为 **Incomplete** 时，表示有文件仍被占用；关闭占用该文件的程序后重试。
6. 出现 **RecoveryRequired** 时，不要继续尝试；使用完整 GUI 或 CLI，并按界面显示的绑定备份执行恢复。

## 不做什么

精简切换器绝不会管理 `auth.json`、API Key、账号、`base_url` 或 Provider 定义；不会终止或重启进程，也不会管理 watcher 行为。需要认证、Provider 配置、备份恢复、清理或其它高级操作时，请使用完整 GUI 或 CLI。

## 本地文件

- 设置：`%AppData%\codex-provider-switcher\settings.json`
- 启动错误日志：`%AppData%\codex-provider-switcher\startup-error.log`
