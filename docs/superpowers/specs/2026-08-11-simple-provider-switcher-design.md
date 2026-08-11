# Codex Provider Switcher 精简版设计

## 1. 目标与结论

在现有 `codex-provider-sync` Windows 代码库内新增一个独立、可见的 WinForms 程序 `CodexProviderSwitcher.exe`。程序只完成两件事：切换 Codex 根级 `model_provider`，以及把历史会话的可见性元数据同步到目标 Provider。

实现复用现有 Application 与 Core 层的状态读取、写入规划、锁、备份、事务日志、回滚和 SQLite 安全能力，不复制或重写同步算法。

## 2. 用户与使用场景

目标用户是在同一台 Windows 电脑上使用多个已配置 Codex Provider，并希望手动决定何时切换和同步的人。

典型流程：

1. 用户关闭 Codex CLI、Codex App、app-server 和相关终端。
2. 用户打开 `CodexProviderSwitcher.exe`。
3. 程序读取 `%USERPROFILE%\.codex`，显示当前 Provider 和可切换 Provider。
4. 用户选择目标 Provider，点击“切换并同步”。
5. 程序完成配置切换与历史会话元数据同步，显示结果。
6. 用户重新打开 Codex。

## 3. 范围和优先级

### 必须实现

- 使用可见的 Windows 图形窗口，不依赖命令行窗口。
- 启动后自动刷新当前 Provider、配置中已声明的 Provider 和同步状态。
- 仅允许选择配置中已声明的 Provider；隐式默认的 `openai` 也视为合法 Provider。
- 提供一个主操作按钮“切换并同步”。
- 目标 Provider 与当前 Provider 不同时，更新根级 `model_provider` 并同步 rollout 与 SQLite 会话可见性元数据。
- 目标 Provider 与当前 Provider 相同时，不重复改写配置，但仍执行会话同步。
- 同一时间只允许一个刷新或写入操作；忙碌期间禁用选择与按钮。
- 发现已知 Codex 进程、有效 provider-sync 锁、SQLite 占用或活动会话文件占用时，不结束进程、不删除锁，停止写入并给出手动关闭提示。
- 写入前沿用现有自动备份；失败时沿用现有事务回滚和恢复证据。
- 成功后显示目标 Provider、变更的 rollout 数、更新的 SQLite 行数、备份目录和“现在可以重新打开 Codex”。
- 失败后显示可执行的中文提示；存在未完成事务时显示绑定备份目录和恢复要求。

### 建议实现

- 提供“刷新”按钮，供用户关闭 Codex 后重新检查状态。
- 在窗口底部显示简短操作日志，并允许复制结果。
- 记住窗口位置和上次选择的 Provider，但启动后必须重新读取真实状态，不把缓存当作当前状态。

### 可以延期

- 系统托盘入口。
- 深色主题。
- 多语言界面。
- 自动更新功能。

### 明确不做

- 账号登录或账号切换。
- API Key、`auth.json`、`base_url` 或 `[model_providers.*]` 配置管理。
- 自动结束或自动重启 Codex 相关进程。
- 后台监控、文件 watcher 或自动同步。
- 手动添加未配置的 Provider。
- 在界面中提供备份清理、备份恢复、Codex Home 切换、SQLite Home 修改、自定义 model 或更新检查。
- 修改消息内容、标题、时间排序或 `encrypted_content`。

## 4. 方案选择

采用“新增独立精简 WinForms 前端，复用现有 Application/Core”方案。

未采用的方案：

- 直接发布现有完整 GUI：实现成本最低，但包含恢复、清理、更新、路径和 model 等超出本产品范围的入口。
- 用 PowerShell 或 Node 包装 CLI：代码更少，但会重复处理进程、结构化结果、窗口状态和发布依赖，错误反馈与测试能力弱于现有 .NET Application 层。

主要取舍：新增一个小型桌面项目会增加一个发布产物，但能够保持单一职责，且不改变现有完整 GUI 的行为和用户。

## 5. 界面设计

窗口采用单页布局，默认大小约 560×420 像素，可调整大小，首次启动居中显示。

从上到下包含：

1. 标题和范围提示：“只切换 Provider 并同步会话；不会修改账号或密钥。”
2. 当前状态卡片：Codex Home、当前 Provider、SQLite 状态和待恢复事务状态。
3. 目标 Provider 下拉框：只显示配置中声明的 Provider，并标记当前项。
4. 主按钮“切换并同步”和次按钮“刷新”。
5. 结果区域：显示空闲、检查中、执行中、成功、占用阻塞或失败状态。
6. 只读日志区域和“复制结果”按钮。

空状态和禁用规则：

- `config.toml` 不存在、格式错误、没有合法目标 Provider、SQLite 路径不受支持或存在待恢复事务时，禁用主按钮。
- 未选择目标 Provider 时，禁用主按钮。
- 刷新或写入期间禁用所有可变控件，避免重复点击。
- 失败后保留目标选择，用户处理占用后可点击“刷新”并重试。

## 6. 架构与模块职责

新增项目 `desktop/CodexProviderSync.SimpleApp`，引用：

- `CodexProviderSync.Application`：状态刷新、请求准备、写入规划和操作生命周期。
- `CodexProviderSync.Core`：配置解析、Provider 发现、同步、锁、SQLite、备份和事务恢复。

精简程序包含以下边界清晰的单元：

- `Program`：单实例启动、异常日志和 WinForms 生命周期。
- `SimpleMainForm`：控件创建、状态渲染和用户事件；不直接实现同步算法。
- `SimpleSwitcherController`：把 Application 状态裁剪为精简 UI 状态，只暴露刷新和“切换并同步”。
- `CodexProcessProbe`：只读枚举进程名为 `codex`、`codex-app-server` 或 `app-server`（不区分大小写）的进程并返回显示信息；从不结束进程。程序自身 PID 必须排除。
- `SimpleAppPaths`：解析默认 `%USERPROFILE%\.codex`、应用日志和窗口设置路径。

现有完整 GUI、Automation API、CLI 和 watcher 的行为保持不变。

## 7. Provider 列表规则

Provider 候选来源于 `StatusSnapshot.ConfiguredProviders` 和当前 Provider。现有发现结果中只有 `ProviderSource.Config` 的选项可以进入下拉框；仅在 rollout、SQLite 或 GUI 历史设置中出现的 Provider 不可作为切换目标。

`openai` 在没有显式表但属于当前隐式默认值时仍可选择。Provider ID 使用现有区分大小写规则，不进行模糊匹配或自动修正。

该限制避免把只有历史痕迹、但没有 `[model_providers.<id>]` 配置的名称写入根级 `model_provider`。

## 8. 操作数据流

### 启动与刷新

1. 解析默认 Codex Home。
2. 调用现有 Application 状态用例读取 `config.toml`、rollout、SQLite、锁和事务状态。
3. 过滤 Provider 列表并选择当前 Provider。
4. 根据状态渲染控件可用性与中文诊断。

### 切换并同步

1. 锁定 UI 状态，阻止并发点击。
2. 使用 `CodexProcessProbe` 检查已知 Codex 相关进程。发现进程时直接返回阻塞结果，提示用户手动关闭后刷新；不进入写入规划。
3. 再次刷新状态，防止使用过期快照。
4. 验证目标 Provider 仍在配置列表中。
5. 如果目标等于当前 Provider，提交现有 `SyncIntent`。
6. 如果目标不同，提交现有 `SwitchIntent`，model 选择固定为 `FollowProviderModelSelection`，自动备份保留数沿用 Core 默认值 5。
7. Application/Core 在写入前重新验证计划、获取操作锁并创建绑定备份与事务日志。
8. Core 原子更新配置和 rollout，并在 SQLite 事务内更新状态；完成后写入事务终态。
9. 刷新真实状态，显示结构化结果并恢复 UI。

进程枚举是提前改善体验的提示机制，不替代 Core 的锁、快照和 SQLite 校验。即使进程名称未被识别，底层安全检查仍是最终写入门槛。

## 9. 错误处理与恢复

- 已知 Codex 进程：阻塞，列出进程名和 PID，提示手动关闭；不弹出结束进程选项。
- 有效 provider-sync 锁：阻塞并显示持有者信息；不删除锁声明。
- SQLite busy/locked：不重试写入，提示关闭 Codex 后刷新并重试。
- rollout 文件被活动会话占用：沿用 Core 的跳过行为，成功结果必须显示“同步未完整”和跳过文件数，不显示“现在可以重新打开 Codex”；用户关闭占用进程后刷新并再次同步。已写入的目标仍由现有事务保证一致性。
- 配置或数据库在确认后发生变化：写入计划校验失败，刷新状态后要求重试。
- 普通写入失败：显示原始操作错误和回滚结果。
- 回滚失败或未完成事务：显示“需要恢复”、绑定备份目录和恢复说明；精简程序不提供恢复按钮，用户可使用现有完整 GUI 或 CLI 恢复。
- WSL UNC SQLite Home：在刷新阶段显示不支持并禁用主按钮，避免跨 Windows/WSL 文件层写 SQLite。
- 含 `encrypted_content` 的历史会话：显示现有警告；程序只保证可见性元数据同步，不承诺旧加密内容可继续对话。

## 10. 安全、数据一致性和可维护性

- 不读取或写入 `auth.json`，不记录凭据。
- 不调用进程终止 API。
- 不直接删除锁、数据库或用户会话文件。
- 所有写入必须经过现有 Application 写入计划与 Core 事务实现。
- 日志不得包含消息正文、API Key 或认证内容。
- 新前端只做状态裁剪与交互，不复制 Core 的配置/SQLite/rollout 写入逻辑。
- 精简程序使用独立单实例标识，避免与自身重复启动；底层 provider-sync 操作锁继续与 CLI、完整 GUI 和 watcher 互斥。

## 11. 测试设计

### 单元测试

- Provider 过滤只保留配置来源与当前隐式 `openai`。
- 相同 Provider 生成 `SyncIntent`，不同 Provider 生成 `SwitchIntent`。
- `SwitchIntent` 固定使用跟随 Provider 的 model 策略和默认保留 5 份备份。
- 忙碌、无选择、不支持的 SQLite、待恢复事务和刷新失败正确禁用主按钮。
- 进程探测返回占用时不调用 Application 写入端口。
- 连续点击只启动一次操作。
- 完整成功、存在跳过文件的未完整成功、锁占用、SQLite busy、回滚成功和需要恢复的错误映射为明确中文状态。

### 集成测试

- 使用隔离 Codex Home 完成 `openai -> custom` 切换，校验根级 `model_provider`、rollout 与 SQLite。
- 对当前 Provider 执行重同步，校验配置字节不变且会话元数据被修复。
- 写入中注入失败，校验配置、rollout 与 SQLite 回滚并保留备份证据。
- 模拟有效操作锁和 SQLite 占用，校验零目标文件变化。
- 重启精简程序后重新读取真实 Provider，而不是显示缓存值。

### 可见 GUI 验收

- 在真实可交互 Windows 桌面启动发布后的单文件 EXE。
- 验证当前状态、Provider 选择、刷新、主按钮、忙碌禁用、复制结果和窗口重启持久化。
- 验证检测到 Codex 进程时只提示手动关闭，没有结束进程行为。
- 验证完整成功消息包含 Provider 和同步计数；存在跳过文件时显示未完整状态和重试步骤；失败消息包含下一步操作。

## 12. 验收标准

- 用户无需打开 PowerShell，即可在可见窗口中完成一次 Provider 切换和会话同步。
- 程序界面不存在账号、密钥、认证、Provider 地址、恢复、清理、更新或 watcher 入口。
- `openai -> custom` 正常路径完成后，配置、rollout 和 SQLite 一致指向 `custom`。
- 选择当前 Provider 时只同步会话，`config.toml` 保持字节级不变。
- Codex 正在运行、存在有效锁或 SQLite 被占用时，程序不结束进程、不删除锁且不产生部分写入。
- 失败时要么自动恢复原状态，要么给出绑定备份和明确恢复要求；不得静默留下未知状态。
- Node 基线测试、相关 .NET 单元/集成测试、Release 构建和真实可见 GUI 验收全部通过后才能交付 EXE。

## 13. 工程约束与已知风险

- 目标平台为 Windows x64，沿用仓库的 `net10.0-windows` 和 WinForms。
- 实施、编译和 .NET 测试需要 .NET 10 SDK。本机当前只发现 .NET 10.0.10 运行时，没有 SDK；开始实现前必须安装或提供可用的 .NET 10 SDK。
- 当前基线 Node 测试使用 Node.js v24.14.0，192 项全部通过。
- 现有源码仓库是浅历史，设计与实现位于独立分支 `feat/simple-provider-switcher` 的 worktree 中。
- 仅靠进程名无法覆盖所有 Codex 启动形态，因此进程探测只做前置提示，最终安全性依赖共享操作锁、计划快照和 SQLite 锁校验。
- 跨 Provider 的 `encrypted_content` 兼容性不在本项目控制范围内，界面必须保留警告。
