// Pure planning for the sync-skills workflow: no network, git or file system. main.mjs does the I/O.

// The subset of YAML that SKILL.md frontmatter uses: top-level scalars (plain, quoted, folded or literal,
// possibly spanning indented lines) and one level of nested `key: value` maps.
export function parseFrontmatter(skillMd) {
  const lines = skillMd.replace(/\r\n?/g, "\n").split("\n");
  if (lines[0].trim() !== "---") return {};
  const end = lines.findIndex((line, index) => index > 0 && line.trim() === "---");
  const body = lines.slice(1, end === -1 ? lines.length : end);

  const result = {};
  let i = 0;
  while (i < body.length) {
    const match = /^([A-Za-z0-9_.-]+):\s*(.*)$/.exec(body[i]);
    i++;
    if (!match) continue;
    const [, key, rawValue] = match;
    const indented = [];
    while (i < body.length && (body[i].trim() === "" || /^\s/.test(body[i]))) indented.push(body[i++]);

    const value = rawValue.trim();
    if (/^[>|][+-]?$/.test(value)) {
      const text = indented.map((line) => line.trim());
      result[key] = (value[0] === ">" ? text.join(" ").replace(/ {2,}/g, " ") : text.join("\n")).trim();
    } else if (value === "" && indented.some((line) => /^\s+[^\s:]+:\s/.test(line))) {
      result[key] = {};
      for (const line of indented) {
        const entry = /^\s+([^\s:]+):\s*(.*)$/.exec(line);
        if (entry) result[key][entry[1]] = unquote(entry[2].trim());
      }
    } else {
      result[key] = unquote([value, ...indented.map((line) => line.trim())].filter(Boolean).join(" "));
    }
  }
  return result;
}

function unquote(value) {
  if (value.length >= 2 && value[0] === value.at(-1) && (value[0] === '"' || value[0] === "'")) {
    return value.slice(1, -1);
  }
  return value;
}

// `required_editor_version` and `required_packages` the project doesn't meet, as "needs" notes for a proposal.
// project: { editorVersion: "6000.3.20f1", packages: { "<id>": "<resolved version>" } }.
export function unmetRequirements(frontmatter, project) {
  const unmet = [];
  const editor = frontmatter.required_editor_version;
  if (editor && !satisfies(project.editorVersion, editor)) {
    unmet.push(`Unity ${editor} (this project is on ${project.editorVersion})`);
  }
  const packages = frontmatter.required_packages;
  if (packages && typeof packages === "object") {
    for (const [id, range] of Object.entries(packages)) {
      const installed = project.packages[id];
      if (installed === undefined) unmet.push(`${id} ${range} (not installed)`);
      else if (!satisfies(installed, range)) unmet.push(`${id} ${range} (this project has ${installed})`);
    }
  }
  return unmet;
}

// A single comparison such as ">=6000.6" or "17.5.0" (a bare version is a minimum). Versions compare by their
// leading numeric parts, so "6000.3.20f1" is 6000.3.20. A version that isn't numeric (a `file:` package) passes.
function satisfies(version, range) {
  const match = /^\s*(>=|<=|>|<|==?)?\s*v?([\d.]+)/.exec(String(range));
  if (!match) return false;
  const actual = numericParts(version);
  if (actual === null) return true;
  const order = compareParts(actual, numericParts(match[2]));
  switch (match[1] ?? ">=") {
    case ">=": return order >= 0;
    case ">": return order > 0;
    case "<=": return order <= 0;
    case "<": return order < 0;
    default: return order === 0;
  }
}

function numericParts(version) {
  const match = /^\s*v?(\d+(?:\.\d+)*)/.exec(String(version));
  return match ? match[1].split(".").map(Number) : null;
}

function compareParts(a, b) {
  for (let i = 0; i < Math.max(a.length, b.length); i++) {
    const difference = (a[i] ?? 0) - (b[i] ?? 0);
    if (difference !== 0) return Math.sign(difference);
  }
  return 0;
}

export const PROPOSAL_LABEL = "skill-proposal";
export const ACCEPTED_LABEL = "skill-accepted";

const TITLE_PATTERN = /^chore\(skills\): adopt (\S+) from ([^\s/]+\/[^\s/]+)$/;
const MARKER_PATTERN = /<!-- skill-proposal source="([^"]+)" skill="([^"]+)" -->/;

// The issue that proposes adopting an upstream skill. The hidden marker keeps it recognisable after its title is edited.
export function proposalIssue({ source, ref, skill, project }) {
  const lines = [];
  const description = skill.frontmatter.description;
  if (description) lines.push(...description.split("\n").map((line) => `> ${line}`), "");
  for (const need of unmetRequirements(skill.frontmatter, project)) lines.push(`Needs ${need}.`, "");
  lines.push(
    `[\`${skill.skillPath}\`](https://github.com/${source}/blob/${ref}/${skill.skillPath})`,
    "",
    `Accept: add the \`${ACCEPTED_LABEL}\` label. Reject: close this issue as not planned, with the reason as the closing comment.`,
    "",
    `<!-- skill-proposal source="${source}" skill="${skill.name}" -->`,
  );
  return {
    title: `chore(skills): adopt ${skill.name} from ${source}`,
    body: lines.join("\n") + "\n",
    labels: ["needs-triage", PROPOSAL_LABEL],
  };
}

// { source, skill } for a proposal issue, or null when the issue isn't one.
export function parseProposal(issue) {
  const marker = MARKER_PATTERN.exec(issue.body ?? "");
  if (marker) return { source: marker[1], skill: marker[2] };
  const title = TITLE_PATTERN.exec(issue.title.trim());
  return title ? { source: title[2], skill: title[1] } : null;
}

// GitHub's "Close with comment" posts the comment, then closes: take the closer's last comment from just before
// the close, flattened to one line. Without one, the reason is the issue's URL.
const CLOSING_COMMENT_WINDOW_MS = 5 * 60 * 1000;

export function rejectionReason(issue, comments) {
  const closedAt = Date.parse(issue.closed_at);
  const closing = comments
    .filter((comment) => comment.user?.login === issue.closed_by?.login)
    .filter((comment) => {
      const age = closedAt - Date.parse(comment.created_at);
      return age >= 0 && age <= CLOSING_COMMENT_WINDOW_MS;
    })
    .at(-1);
  const reason = closing?.body?.replace(/\s+/g, " ").trim();
  return reason || issue.html_url;
}

// Every GitHub source with a locked skill is watched, at each ref it's locked at (none: its default branch).
export function watchedSources(lock) {
  const watched = new Map();
  for (const entry of Object.values(lock.skills ?? {})) {
    if (entry.sourceType !== "github") continue;
    const key = watchKey(entry);
    if (!watched.has(key)) watched.set(key, { key, source: entry.source, ref: entry.ref || undefined });
  }
  return [...watched.values()];
}

// Where an entry's upstream inventory is kept: "<owner>/<repo>", or "<owner>/<repo>#<ref>" when it's locked at a ref.
export function watchKey(entry) {
  return entry.ref ? `${entry.source}#${entry.ref}` : entry.source;
}

// What a run changes, from main's lock and rejection list, each watched source's skills
// (upstream: { [watchKey]: { source, ref, pinned, skills: [{ name, skillPath, frontmatter }] } }) and every
// skill-proposal issue. Accepting adds from the default branch, so a ref-pinned inventory is never proposed from.
//   removed: locked skills that no longer exist upstream; they leave the lock.
//   accept:  open proposals labelled skill-accepted, for skills that exist upstream and aren't locked yet.
//   reject:  proposals closed as not planned, for skills not yet in the rejection list ({ issue, source, name }).
//   propose: upstream skills that are neither locked nor rejected and have no open proposal.
export function planSync({ lock, rejected, upstream, issues }) {
  // Lock entries are keyed by name, so a name locked from any source is taken.
  const isLocked = (name) => name in lock.skills;
  const findUpstream = (source, name) => upstream[source]?.skills.find((skill) => skill.name === name);

  const removed = [];
  for (const [name, entry] of Object.entries(lock.skills)) {
    const skills = upstream[watchKey(entry)]?.skills;
    if (entry.sourceType !== "github" || !skills) continue;
    if (skills.some((skill) => skill.name === name || skill.skillPath === entry.skillPath)) continue;
    removed.push({ name, source: entry.source, skillPath: entry.skillPath });
  }

  const proposals = issues
    .map((issue) => ({ issue, proposal: parseProposal(issue) }))
    .filter(({ proposal }) => proposal !== null);

  const accept = [];
  const reject = [];
  const isRejected = (source, name) =>
    rejected.sources[source]?.[name] !== undefined || reject.some((r) => r.source === source && r.name === name);
  const openProposals = new Set();
  for (const { issue, proposal: { source, skill } } of proposals) {
    if (issue.state === "open") {
      openProposals.add(`${source}\n${skill}`);
      const upstreamSkill = findUpstream(source, skill);
      const isAccepted = issue.labels.some((label) => label.name === ACCEPTED_LABEL);
      if (isAccepted && upstreamSkill && !isLocked(skill)) accept.push({ issue, source, skill: upstreamSkill });
    } else if (issue.state_reason === "not_planned" && !isRejected(source, skill)) {
      reject.push({ issue, source, name: skill });
    }
  }

  const propose = [];
  for (const { source, pinned, skills } of Object.values(upstream)) {
    if (pinned) continue;
    for (const skill of skills) {
      if (isLocked(skill.name) || isRejected(source, skill.name)) continue;
      if (openProposals.has(`${source}\n${skill.name}`)) continue;
      propose.push({ source, skill });
    }
  }

  return { removed, accept, reject, propose };
}

// A copy of skills-rejected.json with the rejections added, each source's skills in name order.
export function withRejections(rejected, rejections) {
  const sources = structuredClone(rejected.sources);
  for (const { source, name, reason } of rejections) (sources[source] ??= {})[name] = reason;
  for (const source of Object.keys(sources)) {
    sources[source] = Object.fromEntries(Object.entries(sources[source]).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0)));
  }
  return { ...rejected, sources };
}

const LISTED_COMMITS = 10;

// The rolling PR's body, one section per kind of change and none for an empty one; null when nothing changed.
//   updated:  [{ name, source, commits: [{ sha, html_url, message }], historyUrl }]
//   added:    [{ name, source, issue }]
//   rejected: [{ name, source, issue, reason }]
//   removed:  [{ name, source, skillPath }]
export function prBody({ updated, added, rejected, removed }) {
  const sections = [];
  const section = (heading, items, render) => {
    if (items.length > 0) sections.push([`## ${heading}`, "", ...items.flatMap(render)].join("\n"));
  };

  section("Updated", updated, ({ name, source, commits, historyUrl }) => {
    const lines = [`- \`${name}\` from ${source}`];
    for (const commit of commits.slice(0, LISTED_COMMITS)) {
      lines.push(`  - [\`${commit.sha.slice(0, 7)}\`](${commit.html_url}) ${commit.message.split("\n")[0]}`);
    }
    if (commits.length > LISTED_COMMITS) {
      lines.push(`  - ${commits.length - LISTED_COMMITS} more in the [history](${historyUrl})`);
    } else if (commits.length === 0) {
      lines.push(`  - See the [history](${historyUrl}).`);
    }
    return lines;
  });
  section("Added", added, ({ name, source, issue }) => [`- \`${name}\` from ${source}. Closes #${issue}`]);
  section("Rejected", rejected, ({ name, source, issue, reason }) => [`- \`${name}\` from ${source}: ${reason} Closes #${issue}`]);
  section("Removed upstream", removed, ({ name, source, skillPath }) => [
    `- \`${name}\` from ${source}: \`${(skillPath ?? name).replace(/\/?SKILL\.md$/, "")}\` no longer exists upstream.`,
  ]);

  return sections.length > 0 ? sections.join("\n\n") + "\n" : null;
}

const IGNORE_BEGIN = "# >>> Agent Skills Sync: managed from skills-lock.json; do not edit this block.";
const IGNORE_INTRO = "# Lists every skill the tool may install or link here, on any machine. Anything else stays tracked.";
const IGNORE_END = "# <<< Agent Skills Sync";

// A skills folder's .gitignore with the package's managed block listing `names`, as ManagedStateFile.Write writes it.
// Only an existing block is rewritten: the package decides which folders get one.
export function withIgnoreBlock(text, names) {
  const lines = text.split("\n");
  const start = lines.findIndex((line) => line.trim() === IGNORE_BEGIN);
  if (start === -1) return text;
  const endLine = lines.findIndex((line, index) => index > start && line.trim() === IGNORE_END);
  const end = endLine === -1 ? lines.length : endLine + 1;
  const sorted = [...names].sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  const block = [IGNORE_BEGIN, IGNORE_INTRO, ...sorted.map((name) => `/${name}`), IGNORE_END];
  const after = lines.slice(end);
  return [...lines.slice(0, start), ...block, ...(after.length > 0 ? after : [""])].join("\n");
}
