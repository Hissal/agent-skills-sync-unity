// Checks that every C# name in the code map still occurs in the package source (see names.mjs for what that does and
// doesn't prove). Run from the repo root: node .github/scripts/code-map/main.mjs [map] [package folder]
// Exits 0 when every name resolves, 1 when some don't, and 2 when an input is missing or unreadable.
import { readdirSync, readFileSync } from "node:fs";
import { join } from "node:path";
import { documentedNames, missingNames, sourceWords } from "./names.mjs";

const [mapPath = "docs/code-map.md", packageDir = "Packages/com.hissal.agent-skills-sync"] = process.argv.slice(2);

function fail(message) {
  console.error(`code map check: ${message}`);
  process.exit(2);
}

let markdown;
try {
  markdown = readFileSync(mapPath, "utf8");
} catch (error) {
  fail(`can't read the code map ${mapPath}: ${error.message}`);
}

let sources;
try {
  sources = readdirSync(packageDir, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile() && entry.name.endsWith(".cs"))
    .map((entry) => readFileSync(join(entry.parentPath, entry.name), "utf8"));
} catch (error) {
  fail(`can't read the package source in ${packageDir}: ${error.message}`);
}
if (sources.length === 0) fail(`no .cs files in ${packageDir}`);

const names = documentedNames(markdown);
if (names.size === 0) fail(`no C# names in inline code in ${mapPath}`);

const missing = missingNames(names, sourceWords(sources));
if (missing.length > 0) {
  console.error(`${mapPath}: not found in ${packageDir}:`);
  for (const { name, segment } of missing) {
    console.error(name === segment ? `  \`${name}\`` : `  \`${name}\`: \`${segment}\``);
  }
  console.error("Rename them in the map, or add an external name to EXCEPTIONS in names.mjs with its reason.");
  process.exit(1);
}
console.log(`${mapPath}: all ${names.size} names found in ${packageDir}.`);
