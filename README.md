# CodexProviderSwitcher

Windows 可视化工具，用于手动切换 Codex Provider，并同步历史会话的可见性元数据。

本项目基于 [codex-provider-sync](https://github.com/Dailin521/codex-provider-sync) 的 Core 与 Application 层构建独立精简 WinForms 前端，复用其写入规划、备份、事务回滚和 SQLite 安全检查。

## 功能范围

- 显示当前 Provider 和配置中显式声明的 Provider；当前 Provider 始终可选。
- 启动时始终选择实时当前 Provider，不使用历史选择作为隐式切换目标。
- 切换到不同 Provider 前显示 `当前 → 目标` 确认；同 Provider 同步不弹确认。
- 目标 Provider 不同时，切换根级 `model_provider` 并同步会话。
- 目标 Provider 相同时，保持 `config.toml` 字节不变，只同步会话。
- 同步 rollout 与 SQLite 中的会话可见性元数据。
- 写入前检查 Codex 进程、操作锁、SQLite 状态和待恢复事务。
- 显示操作结果、备份路径和需要人工处理的错误。

## 明确不做

- 不管理账号、API Key、`auth.json` 或 `base_url`。
- 不新增或编辑 `[model_providers.*]` 配置。
- 不自动结束或重启 Codex。
- 不删除锁文件。
- 不提供 watcher、后台监控、自动同步或开机自启动。
- 不在精简界面中提供备份恢复、备份清理或自动更新。

## 使用方法

1. 关闭 Codex CLI、Codex App、app-server 和仍在使用会话文件的终端。
2. 运行 `CodexProviderSwitcher.exe`。
3. 点击“刷新”，确认 SQLite 可用且没有 Codex 相关进程。
4. 选择目标 Provider。
5. 点击“切换并同步”；如果目标不同，请在确认框再次核对 `当前 → 目标`。
6. 操作成功后重新打开 Codex。

如果窗口检测到 Codex 正在运行，它只会阻止写入并提示手动关闭，不会结束进程。

## 构建

要求：

- Windows x64
- .NET 10 SDK

发布自包含单文件 EXE：

```powershell
.\scripts\publish-simple-gui.ps1 -Output artifacts\simple-win-x64
```

输出文件：

```text
artifacts\simple-win-x64\CodexProviderSwitcher.exe
```

发布脚本只允许清理仓库 `artifacts` 下带所有权标记的专属输出目录，并拒绝 reparse point/junction 路径。

## 项目位置

- 精简 GUI：`desktop/CodexProviderSync.SimpleApp`
- GUI 测试：`desktop/CodexProviderSync.SimpleApp.Tests`
- Application 层：`desktop/CodexProviderSync.Application`
- Core 层：`desktop/CodexProviderSync.Core`
- 中文详细说明：`docs/README_SIMPLE_GUI_ZH.md`

## 已知限制

- 目标平台为 Windows x64。
- 必须先在 `config.toml` 中配置自定义 Provider；本软件不创建 Provider 配置。
- 跨 Provider 的 `encrypted_content` 兼容性由 Codex 服务端决定，本软件只同步可见性元数据。
- 发生未完成事务时，精简界面会给出绑定备份路径，但恢复需使用上游完整工具或 CLI。

## License

[MIT](LICENSE)
