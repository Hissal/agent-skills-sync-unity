import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { codeSnippets, commandRows, definedNames, frontmatter, isSafeSkillName, skillProblems } from "./check.mjs";

const PROGRAM = `
  const string Usage =
      "Usage: agent-skills-sync <command> [--project <unity-project>] [options]\\n" +
      "  restore [--dry-run] [--confirm-source <owner/repo>]...\\n" +
      "      Install the selected skills.\\n" +
      "  sync [--install-mode latest|pinned]\\n" +
      "  hook install | status | uninstall\\n" +
      "--project defaults to the current directory.";
  case "--project": break;
  case "--hook-output" when options.Command == "restore": break;
  case "--dry-run" when options.Command == "restore": break;
  case "--confirm-source" when options.Command == "restore" || options.Command == "sync":
  case "--install-mode" when options.Command == "sync":
`;
const PIPELINE = `[CliCommand("skills_restore", "Restore.")] public static object Run([CliArg("dry_run", "Dry.")] bool d) {}
  [CliCommand("skills_sync", "Sync.")] public static object Sync([CliArg("install_mode", "Mode.")] string m) {}`;
const DEFINED = definedNames(PROGRAM, [PIPELINE]);
const HEADER = "---\nname: tool\ndescription: Does things.\n---\n\n";

function problems(body, folder = "tool") {
  return skillProblems(folder, new Map([["SKILL.md", body]]), DEFINED);
}

test("each command's subcommands and flags are read from the usage text, the parser and the Pipeline", () => {
  const { commands } = DEFINED;
  assert.deepEqual([...commands.keys()], ["restore", "sync", "hook", "skills_restore", "skills_sync"]);
  assert.deepEqual([...commands.get("hook").subcommands], ["install", "status", "uninstall"]);
  assert.deepEqual([...commands.get("sync").subcommands], []);
  assert.deepEqual([...commands.get("restore").flags].sort(), ["--confirm-source", "--dry-run", "--hook-output", "--project"]);
  assert.deepEqual([...commands.get("sync").flags].sort(), ["--confirm-source", "--install-mode", "--project"]);
  assert.deepEqual([...commands.get("hook").flags], ["--project"]);
  assert.deepEqual([...commands.get("skills_restore").flags], ["--dry_run"]);
  assert.deepEqual([...commands.get("skills_sync").flags], ["--install_mode"]);
});

test("a skill naming only defined commands and flags passes", () => {
  const body = HEADER + "Run `restore --dry-run`, then `unity command skills_restore --dry_run true`.\n\n```sh\ndotnet <dll> hook install --project <p>\n```\n";
  assert.deepEqual(problems(body), []);
});

test("an unknown flag, command or subcommand is reported", () => {
  assert.deepEqual(problems(HEADER + "Pass `--force`."), ["SKILL.md: unknown flag --force"]);
  assert.deepEqual(problems(HEADER + "`repair --project x`"), ["SKILL.md: unknown command repair in `repair --project x`"]);
  assert.deepEqual(problems(HEADER + "```\ndotnet <dll> hook remove --project x\n```\n"), [
    "SKILL.md: unknown hook subcommand remove in `dotnet <dll> hook remove --project x`",
  ]);
});

test("a flag on a command that does not accept it is reported", () => {
  assert.deepEqual(problems(HEADER + "`sync --dry-run`"), ["SKILL.md: sync does not accept --dry-run in `sync --dry-run`"]);
  assert.deepEqual(problems(HEADER + "`unity command skills_restore --install_mode latest`"), [
    "SKILL.md: skills_restore does not accept --install_mode in `unity command skills_restore --install_mode latest`",
  ]);
});

test("a prefixed snippet is a command even without a flag", () => {
  assert.deepEqual(problems(HEADER + "`dotnet <dll> improt`"), ["SKILL.md: unknown command improt in `dotnet <dll> improt`"]);
  assert.deepEqual(problems(HEADER + "`dotnet <dll> hook`"), ["SKILL.md: hook without a subcommand in `dotnet <dll> hook`"]);
  assert.deepEqual(problems(HEADER + "`dotnet <dll> hook status`"), []);
});

test("a command table names commands in its first cell, and the row's flags are that command's", () => {
  const table = (rows) => HEADER + "| Command | Does |\n| --- | --- |\n" + rows.join("\n") + "\n";
  assert.deepEqual(problems(table(["| `hook install \\| status \\| uninstall` | Hooks. |", "| `sync` | `--install-mode` overrides. |"])), []);
  assert.deepEqual(problems(table(["| `improt` | Imports. |"])), ["SKILL.md: unknown command improt in `improt`"]);
  assert.deepEqual(problems(table(["| `hook install \\| remove` | Hooks. |"])), ["SKILL.md: unknown hook subcommand remove in `hook install | remove`"]);
  assert.deepEqual(problems(table(["| `skills_restore` | `--install_mode latest` |"])), [
    "SKILL.md: skills_restore does not accept --install_mode in `skills_restore`",
  ]);
});

test("only tables headed Command hold command rows", () => {
  assert.deepEqual(commandRows("| Code | Meaning |\n| --- | --- |\n| `0` | Done. |\n"), []);
  assert.deepEqual(commandRows("| Command | Does |\n| --- | --- |\n| `a \\| b` | x |\n"), [["`a | b`", "x"]]);
});

test("snippets without a prefix or a flag after their first word are not commands", () => {
  assert.deepEqual(problems(HEADER + "`git worktree add`, `--confirm-source <owner/repo>`, `<name> --project x`"), []);
});

test("frontmatter must hold a safe name matching the folder and a description", () => {
  assert.deepEqual(problems("# No frontmatter"), ["SKILL.md has no frontmatter"]);
  assert.deepEqual(problems("---\nname: other\ndescription: d\n---\n"), ['name "other" differs from its folder "tool"']);
  assert.deepEqual(problems("---\nname: a/b\n---\n"), ['name "a/b" is not a safe skill name', "frontmatter has no description"]);
});

test("frontmatter values lose surrounding quotes", () => {
  assert.equal(frontmatter('---\nname: "tool"\n---\n').get("name"), "tool");
});

test("safe names follow SkillName.IsSafe", () => {
  for (const name of ["agent-skills-sync", "a.b", "conx"]) assert.ok(isSafeSkillName(name), name);
  for (const name of ["", ".", "..", "a/b", "a:b", "a.", "a ", "CON", "com1.txt", "LPT¹"]) assert.ok(!isSafeSkillName(name), name);
});

test("code snippets are read from inline spans and fenced lines", () => {
  assert.deepEqual(codeSnippets("Use `a`.\n```sh\nb --c\n\n```\nThen `` d`e ``."), ["b --c", "a", "d`e"]);
});

test("the real sources define each command with its flags", () => {
  const root = new URL("../../../Packages/com.hissal.agent-skills-sync/", import.meta.url);
  const program = readFileSync(new URL("Tools~/AgentSkillsSync.Cli/Program.cs", root), "utf8");
  const pipelineDir = new URL("Pipeline/", root);
  const pipeline = readdirSync(pipelineDir).filter((n) => n.endsWith(".cs")).map((n) => readFileSync(new URL(n, pipelineDir), "utf8"));
  const defined = definedNames(program, pipeline);
  assert.deepEqual([...defined.commands.keys()].sort(), ["hook", "import", "restore", "scope", "skills_restore", "skills_sync", "sync"]);
  assert.deepEqual([...defined.commands.get("import").flags], ["--project"]);
  assert.deepEqual([...defined.commands.get("scope").flags].sort(), ["--project", "--shared", "--worktree"]);
  assert.deepEqual([...defined.commands.get("skills_restore").flags].sort(), ["--baseline", "--consent_new_sources", "--dry_run"]);
});
