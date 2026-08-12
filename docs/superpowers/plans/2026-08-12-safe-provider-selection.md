# Safe Provider Selection Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Prevent an OpenAI synchronization from silently becoming a switch to a remembered custom provider.

**Architecture:** Keep the sync Core and Application layers unchanged. Correct the provider-list invariant in `SimpleSwitcherController`, make initial form loading select the live current provider, and place a testable confirmation gate in `SimpleMainForm` before different-provider execution.

**Tech Stack:** C# 13, .NET 10, WinForms, xUnit

## Global Constraints

- Do not read or write `auth.json`, API keys, account data, or provider endpoint values.
- Do not access or modify the CCSwitch database.
- Do not execute a real switch or synchronization against the user's profile.
- Do not add a watcher, startup task, or automatic Codex termination/restart.
- Run only focused tests needed for this production change, then publish once.

---

### Task 1: Make the active Provider a list invariant

**Files:**
- Modify: `desktop/CodexProviderSync.SimpleApp/SimpleSwitcherController.cs`
- Test: `desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherControllerRefreshTests.cs`
- Test: `desktop/CodexProviderSync.SimpleApp.Tests/SimpleSwitcherIntegrationTests.cs`

**Interfaces:**
- Consumes: `StatusSnapshot.CurrentProvider` and `StatusSnapshot.DeclaredProviders`
- Produces: `SimpleSwitcherSnapshot.Providers` containing the current provider and `SelectedProviderId` defaulting to it when no in-window preference exists

- [ ] Write failing tests for an explicit current `openai` with only `custom` declared and for an undeclared non-built-in current provider.
- [ ] Run only those tests and confirm the current provider is absent before the fix.
- [ ] Change `BuildProviders` to add every non-empty current provider.
- [ ] Run the focused controller and composition tests and confirm they pass.

### Task 2: Prevent remembered settings from creating a launch-time switch target

**Files:**
- Modify: `desktop/CodexProviderSync.SimpleApp/SimpleMainForm.cs`
- Test: `desktop/CodexProviderSync.SimpleApp.Tests/SimpleMainFormLifecycleTests.cs`

**Interfaces:**
- Consumes: persisted `SimpleUserSettings` for bounds and compatibility
- Produces: initial `RefreshAsync(null)`, so the controller chooses the live current provider

- [ ] Write a failing form lifecycle test that loads remembered `custom` while live current is `openai` and asserts `openai` is selected.
- [ ] Run the single test and confirm it selects `custom` before the fix.
- [ ] Stop passing `LastProvider` into the initial refresh; retain window-bound restoration and safe close behavior.
- [ ] Run the focused lifecycle tests and confirm they pass.

### Task 3: Confirm a different-provider switch

**Files:**
- Modify: `desktop/CodexProviderSync.SimpleApp/SimpleMainForm.cs`
- Test: `desktop/CodexProviderSync.SimpleApp.Tests/SimpleMainFormLifecycleTests.cs`

**Interfaces:**
- Consumes: `CurrentProviderId`, `SelectedProviderId`, and an injectable `Func<string, string, bool>` confirmation callback
- Produces: execution-time confirmation after the controller status reread; no plan/write on cancellation; normal execution after approval; no prompt for same-provider synchronization

- [ ] Write failing tests for cancellation, approval, and same-provider synchronization.
- [ ] Run those tests and confirm the missing confirmation behavior.
- [ ] Add the confirmation callback with a default warning `MessageBox` showing `current -> target`.
- [ ] Pass the confirmation callback into `ExecuteAsync` and gate after its fresh status read, only when current and target differ.
- [ ] Run the focused lifecycle and execution tests and confirm they pass.

### Task 4: Verify, publish, and deliver

**Files:**
- Modify only if needed: user documentation and release metadata
- Produce: `outputs/CodexProviderSwitcher.exe`

**Interfaces:**
- Consumes: verified SimpleApp source
- Produces: self-contained `win-x64` executable and matching GitHub release asset

- [ ] Run the focused SimpleApp test project once.
- [ ] Run `git diff --check`, prohibited-reference scans, and verify the removed startup task remains absent.
- [ ] Publish to a new sentinel-owned output leaf and copy the single EXE to `outputs`.
- [ ] Perform a visible read-only smoke test without clicking the execution button.
- [ ] Commit, push `main`, tag the patch release, and upload the EXE to the GitHub release.
