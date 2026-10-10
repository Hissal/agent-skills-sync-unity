// Pure name checking for the code-map workflow: no file system. main.mjs does the I/O.
//
// The check is lexical: each segment of a documented name must occur as a whole, case-sensitive word somewhere in
// the package's C# source. For `SkillSync.Plan` it finds both `SkillSync` and `Plan`, but not that `Plan` is a
// member of `SkillSync`, and a word in a comment or string counts. Member ownership and behaviour claims are left
// to review (CODING_STANDARDS.md, "Claims from the code").

// A C# name, optionally dotted: `SkillSync`, `SkillSync.Plan`. Other inline code (paths, files, wildcards) is skipped.
const IDENTIFIER = /^[A-Za-z_][A-Za-z0-9_]*(\.[A-Za-z_][A-Za-z0-9_]*)*$/;

// Identifier-shaped names the map uses that aren't the package's own. Each matches a whole documented name, before
// it is split into segments; any other name the source doesn't contain fails the check.
export const EXCEPTIONS = new Map([
  ["skills", "the skills CLI, which writes skills-lock.json"],
  ["com.unity.pipeline", "the Unity package ID that enables the Pipeline command"],
  ["skills_sync", "the Pipeline command's name, registered as a string"],
]);

// The contents of each inline code span, in order.
export function inlineCode(markdown) {
  return [...markdown.matchAll(/(`+)([\s\S]+?)(?<!`)\1(?!`)/g)].map((match) => match[2].trim());
}

// The distinct identifier-shaped names in the map's inline code, exceptions included.
export function documentedNames(markdown) {
  return new Set(inlineCode(markdown).filter((token) => IDENTIFIER.test(token)));
}

// Every whole word in the source: a run of identifier characters, so `SkillSyncTests` doesn't hold `SkillSync`.
export function sourceWords(sources) {
  const words = new Set();
  for (const source of sources) for (const [word] of source.matchAll(/[A-Za-z0-9_]+/g)) words.add(word);
  return words;
}

// Each name, other than an exception, with a segment the source lacks, as { name, segment }.
export function missingNames(names, words, exceptions = EXCEPTIONS) {
  return [...names]
    .filter((name) => !exceptions.has(name))
    .flatMap((name) => name.split(".").filter((segment) => !words.has(segment)).map((segment) => ({ name, segment })));
}
