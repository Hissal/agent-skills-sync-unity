// Prints the computedHash the real `skills` CLI writes for every fixture folder here.
//
// For each fixture it makes a throwaway project, runs `npx skills@<version> add <fixture> -y`
// (a local source, project scope), and reads `computedHash` back from that project's
// skills-lock.json. Paste the printed hashes into SkillFolderHashTests.cs.
//
// Usage: node generate-hashes.mjs [skills-version]   (default 1.7.0; needs Node + network for npx)
// Run it with an English (or other non-tailored) locale: the CLI sorts paths with localeCompare.

import { execFileSync } from "node:child_process";
import { mkdtempSync, readdirSync, readFileSync, rmSync, statSync } from "node:fs";
import { tmpdir } from "node:os";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const version = process.argv[2] ?? "1.7.0";
const here = dirname(fileURLToPath(import.meta.url));
const fixtures = readdirSync(here).filter((name) => statSync(join(here, name)).isDirectory()).sort();

console.log(`skills@${version}, locale ${Intl.Collator().resolvedOptions().locale}`);
for (const name of fixtures) {
	const project = mkdtempSync(join(tmpdir(), "skills-hash-"));
	try {
		execFileSync("npx", ["-y", `skills@${version}`, "add", join(here, name), "-y", "--agent", "codex", "--copy"], {
			cwd: project,
			stdio: "ignore",
			shell: process.platform === "win32",
			env: { ...process.env, DISABLE_TELEMETRY: "1", DO_NOT_TRACK: "1" },
		});
		const lock = JSON.parse(readFileSync(join(project, "skills-lock.json"), "utf8"));
		console.log(`${name} ${lock.skills[name].computedHash}`);
	} finally {
		rmSync(project, { recursive: true, force: true });
	}
}
