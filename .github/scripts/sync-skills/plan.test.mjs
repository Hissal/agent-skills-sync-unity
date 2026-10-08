import { test } from "node:test";
import assert from "node:assert/strict";
import {
  parseFrontmatter,
  parseProposal,
  planSync,
  prBody,
  proposalIssue,
  rejectionReason,
  watchedSources,
  withRejections,
  unmetRequirements,
} from "./plan.mjs";

test("frontmatter reads plain, quoted and folded values and a nested map", () => {
  const skillMd = [
    "---",
    "name: build-gtk",
    "description: >-",
    "  Builds node-based Editor tools",
    "  on Unity 6.6+.",
    'required_editor_version: ">=6000.6"',
    "required_packages:",
    '    com.unity.industry.toolkit: ">=4.0.0"',
    "    com.unity.shadergraph: '>=17.5.0'",
    "---",
    "",
    "# Body",
    "name: not frontmatter",
  ].join("\n");

  assert.deepEqual(parseFrontmatter(skillMd), {
    name: "build-gtk",
    description: "Builds node-based Editor tools on Unity 6.6+.",
    required_editor_version: ">=6000.6",
    required_packages: {
      "com.unity.industry.toolkit": ">=4.0.0",
      "com.unity.shadergraph": ">=17.5.0",
    },
  });
});

test("requirements the project doesn't meet are listed; met ones are not", () => {
  const project = {
    editorVersion: "6000.3.20f1",
    packages: { "com.unity.shadergraph": "17.0.3", "com.unity.ugui": "2.0.0" },
  };
  const frontmatter = {
    required_editor_version: ">=6000.6",
    required_packages: {
      "com.unity.shadergraph": ">=17.5.0",
      "com.unity.ugui": ">=2.0.0",
      "com.unity.services.vivox": ">=16.4.0",
    },
  };

  assert.deepEqual(unmetRequirements(frontmatter, project), [
    "Unity >=6000.6 (this project is on 6000.3.20f1)",
    "com.unity.shadergraph >=17.5.0 (this project has 17.0.3)",
    "com.unity.services.vivox >=16.4.0 (not installed)",
  ]);
  assert.deepEqual(unmetRequirements({ required_editor_version: ">=6000.3" }, project), []);
  assert.deepEqual(unmetRequirements({}, project), []);
});

test("a proposal issue names the skill, quotes its description, flags unmet needs and links its SKILL.md", () => {
  const issue = proposalIssue({
    source: "Unity-Technologies/skills",
    ref: "main",
    skill: {
      name: "build-gtk",
      skillPath: "skills/build-gtk/SKILL.md",
      frontmatter: { description: "Builds graph tools.", required_editor_version: ">=6000.6" },
    },
    project: { editorVersion: "6000.3.20f1", packages: {} },
  });

  assert.equal(issue.title, "chore(skills): adopt build-gtk from Unity-Technologies/skills");
  assert.deepEqual(issue.labels, ["needs-triage", "skill-proposal"]);
  assert.match(issue.body, /^> Builds graph tools\.$/m);
  assert.match(issue.body, /Needs Unity >=6000\.6 \(this project is on 6000\.3\.20f1\)/);
  assert.ok(issue.body.includes(
    "https://github.com/Unity-Technologies/skills/blob/main/skills/build-gtk/SKILL.md"));
  assert.match(issue.body, /`skill-accepted`/);
  assert.match(issue.body, /not planned/);
  assert.deepEqual(parseProposal(issue), { source: "Unity-Technologies/skills", skill: "build-gtk" });
});

test("a proposal whose body was rewritten is still recognised by its title", () => {
  const issue = { title: "chore(skills): adopt ui from Unity-Technologies/skills", body: "edited" };
  assert.deepEqual(parseProposal(issue), { source: "Unity-Technologies/skills", skill: "ui" });
  assert.equal(parseProposal({ title: "Something else", body: null }), null);
});

test("the rejection reason is the comment posted when the issue was closed, else the issue URL", () => {
  const issue = {
    html_url: "https://github.com/o/r/issues/7",
    closed_at: "2026-10-08T12:00:05Z",
    closed_by: { login: "maintainer" },
  };
  const earlier = { user: { login: "maintainer" }, created_at: "2026-10-01T09:00:00Z", body: "Thinking about it." };
  const closing = {
    user: { login: "maintainer" },
    created_at: "2026-10-08T12:00:04Z",
    body: "No audio here.\r\n\r\nMaybe later.",
  };
  const other = { user: { login: "someone" }, created_at: "2026-10-08T12:00:04Z", body: "+1" };

  assert.equal(rejectionReason(issue, [earlier, closing, other]), "No audio here. Maybe later.");
  assert.equal(rejectionReason(issue, [earlier, other]), "https://github.com/o/r/issues/7");
});

test("the plan drops vanished skills, takes accepted and rejected proposals and proposes the rest", () => {
  const source = "Unity-Technologies/skills";
  const entry = (name) => ({ source, sourceType: "github", skillPath: `skills/${name}/SKILL.md`, computedHash: "h" });
  const lock = {
    version: 1,
    skills: {
      "ui-uitk": entry("ui-uitk"),
      gone: entry("gone"),
      local: { source: "./my-skill", sourceType: "local", computedHash: "h" },
    },
  };
  const rejected = { version: 1, sources: { [source]: { "ui-ugui": "No runtime UI." } } };
  const upstreamSkill = (name) => ({ name, skillPath: `skills/${name}/SKILL.md`, frontmatter: {} });
  const upstream = {
    [source]: {
      ref: "main",
      skills: ["ui-uitk", "ui-ugui", "accepted", "rejected-now", "already-proposed", "closed-done", "brand-new"]
        .map(upstreamSkill),
    },
  };
  const issue = (number, skill, fields) => ({
    number,
    title: `chore(skills): adopt ${skill} from ${source}`,
    body: "",
    state: "open",
    state_reason: null,
    labels: [{ name: "skill-proposal" }],
    ...fields,
  });
  const issues = [
    issue(1, "accepted", { labels: [{ name: "skill-proposal" }, { name: "skill-accepted" }] }),
    issue(2, "rejected-now", { state: "closed", state_reason: "not_planned" }),
    issue(3, "already-proposed"),
    issue(4, "closed-done", { state: "closed", state_reason: "completed" }),
    issue(5, "ui-ugui", { state: "closed", state_reason: "not_planned" }),
    issue(6, "rejected-now", { state: "closed", state_reason: "not_planned" }),
  ];

  const plan = planSync({ lock, rejected, upstream, issues });

  assert.deepEqual(plan.removed, [{ name: "gone", source, skillPath: "skills/gone/SKILL.md" }]);
  assert.deepEqual(plan.accept, [{ issue: issues[0], source, skill: upstreamSkill("accepted") }]);
  assert.deepEqual(plan.reject, [{ issue: issues[1], source, skill: "rejected-now" }]);
  assert.deepEqual(plan.propose, [
    { source, skill: upstreamSkill("closed-done") },
    { source, skill: upstreamSkill("brand-new") },
  ]);
});

test("an accepted proposal for a skill that is gone upstream or already locked adds nothing", () => {
  const source = "o/r";
  const lock = { version: 1, skills: { a: { source, sourceType: "github", skillPath: "a/SKILL.md", computedHash: "h" } } };
  const upstream = { [source]: { ref: "main", skills: [{ name: "a", skillPath: "a/SKILL.md", frontmatter: {} }] } };
  const accepted = (number, skill) => ({
    number,
    title: `chore(skills): adopt ${skill} from ${source}`,
    body: "",
    state: "open",
    state_reason: null,
    labels: [{ name: "skill-proposal" }, { name: "skill-accepted" }],
  });

  const plan = planSync({
    lock,
    rejected: { version: 1, sources: {} },
    upstream,
    issues: [accepted(1, "a"), accepted(2, "vanished")],
  });

  assert.deepEqual(plan.accept, []);
  assert.deepEqual(plan.propose, []);
});

test("every github source with a locked skill is watched, once", () => {
  const lock = {
    version: 1,
    skills: {
      a: { source: "o/r", sourceType: "github" },
      b: { source: "o/r", sourceType: "github" },
      c: { source: "x/y", sourceType: "github" },
      d: { source: "./local", sourceType: "local" },
    },
  };
  assert.deepEqual(watchedSources(lock), ["o/r", "x/y"]);
});

test("rejections are added in name order, under a new source when needed", () => {
  const rejected = { version: 1, sources: { "o/r": { alpha: "A.", zulu: "Z." } } };

  const updated = withRejections(rejected, [
    { source: "o/r", skill: "mike", reason: "M." },
    { source: "x/y", skill: "solo", reason: "S." },
  ]);

  assert.equal(JSON.stringify(updated, null, 2), JSON.stringify({
    version: 1,
    sources: { "o/r": { alpha: "A.", mike: "M.", zulu: "Z." }, "x/y": { solo: "S." } },
  }, null, 2));
  assert.deepEqual(Object.keys(rejected.sources["o/r"]), ["alpha", "zulu"]);
});

test("the PR body has a section per kind of change, links upstream commits and closes proposals", () => {
  const source = "o/r";
  const commit = (n) => ({ sha: `${n}`.repeat(40).slice(0, 40), html_url: `https://github.com/o/r/commit/${n}`, message: `Change ${n}\n\nDetails` });

  const body = prBody({
    updated: [{
      name: "ui-uitk",
      source,
      commits: Array.from({ length: 12 }, (_, i) => commit(i + 1)),
      historyUrl: "https://github.com/o/r/commits/main/skills/ui-uitk",
    }],
    added: [{ name: "audio", source, issue: 12 }],
    rejected: [{ name: "vivox", source, issue: 13, reason: "No chat." }],
    removed: [{ name: "gone", source, skillPath: "skills/gone/SKILL.md" }],
  });

  assert.deepEqual(body.match(/^## .+$/gm), ["## Updated", "## Added", "## Rejected", "## Removed upstream"]);
  assert.ok(body.includes("- `ui-uitk` from o/r"));
  assert.ok(body.includes("  - [`1111111`](https://github.com/o/r/commit/1) Change 1\n"));
  assert.ok(!body.includes("commit/11)"));
  assert.ok(body.includes("2 more in the [history](https://github.com/o/r/commits/main/skills/ui-uitk)"));
  assert.ok(body.includes("- `audio` from o/r. Closes #12"));
  assert.ok(body.includes("- `vivox` from o/r: No chat. Closes #13"));
  assert.ok(body.includes("- `gone` from o/r: `skills/gone` no longer exists upstream."));
});

test("empty sections are left out, and nothing to say is no body", () => {
  const empty = { updated: [], added: [], rejected: [], removed: [] };
  assert.equal(prBody(empty), null);

  const body = prBody({ ...empty, added: [{ name: "a", source: "o/r", issue: 1 }] });
  assert.deepEqual(body.match(/^## .+$/gm), ["## Added"]);
});
