import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";
import path from "node:path";
import test from "node:test";
import { fileURLToPath } from "node:url";

const testDirectory = path.dirname(fileURLToPath(import.meta.url));
const repoRoot = path.resolve(testDirectory, "..");

test("simple GUI publishes the dedicated self-contained executable", async () => {
  const script = await readFile(
    path.join(repoRoot, "scripts", "publish-simple-gui.ps1"),
    "utf8"
  );
  const project = await readFile(
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
