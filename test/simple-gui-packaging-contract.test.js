import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(testDirectory, "..");

test("simple GUI publishes the dedicated self-contained executable", async () => {
  const script = fs.readFileSync(
    path.join(repoRoot, "scripts", "publish-simple-gui.ps1"),
    "utf8"
  );
  const project = fs.readFileSync(
    path.join(
      repoRoot,
      "desktop",
      "CodexProviderSync.SimpleApp",
      "CodexProviderSync.SimpleApp.csproj"
    ),
    "utf8"
  );

  assert.match(script, /CodexProviderSync\.SimpleApp/);
  assert.match(script, /PublishSingleFile=true/);
  assert.match(script, /--self-contained true/);
  assert.match(script, /win-x64/);
  assert.match(project, /<AssemblyName>CodexProviderSwitcher<\/AssemblyName>/);
});

test("simple GUI publish cleanup is confined to owned artifacts leaves", (t) => {
  const fixture = fs.mkdtempSync(path.join(os.tmpdir(), "simple-gui-publish-"));
  const scriptPath = path.join(fixture, "scripts", "publish-simple-gui.ps1");
  const project = path.join(fixture, "desktop", "CodexProviderSync.SimpleApp", "CodexProviderSync.SimpleApp.csproj");
  const fakeDotnet = path.join(fixture, "fake-dotnet.cmd");
  const sentinel = ".codex-provider-switcher-publish-root";
  const sentinelContent = "codex-provider-switcher-simple-publish-root-v1\n";

  fs.mkdirSync(path.dirname(scriptPath), { recursive: true });
  fs.mkdirSync(path.dirname(project), { recursive: true });
  fs.mkdirSync(path.join(fixture, "artifacts"));
  fs.writeFileSync(scriptPath, fs.readFileSync(path.join(repoRoot, "scripts", "publish-simple-gui.ps1")));
  fs.writeFileSync(project, "<Project />\n");
  fs.writeFileSync(fakeDotnet, [
    "@echo off",
    "setlocal EnableDelayedExpansion",
    "set output=",
    "> \"%FAKE_DOTNET_LOG%\\args.txt\" (",
    "  for %%A in (%*) do echo %%~A",
    ")",
    "set previous=",
    "for %%A in (%*) do (",
    "  if \"!previous!\"==\"-o\" set output=%%~A",
    "  set previous=%%~A",
    ")",
    "if not \"%FAKE_DOTNET_EXIT%\"==\"0\" exit /b %FAKE_DOTNET_EXIT%",
    "if \"%FAKE_DOTNET_EXE%\"==\"1\" (",
    "  if not exist \"%output%\" mkdir \"%output%\"",
    "  > \"%output%\\CodexProviderSwitcher.exe\" echo fake",
    ")",
    "exit /b 0",
  ].join("\r\n"));

  const invoke = (output, { exit = 0, exe = true } = {}) => {
    const logDir = fs.mkdtempSync(path.join(fixture, "log-"));
    return spawnSync("powershell.exe", ["-NoProfile", "-ExecutionPolicy", "Bypass", "-File", scriptPath, "-DotnetPath", fakeDotnet, "-Output", output], {
      cwd: fixture,
      encoding: "utf8",
      env: { ...process.env, FAKE_DOTNET_LOG: logDir, FAKE_DOTNET_EXIT: String(exit), FAKE_DOTNET_EXE: exe ? "1" : "0" },
    });
  };

  try {
    const unowned = path.join(fixture, "artifacts", "unowned");
    fs.mkdirSync(unowned);
    fs.writeFileSync(path.join(unowned, "valuable.txt"), "preserve");
    assert.notEqual(invoke("artifacts\\unowned").status, 0, "must reject non-empty unowned output");
    assert.equal(fs.readFileSync(path.join(unowned, "valuable.txt"), "utf8"), "preserve");

    const owned = path.join(fixture, "artifacts", "owned");
    fs.mkdirSync(owned);
    fs.writeFileSync(path.join(owned, sentinel), sentinelContent);
    fs.writeFileSync(path.join(owned, "old.txt"), "remove");
    const logEntriesBeforeSuccess = new Set(fs.readdirSync(fixture).filter((entry) => entry.startsWith("log-")));
    const success = invoke("artifacts\\owned");
    assert.equal(success.status, 0, success.stderr);
    assert.equal(fs.existsSync(path.join(owned, "old.txt")), false);
    assert.equal(fs.readFileSync(path.join(owned, sentinel), "utf8"), sentinelContent);
    assert.equal(fs.existsSync(path.join(owned, "CodexProviderSwitcher.exe")), true);
    const successLog = fs.readdirSync(fixture).find((entry) => entry.startsWith("log-") && !logEntriesBeforeSuccess.has(entry));
    const argumentsText = fs.readFileSync(path.join(fixture, successLog, "args.txt"), "utf8");
    for (const expected of [project, "--runtime", "win-x64", "-c", "Release", "--self-contained", "true", "PublishSingleFile", "IncludeNativeLibrariesForSelfExtract", "EnableCompressionInSingleFile", "DebugType", "None", "DebugSymbols", "false"]) {
      assert.match(argumentsText, new RegExp(expected.replace(/[.*+?^${}()|[\]\\]/g, "\\$&")));
    }

    assert.notEqual(invoke("artifacts\\failed-dotnet", { exit: 9 }).status, 0, "must propagate fake dotnet failure");
    assert.notEqual(invoke("artifacts\\no-exe", { exe: false }).status, 0, "must reject a publish with no executable");

    const junction = path.join(fixture, "artifacts", "junction");
    const outside = fs.mkdtempSync(path.join(os.tmpdir(), "simple-gui-junction-"));
    const junctionResult = spawnSync("cmd.exe", ["/c", "mklink", "/J", junction, outside], { encoding: "utf8" });
    if (junctionResult.status === 0) {
      assert.notEqual(invoke("artifacts\\junction\\leaf").status, 0, "must reject a reparse-point output path");
    } else {
      t.diagnostic(`junction test skipped: ${junctionResult.stderr || junctionResult.stdout}`);
    }

    for (const unsafe of [".", "artifacts", "desktop", ".git", path.join(os.tmpdir(), "outside-simple-gui-publish")]) {
      const result = invoke(unsafe);
      assert.notEqual(result.status, 0, `must reject unsafe output ${unsafe}`);
    }
  } finally {
    fs.rmSync(fixture, { recursive: true, force: true });
  }
});
