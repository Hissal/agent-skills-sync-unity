// Prints the computedHash the real `skills` CLI writes for every fixture folder here, and for
// every tree in generated-trees.json (built in a temp folder first).
//
// For each skill folder it makes a throwaway project, runs `npx skills@<version> add <folder> -y`
// (a local source, project scope), and reads `computedHash` and the lock's `version` back from
// that project's skills-lock.json. Paste the printed hashes into SkillFolderHashTests.cs.
//
// Usage: node generate-hashes.mjs [skills-version]   (default 1.7.0; needs Node + network for npx)
// 1.7.x writes lock version 1, 1.8.x lock version 2.
// Run it with an English (or other non-tailored) locale: version 1 sorts paths with localeCompare.
// Generated trees with symlinks need a file system and account that can create them.

import { execFileSync } from "node:child_process";
import { cpSync, mkdirSync, mkdtempSync, readdirSync, readFileSync, rmSync, statSync, symlinkSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const version = process.argv[2] ?? "1.7.0";
const here = dirname(fileURLToPath(import.meta.url));
const fixtures = readdirSync(here).filter((name) => statSync(join(here, name)).isDirectory()).sort();
const generated = JSON.parse(readFileSync(join(here, "generated-trees.json"), "utf8")).trees;

// The lock's only entry, named by SKILL.md's frontmatter (a generated tree keeps its base's name).
function lockedHash(folder) {
	const project = mkdtempSync(join(tmpdir(), "skills-hash-"));
	try {
		execFileSync("npx", ["-y", `skills@${version}`, "add", folder, "-y", "--agent", "codex", "--copy"], {
			cwd: project,
			stdio: "ignore",
			shell: process.platform === "win32",
			env: { ...process.env, DISABLE_TELEMETRY: "1", DO_NOT_TRACK: "1" },
		});
		const lock = JSON.parse(readFileSync(join(project, "skills-lock.json"), "utf8"));
		const entries = Object.values(lock.skills);
		if (entries.length !== 1) throw new Error(`expected one lock entry for ${folder}, got ${entries.length}`);
		return `${entries[0].computedHash} (lock version ${lock.version})`;
	} finally {
		rmSync(project, { recursive: true, force: true });
	}
}

// Builds a generated tree as <temp>/<name>, with its outside files in <temp>.
function build(name, tree) {
	const parent = mkdtempSync(join(tmpdir(), "skills-tree-"));
	const skill = join(parent, name);
	cpSync(join(here, tree.base), skill, { recursive: true });
	const write = (root, files) => {
		for (const [path, contents] of Object.entries(files ?? {})) {
			mkdirSync(dirname(join(root, path)), { recursive: true });
			writeFileSync(join(root, path), contents, "utf8");
		}
	};
	write(skill, tree.files);
	write(parent, tree.outside);
	for (const dir of tree.dirs ?? []) mkdirSync(join(skill, dir), { recursive: true });
	for (const [path, target] of Object.entries(tree.links ?? {})) symlinkSync(target, join(skill, path));
	return { parent, skill };
}

console.log(`skills@${version}, locale ${Intl.Collator().resolvedOptions().locale}`);
for (const name of fixtures) console.log(`${name} ${lockedHash(join(here, name))}`);
for (const [name, tree] of Object.entries(generated)) {
	const { parent, skill } = build(name, tree);
	try {
		console.log(`${name} ${lockedHash(skill)}`);
	} finally {
		rmSync(parent, { recursive: true, force: true });
	}
}
