# Safe Provider Selection Design

## Problem

`CodexProviderSwitcher` v0.4.0 can omit the active provider when `config.toml`
contains an explicit built-in `model_provider = "openai"` without a matching
`[model_providers.openai]` table. The GUI then restores a remembered `custom`
selection, so a click intended as an OpenAI synchronization becomes an
`openai -> custom` switch.

## Scope

The fix is limited to the simple Windows GUI. It does not change the sync
transaction, session transformation, authentication files, provider endpoint
configuration, CCSwitch data, watcher behavior, or Codex process handling.

## Required behavior

1. The provider list contains every explicitly declared provider and the
   provider currently active in `config.toml`, including explicit `openai`.
2. Initial window loading selects the actual current provider. A remembered
   provider may be retained for settings compatibility, but it must not become
   an implicit switch target on launch.
3. A manual refresh may preserve a provider the user explicitly selected in
   the open window, provided it remains available.
4. Synchronizing the current provider proceeds without an extra switch
   confirmation.
5. Switching to a different provider requires a confirmation that names both
   ends as `current -> target`. The current end comes from the execution-time
   status reread, not a stale window snapshot. Cancelling performs no plan or
   write.
6. If the current provider is not declared, the GUI includes it instead of
   silently falling back to the first declared provider.

## UI and safety

The confirmation text is Chinese and states that provider configuration and
session metadata will be changed. Existing Codex-running, SQLite, recovery,
single-instance, and rollback protections remain unchanged. The GUI never
reads or writes the CCSwitch database.

## Verification

Focused controller and real-composition tests cover explicit OpenAI inclusion
and current selection. Form lifecycle tests cover ignoring remembered targets
at startup and both confirmation outcomes. A focused SimpleApp test run and a
self-contained publish are sufficient; no real profile switch is permitted.
