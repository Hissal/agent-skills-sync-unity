// Checks every skill the package ships (see check.mjs for what that does and doesn't prove).
// Run from the repo root: node .github/scripts/package-skills/main.mjs [package folder]
// Exits 0 when every skill passes, 1 when one doesn't, and 2 when an input is missing or unreadable.
import { existsSync, readdirSync, readFileSync } from "node:fs";
import { join, relative } from "node:path";
import { definedNames, skillProblems } from "./check.mjs";

const [packageDir = "Packages/com.hissal.agent-skills-sync"] = process.argv.slice(2);

function fail(message) {
  console.error(`package skills check: ${message}`);
  process.exit(2);
}

function read(path) {
  try {
    return readFileSync(path, "utf8");
  } catch (error) {
    fail(`can't read ${path}: ${error.message}`);
  }
}

const program = read(join(packageDir, "Tools~", "AgentSkillsSync.Cli", "Program.cs"));
const pipelineDir = join(packageDir, "Pipeline");
const pipeline = readdirSync(pipelineDir).filter((name) => name.endsWith(".cs")).map((name) => read(join(pipelineDir, name)));
const defined = definedNames(program, pipeline);
const entries = [...defined.commands.values()];
if (!entries.some((entry) => !entry.pipeline)) fail("no commands in the standalone command's usage text");
if (!entries.some((entry) => entry.pipeline)) fail(`no CliCommand in ${pipelineDir}`);

const skillFolders = [".claude/skills", ".agents/skills"]
  .map((folder) => join(packageDir, folder))
  .filter(existsSync)
  .flatMap((root) => readdirSync(root, { withFileTypes: true }).filter((e) => e.isDirectory()).map((e) => join(root, e.name)));
if (skillFolders.length === 0) fail(`no skills in ${packageDir}/.claude/skills or .agents/skills`);

let failed = false;
for (const folder of skillFolders) {
  const files = new Map(
    readdirSync(folder, { recursive: true, withFileTypes: true })
      .filter((entry) => entry.isFile() && entry.name.endsWith(".md"))
      .map((entry) => {
        const path = join(entry.parentPath, entry.name);
        return [relative(folder, path).replaceAll("\\", "/"), read(path)];
      }),
  );
  const problems = skillProblems(folder.split(/[\\/]/).pop(), files, defined);
  if (problems.length === 0) {
    console.log(`${folder}: ok`);
    continue;
  }
  failed = true;
  console.error(`${folder}:`);
  for (const problem of problems) console.error(`  ${problem}`);
}
if (failed) {
  console.error("Fix the skill, or the command it names, so they agree.");
  process.exit(1);
}
