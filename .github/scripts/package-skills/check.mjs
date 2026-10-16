// Pure checks for the package-skills workflow: no file system. main.mjs does the I/O.
//
// A shipped skill must parse, carry a safe name matching its folder, and name only commands and flags that exist: the
// standalone command's (its usage text and option parsing in Program.cs) and the Pipeline commands' (`CliCommand` and
// `CliArg` names). The check is lexical; what the skill says about a command is left to review (CODING_STANDARDS.md,
// "Claims from the code").

// SkillName.IsSafe's rule: one folder name that is valid on Windows, which the package applies everywhere.
const UNSAFE_CHARS = /[/\\:*?"<>|\u0000-\u001f]/;
const DEVICE = /^(con|prn|aux|nul|(com|lpt)[1-9¹²³])$/i;

export function isSafeSkillName(name) {
  if (!name || name === "." || name === ".." || UNSAFE_CHARS.test(name)) return false;
  if (name.endsWith(".") || name.endsWith(" ")) return false;
  return !DEVICE.test(name.split(".")[0]);
}

// The `key: value` lines of the leading `---` block, or null when there is none. Values are taken as written, with
// surrounding quotes removed; the skill's frontmatter needs no YAML beyond that.
export function frontmatter(markdown) {
  const match = /^---\r?\n([\s\S]*?)\r?\n---\r?\n/.exec(markdown);
  if (!match) return null;
  const fields = new Map();
  for (const line of match[1].split(/\r?\n/)) {
    const field = /^([A-Za-z][\w-]*):\s*(.*)$/.exec(line);
    if (field) fields.set(field[1], field[2].trim().replace(/^(["'])(.*)\1$/, "$2"));
  }
  return fields;
}

// What the package's sources define: standalone commands with their subcommands, its flags, and the Pipeline
// commands and arguments.
export function definedNames(programSource, pipelineSources) {
  const usage = /const string Usage =([\s\S]*?);/.exec(programSource);
  const usageText = usage ? [...usage[1].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]).join("").replace(/\\n/g, "\n") : "";
  const commands = new Map();
  for (const [, command, rest] of usageText.matchAll(/^ {2}([a-z]+)\b(.*)$/gm)) {
    // Subcommands are the bare words before the first option: `hook install | status | uninstall`.
    commands.set(command, new Set(rest.split(/[[-]/)[0].split(/[\s|]+/).filter((word) => /^[a-z]+$/.test(word))));
  }
  const flags = new Set(programSource.match(/--[a-z][a-z-]*/g) ?? []);
  const pipeline = pipelineSources.join("\n");
  const pipelineCommands = new Set([...pipeline.matchAll(/CliCommand\("(\w+)"/g)].map((m) => m[1]));
  for (const [, arg] of pipeline.matchAll(/CliArg\("(\w+)"/g)) flags.add(`--${arg}`);
  return { commands, flags, pipelineCommands };
}

// Each inline code span and each fenced code line, in order.
export function codeSnippets(markdown) {
  const snippets = [];
  const prose = markdown.replace(/^```[^\n]*\n([\s\S]*?)^```/gm, (_, block) => {
    snippets.push(...block.split("\n").filter((line) => line.trim()));
    return "";
  });
  for (const match of prose.matchAll(/(`+)([\s\S]+?)(?<!`)\1(?!`)/g)) snippets.push(match[2].trim());
  return snippets;
}

// Every problem with one skill, as messages; empty when it passes.
export function skillProblems(folderName, files, defined) {
  const problems = [];
  const fields = frontmatter(files.get("SKILL.md") ?? "");
  if (!fields) problems.push("SKILL.md has no frontmatter");
  else {
    const name = fields.get("name");
    if (!name) problems.push("frontmatter has no name");
    else if (!isSafeSkillName(name)) problems.push(`name "${name}" is not a safe skill name`);
    else if (name !== folderName) problems.push(`name "${name}" differs from its folder "${folderName}"`);
    if (!fields.get("description")) problems.push("frontmatter has no description");
  }

  for (const [file, markdown] of files) {
    for (const flag of new Set(markdown.match(/(?<![\w-])--[a-z][a-z_-]*/g) ?? [])) {
      if (!defined.flags.has(flag)) problems.push(`${file}: unknown flag ${flag}`);
    }
    for (const snippet of codeSnippets(markdown)) {
      const problem = commandProblem(snippet, defined);
      if (problem) problems.push(`${file}: ${problem} in \`${snippet}\``);
    }
  }
  return problems;
}

// A snippet that runs a command, recognised by a flag after its first word: that word must be a standalone or Pipeline
// command, and a word following a command with subcommands must be one of them. Placeholders (`<name>`) are skipped.
function commandProblem(snippet, { commands, pipelineCommands }) {
  const words = snippet.replace(/^dotnet <dll> |^unity command /, "").split(/\s+/);
  if (!words.slice(1).some((word) => word.startsWith("--")) || /^[-<]/.test(words[0])) return null;
  if (pipelineCommands.has(words[0])) return null;
  const subcommands = commands.get(words[0]);
  if (!subcommands) return `unknown command ${words[0]}`;
  if (subcommands.size > 0 && !subcommands.has(words[1])) return `unknown ${words[0]} subcommand ${words[1]}`;
  return null;
}
