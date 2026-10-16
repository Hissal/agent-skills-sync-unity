// Pure checks for the package-skills workflow: no file system. main.mjs does the I/O.
//
// A shipped skill must parse, carry a safe name matching its folder, and name only commands, subcommands and flags that
// exist, each flag on a command that accepts it: the standalone command's (its usage text and option parsing in
// Program.cs) and the Pipeline commands' (`CliCommand` and the `CliArg`s of its method). The check is lexical; what the
// skill says about a command is left to review (CODING_STANDARDS.md, "Claims from the code").

// SkillName.IsSafe's rule: one folder name that is valid on Windows, which the package applies everywhere.
const UNSAFE_CHARS = /[/\\:*?"<>|\u0000-\u001f]/;
const DEVICE = /^(con|prn|aux|nul|(com|lpt)[1-9¹²³])$/i;

const FLAG = /(?<![\w-])--[a-z][a-z_-]*/g;

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

// What the package's sources define: each command, standalone or Pipeline, with its subcommands and the flags it
// accepts, and every flag of any command.
export function definedNames(programSource, pipelineSources) {
  const usage = /const string Usage =([\s\S]*?);/.exec(programSource);
  const usageText = usage ? [...usage[1].matchAll(/"((?:[^"\\]|\\.)*)"/g)].map((m) => m[1]).join("").replace(/\\n/g, "\n") : "";
  const commands = new Map();
  for (const [, command, rest] of usageText.matchAll(/^ {2}([a-z]+)\b(.*)$/gm)) {
    // Subcommands are the bare words before the first option: `hook install | status | uninstall`.
    const subcommands = new Set(rest.split(/[[-]/)[0].split(/[\s|]+/).filter((word) => /^[a-z]+$/.test(word)));
    commands.set(command, { subcommands, flags: new Set() });
  }
  // The parser's `case "--flag" when options.Command == "a" || ...:`; a case without `when` is every command's.
  for (const [, flag, condition = ""] of programSource.matchAll(/case "(--[a-z][a-z-]*)"(?: when ([^:]+))?:/g)) {
    const owners = [...condition.matchAll(/options\.Command == "(\w+)"/g)].map((m) => m[1]);
    for (const [command, entry] of commands) if (owners.length === 0 || owners.includes(command)) entry.flags.add(flag);
  }
  // Each `[CliCommand("name", ...)]` owns the `CliArg`s up to the next one.
  for (const source of pipelineSources) {
    for (const chunk of source.split(/(?=\[CliCommand\()/).filter((part) => part.startsWith("[CliCommand("))) {
      const name = /^\[CliCommand\("(\w+)"/.exec(chunk)[1];
      const flags = new Set([...chunk.matchAll(/CliArg\("(\w+)"/g)].map((m) => `--${m[1]}`));
      commands.set(name, { subcommands: new Set(), flags, pipeline: true });
    }
  }
  const flags = new Set([...commands.values()].flatMap((entry) => [...entry.flags]));
  return { commands, flags };
}

// Each inline code span and each fenced code line, in order.
export function codeSnippets(markdown) {
  const snippets = [];
  const prose = markdown.replace(/^```[^\n]*\n([\s\S]*?)^```/gm, (_, block) => {
    snippets.push(...block.split("\n").filter((line) => line.trim()));
    return "";
  });
  snippets.push(...inlineCode(prose));
  return snippets;
}

function inlineCode(text) {
  return [...text.matchAll(/(`+)([\s\S]+?)(?<!`)\1(?!`)/g)].map((match) => match[2].trim());
}

// The body rows of each table whose first header cell is "Command", as cells with `\|` unescaped.
export function commandRows(markdown) {
  const rows = [];
  let header = null;
  for (const line of markdown.split(/\r?\n/)) {
    if (!line.startsWith("|")) {
      header = null;
      continue;
    }
    const cells = line.replace(/^\||\|$/g, "").split(/(?<!\\)\|/).map((cell) => cell.trim().replace(/\\\|/g, "|"));
    if (header === null) header = cells[0];
    else if (header === "Command" && !/^:?-+:?$/.test(cells[0])) rows.push(cells);
  }
  return rows;
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
    for (const flag of new Set(markdown.match(FLAG) ?? [])) {
      if (!defined.flags.has(flag)) problems.push(`${file}: unknown flag ${flag}`);
    }
    const report = (problem, snippet) => problem && problems.push(`${file}: ${problem} in \`${snippet}\``);
    for (const snippet of codeSnippets(markdown)) {
      const command = invocation(snippet);
      if (command) report(commandProblem(command, defined), snippet);
    }
    // A command table row names its command in the first cell; the flags in its other cells are that command's.
    for (const [first, ...rest] of commandRows(markdown)) {
      const [command] = inlineCode(first);
      if (!command) continue;
      const flags = rest.flatMap((cell) => cell.match(FLAG) ?? []).join(" ");
      report(commandProblem(`${command} ${flags}`.trim(), defined), command);
    }
  }
  return problems;
}

// The command a snippet runs, without its `dotnet <dll> ` or `unity command ` prefix, or null when it runs none. A
// snippet runs a command when it has that prefix or a flag follows its first word. Placeholders (`<name>`) are skipped.
function invocation(snippet) {
  const prefixed = /^(dotnet <dll> |unity command )/.test(snippet);
  const command = snippet.replace(/^(dotnet <dll> |unity command )/, "");
  const words = command.split(/\s+/);
  if (/^[-<]/.test(words[0])) return null;
  return prefixed || words.slice(1).some((word) => word.startsWith("--")) ? command : null;
}

// Its first word must be a command; the bare words before its first option must be that command's subcommands (one
// at least, when it has any); and each flag must be one the command accepts.
function commandProblem(command, { commands }) {
  const words = command.split(/\s+/);
  const entry = commands.get(words[0]);
  if (!entry) return `unknown command ${words[0]}`;
  const leading = [];
  for (const word of words.slice(1)) {
    if (/^[-[<]/.test(word)) break;
    if (word !== "|") leading.push(word);
  }
  if (entry.subcommands.size > 0) {
    if (leading.length === 0) return `${words[0]} without a subcommand`;
    const unknown = leading.find((word) => !entry.subcommands.has(word));
    if (unknown) return `unknown ${words[0]} subcommand ${unknown}`;
  }
  const flag = (command.match(FLAG) ?? []).find((f) => !entry.flags.has(f));
  return flag ? `${words[0]} does not accept ${flag}` : null;
}
