import { test } from "node:test";
import assert from "node:assert/strict";
import { readdirSync, readFileSync } from "node:fs";
import { codeSnippets, definedNames, frontmatter, isSafeSkillName, skillProblems } from "./check.mjs";

const PROGRAM = `
  const string Usage =
      "Usage: agent-skills-sync <command> [--project <unity-project>] [options]\\n" +
      "  restore [--dry-run] [--confirm-source <owner/repo>]...\\n" +
      "      Install the selected skills.\\n" +
      "  sync [--install-mode latest|pinned]\\n" +
      "  hook install | status | uninstall\\n" +
      "--project defaults to the current directory.";
  case "--hook-output" when options.Command == "restore": break;
`;
const PIPELINE = `[CliCommand("skills_restore", "Restore.")] public static object Run([CliArg("dry_run", "Dry.")] bool d) {}`;
const DEFINED = definedNames(PROGRAM, [PIPELINE]);
const HEADER = "---\nname: tool\ndescription: Does things.\n---\n\n";

function problems(body, folder = "tool") {
  return skillProblems(folder, new Map([["SKILL.md", body]]), DEFINED);
}

test("commands, subcommands and flags are read from the usage text, the parser and the Pipeline", () => {
  assert.deepEqual([...DEFINED.commands.keys()], ["restore", "sync", "hook"]);
  assert.deepEqual([...DEFINED.commands.get("hook")], ["install", "status", "uninstall"]);
  assert.deepEqual([...DEFINED.commands.get("sync")], []);
  assert.ok(DEFINED.flags.has("--hook-output") && DEFINED.flags.has("--dry_run") && DEFINED.flags.has("--install-mode"));
  assert.deepEqual([...DEFINED.pipelineCommands], ["skills_restore"]);
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

test("snippets without a flag after their first word are not commands", () => {
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

test("the real sources define the standalone and Pipeline commands", () => {
  const root = new URL("../../../Packages/com.hissal.agent-skills-sync/", import.meta.url);
  const program = readFileSync(new URL("Tools~/AgentSkillsSync.Cli/Program.cs", root), "utf8");
  const pipelineDir = new URL("Pipeline/", root);
  const pipeline = readdirSync(pipelineDir).filter((n) => n.endsWith(".cs")).map((n) => readFileSync(new URL(n, pipelineDir), "utf8"));
  const defined = definedNames(program, pipeline);
  assert.deepEqual([...defined.commands.keys()].sort(), ["hook", "import", "restore", "scope", "sync"]);
  assert.deepEqual([...defined.pipelineCommands].sort(), ["skills_restore", "skills_sync"]);
});
