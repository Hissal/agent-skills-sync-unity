// Syncs skills-lock.json with its upstream sources on one rolling PR, and opens an issue proposing each upstream
// skill nobody has accepted or rejected yet. Run from the repo root on the default branch, with GH_TOKEN set.
// DRY_RUN=true still runs the skills CLI and edits the files locally, but prints instead of writing to GitHub.
import { execFileSync, spawnSync } from "node:child_process";
import { existsSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { posix } from "node:path";
import {
  ACCEPTED_LABEL,
  PROPOSAL_LABEL,
  parseFrontmatter,
  planSync,
  prBody,
  proposalIssue,
  rejectionReason,
  watchedSources,
  withRejections,
} from "./plan.mjs";

const SKILLS_CLI = "skills@1.7.1";
const BRANCH = "chore/sync-skills";
const TITLE = "chore(skills): sync skills with upstream";
const LOCK = "skills-lock.json";
const REJECTED = "skills-rejected.json";
const INSTALL_DIRS = [".agents/skills", ".claude/skills"];
// Folders the skills CLI never discovers skills in.
const SKIPPED_DIRS = new Set(["node_modules", ".git", "dist", "build", "__pycache__"]);

const dryRun = process.env.DRY_RUN === "true";
const repo = process.env.GITHUB_REPOSITORY ?? gh(["repo", "view", "--json", "nameWithOwner", "--jq", ".nameWithOwner"]).trim();
// The rolling PR always targets the default branch, so a real run has to start from it.
const base = ghJson(`repos/${repo}`).default_branch;
if (!dryRun && git(["rev-parse", "--abbrev-ref", "HEAD"]).trim() !== base) {
  throw new Error(`Run from ${base}, or set DRY_RUN=true.`);
}

const lock = readJson(LOCK);
const rejected = existsSync(REJECTED) ? readJson(REJECTED) : { version: 1, sources: {} };
const upstream = Object.fromEntries(watchedSources(lock).map((source) => [source, listUpstream(source)]));
const issues = ghList(`repos/${repo}/issues?labels=${PROPOSAL_LABEL}&state=all&per_page=100`)
  .filter((issue) => !issue.pull_request);
const plan = planSync({ lock, rejected, upstream, issues });

// A skill missing upstream aborts every sync, so it leaves the lock before the CLI runs.
const keptLock = structuredClone(lock);
for (const { name } of plan.removed) delete keptLock.skills[name];
if (plan.removed.length > 0) writeJson(LOCK, keptLock);

const githubSkills = Object.keys(keptLock.skills).filter((name) => keptLock.skills[name].sourceType === "github");
if (githubSkills.length > 0) skillsCli(["update", "--project", "--yes", ...githubSkills]);
const bySource = Map.groupBy(plan.accept, (accepted) => accepted.source);
for (const [source, accepted] of bySource) {
  skillsCli(["add", source, "--skill", ...accepted.map(({ skill }) => skill.name), "--yes"]);
}

const syncedLock = readJson(LOCK);
for (const { source, skill } of plan.accept) {
  if (syncedLock.skills[skill.name]?.source !== source) throw new Error(`${skill.name} from ${source} was not added to ${LOCK}.`);
}
const updated = githubSkills
  .filter((name) => syncedLock.skills[name] && syncedLock.skills[name].computedHash !== keptLock.skills[name].computedHash)
  .map((name) => upstreamChanges(name, keptLock.skills[name]));

const rejections = plan.reject.map(({ issue, source, name }) => {
  const closed = ghJson(`repos/${repo}/issues/${issue.number}`);
  const comments = ghList(`repos/${repo}/issues/${issue.number}/comments?per_page=100`);
  return { source, name, reason: rejectionReason(closed, comments), issue: issue.number };
});
if (rejections.length > 0) writeJson(REJECTED, withRejections(rejected, rejections));

// Only the two JSON files are committed; the installed copies go.
for (const name of new Set([...Object.keys(lock.skills), ...Object.keys(syncedLock.skills)])) {
  for (const dir of INSTALL_DIRS) rmSync(posix.join(dir, name), { recursive: true, force: true });
}

const body = prBody({
  updated,
  added: plan.accept.map(({ issue, source, skill }) => ({ name: skill.name, source, issue: issue.number })),
  rejected: rejections,
  removed: plan.removed,
});
const project = readProject();
const proposals = plan.propose.map(({ source, skill }) => proposalIssue({ source, ref: upstream[source].ref, skill, project }));

if (dryRun) {
  console.log(`\n--- ${TITLE} ---\n${body ?? "(nothing to sync)"}`);
  for (const proposal of proposals) console.log(`\n--- ${proposal.title} [${proposal.labels}] ---\n${proposal.body}`);
} else {
  ensureLabels();
  for (const { title, body, labels } of proposals) {
    ghJson(`repos/${repo}/issues`, ["--method", "POST", "-f", `title=${title}`, "-f", `body=${body}`, ...labels.flatMap((label) => ["-f", `labels[]=${label}`])]);
    console.log(`Proposed: ${title}`);
  }
  updateRollingPr(body);
}

function listUpstream(source) {
  const { default_branch: ref } = ghJson(`repos/${source}`);
  const tree = ghJson(`repos/${source}/git/trees/${encodeURIComponent(ref)}?recursive=1`);
  if (tree.truncated) throw new Error(`The file tree of ${source} is too large to list in one request.`);
  return {
    ref,
    skills: tree.tree
      .filter((entry) => entry.type === "blob" && posix.basename(entry.path) === "SKILL.md")
      .filter((entry) => !entry.path.split("/").some((segment) => SKIPPED_DIRS.has(segment)))
      .map(({ path }) => {
        const frontmatter = parseFrontmatter(gh(["api", "-H", "Accept: application/vnd.github.raw", contentsPath(source, path, ref)]));
        const folder = posix.dirname(path);
        return { name: frontmatter.name || posix.basename(folder === "." ? source : folder), skillPath: path, frontmatter };
      }),
  };
}

// The upstream commits that touched the skill's folder since its old hash entered the lock.
function upstreamChanges(name, entry) {
  const { ref } = upstream[entry.source];
  const folder = posix.dirname(entry.skillPath ?? "");
  const historyUrl = `https://github.com/${entry.source}/commits/${ref}${folder === "." ? "" : `/${folder}`}`;
  const lockedAt = git(["log", "-S", entry.computedHash, "--format=%cI", "--", LOCK]).trim().split("\n").at(-1);
  if (!lockedAt) return { name, source: entry.source, commits: [], historyUrl };
  const query = new URLSearchParams({ sha: ref, since: lockedAt, per_page: "100" });
  if (folder !== ".") query.set("path", folder);
  return { name, source: entry.source, commits: ghList(`repos/${entry.source}/commits?${query}`).map(toCommit), historyUrl };
}

function toCommit({ sha, html_url, commit }) {
  return { sha, html_url, message: commit.message };
}

function readProject() {
  const version = /m_EditorVersion:\s*(\S+)/.exec(readFileSync("ProjectSettings/ProjectVersion.txt", "utf8"))[1];
  const dependencies = readJson("Packages/packages-lock.json").dependencies;
  return {
    editorVersion: version,
    packages: Object.fromEntries(Object.entries(dependencies).map(([id, { version }]) => [id, version])),
  };
}

function ensureLabels() {
  gh(["label", "create", PROPOSAL_LABEL, "--force", "--color", "c5def5", "--description", "Proposes adopting an upstream skill"]);
  gh(["label", "create", ACCEPTED_LABEL, "--force", "--color", "0e8a16", "--description", "Adds the proposed skill on the sync PR"]);
}

function updateRollingPr(body) {
  const open = JSON.parse(gh(["pr", "list", "--head", BRANCH, "--state", "open", "--json", "number"]));
  const changed = git(["status", "--porcelain", "--", LOCK, REJECTED]).trim() !== "";
  if (!body || !changed) {
    if (open.length > 0) gh(["pr", "close", String(open[0].number), "--delete-branch", "--comment", "Nothing left to sync."]);
    console.log("Nothing to sync.");
    return;
  }

  git(["switch", "-C", BRANCH]);
  git(["add", "--", LOCK, REJECTED]);
  git(["-c", "user.name=github-actions[bot]", "-c", "user.email=41898282+github-actions[bot]@users.noreply.github.com", "commit", "-m", TITLE]);
  git(["push", "--force", "origin", BRANCH]);
  if (open.length > 0) {
    gh(["pr", "edit", String(open[0].number), "--title", TITLE, "--body-file", "-"], body);
    console.log(`Updated PR #${open[0].number}.`);
  } else {
    console.log(gh(["pr", "create", "--base", base, "--head", BRANCH, "--title", TITLE, "--body-file", "-"], body).trim());
  }
}

function skillsCli(args) {
  console.log(`> skills ${args.join(" ")}`);
  const result = spawnSync("npx", ["--yes", SKILLS_CLI, ...args], {
    stdio: "inherit",
    env: { ...process.env, DISABLE_TELEMETRY: "1" },
    shell: process.platform === "win32",
  });
  if (result.status !== 0) throw new Error(`skills ${args[0]} failed with exit code ${result.status}.`);
}

function contentsPath(source, path, ref) {
  return `repos/${source}/contents/${path.split("/").map(encodeURIComponent).join("/")}?ref=${encodeURIComponent(ref)}`;
}

function gh(args, input) {
  return execFileSync("gh", args, { encoding: "utf8", input, maxBuffer: 64 * 1024 * 1024 });
}

function ghJson(path, args = []) {
  return JSON.parse(gh(["api", path, ...args]));
}

function ghList(path) {
  return gh(["api", "--paginate", path, "--jq", ".[]"]).split("\n").filter(Boolean).map((line) => JSON.parse(line));
}

function git(args) {
  return execFileSync("git", args, { encoding: "utf8" });
}

function readJson(path) {
  return JSON.parse(readFileSync(path, "utf8"));
}

// The skills CLI's own format, so its rewrites and ours diff cleanly.
function writeJson(path, value) {
  writeFileSync(path, JSON.stringify(value, null, 2) + "\n");
}
