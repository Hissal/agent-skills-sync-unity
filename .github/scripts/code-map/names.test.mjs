import { test } from "node:test";
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import { mkdirSync, mkdtempSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";
import { fileURLToPath } from "node:url";
import { documentedNames, inlineCode, missingNames, sourceWords } from "./names.mjs";

const MAIN = fileURLToPath(new URL("./main.mjs", import.meta.url));

function missing(markdown, source, exceptions) {
  return missingNames(documentedNames(markdown), sourceWords([source]), exceptions);
}

test("inline code spans are read, including double-backtick spans", () => {
  assert.deepEqual(inlineCode("`SkillSync` runs `Plan`, then `` a`b ``."), ["SkillSync", "Plan", "a`b"]);
});

test("a map whose names are all in the source has nothing missing", () => {
  const source = "public sealed class SkillSync { public void Plan() {} }";
  assert.deepEqual(missing("`SkillSync` and `SkillSync.Plan`.", source), []);
});

test("a renamed type is reported with the documented name", () => {
  const source = "public sealed class RenamedSync { public void Plan() {} }";
  assert.deepEqual(missing("`SkillSync` and `SkillSync.Plan`.", source), [
    { name: "SkillSync", segment: "SkillSync" },
    { name: "SkillSync.Plan", segment: "SkillSync" },
  ]);
});

test("each dotted segment is checked on its own", () => {
  assert.deepEqual(missing("`SkillSync.Plan`", "class SkillSync { void Run() {} }"), [
    { name: "SkillSync.Plan", segment: "Plan" },
  ]);
  // Lexical only: Plan on another type still counts.
  assert.deepEqual(missing("`SkillSync.Plan`", "class SkillSync {} class Other { void Plan() {} }"), []);
});

test("matching is case-sensitive and by whole word", () => {
  assert.deepEqual(missing("`SkillSync`", "class skillsync {}"), [{ name: "SkillSync", segment: "SkillSync" }]);
  assert.deepEqual(missing("`SkillSync`", "class SkillSyncTests {} class MySkillSync {}"), [
    { name: "SkillSync", segment: "SkillSync" },
  ]);
  assert.deepEqual(missing("`Plan`", "int x = 0xPlan;"), [{ name: "Plan", segment: "Plan" }]);
});

test("paths, files and wildcards are not names", () => {
  const map = "`skills-lock.json` `~/.codex/skills` `Pipeline/` `*Tests.cs` `Record*` `.gitignore` `a b`";
  assert.equal(documentedNames(map).size, 0);
});

test("an exception matches the whole documented name; an unlisted name fails", () => {
  const exceptions = new Map([["com.unity.pipeline", "a package ID"]]);
  assert.deepEqual(missing("`com.unity.pipeline`", "class A {}", exceptions), []);
  assert.deepEqual(missing("`com.unity`", "class A {}", exceptions), [
    { name: "com.unity", segment: "com" },
    { name: "com.unity", segment: "unity" },
  ]);
});

function run(files, args) {
  const root = mkdtempSync(join(tmpdir(), "code-map-"));
  try {
    for (const [path, text] of Object.entries(files)) {
      mkdirSync(join(root, path, ".."), { recursive: true });
      writeFileSync(join(root, path), text);
    }
    return spawnSync(process.execPath, [MAIN, ...args], { cwd: root, encoding: "utf8" });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}

test("the command passes a valid map and fails a renamed name with exit 1", () => {
  const map = "`SkillSync.Plan` and `skills`";
  const passed = run({ "map.md": map, "pkg/Core/SkillSync.cs": "class SkillSync { void Plan() {} }" }, ["map.md", "pkg"]);
  assert.equal(passed.status, 0, passed.stderr);

  const failed = run({ "map.md": map, "pkg/Core/SkillSync.cs": "class RenamedSync { void Plan() {} }" }, ["map.md", "pkg"]);
  assert.equal(failed.status, 1);
  assert.match(failed.stderr, /`SkillSync\.Plan`: `SkillSync`/);
});

test("the command fails with exit 2 on missing or empty input", () => {
  const source = { "pkg/A.cs": "class A {}" };
  assert.match(run(source, ["missing.md", "pkg"]).stderr, /can't read the code map missing\.md/);
  assert.equal(run(source, ["missing.md", "pkg"]).status, 2);

  const noPackage = run({ "map.md": "`A`" }, ["map.md", "missing"]);
  assert.equal(noPackage.status, 2);
  assert.match(noPackage.stderr, /can't read the package source in missing/);

  const noSource = run({ "map.md": "`A`", "pkg/readme.txt": "A" }, ["map.md", "pkg"]);
  assert.equal(noSource.status, 2);
  assert.match(noSource.stderr, /no \.cs files in pkg/);

  const noNames = run({ "map.md": "`a/b`", ...source }, ["map.md", "pkg"]);
  assert.equal(noNames.status, 2);
  assert.match(noNames.stderr, /no C# names/);
});
