using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    public class InstallPlannerTests
    {
        const string LockedHash = "locked-hash";
        const string OldHash = "old-hash";

        static LockedSkill Skill(string name) =>
            new LockedSkill(name, "owner/repo", "github", $"skills/{name}/SKILL.md", LockedHash);

        static Lockfile Lock(params string[] names) => new Lockfile(names.Select(Skill).ToList());

        static UnsupportedSkill Unsupported(string name) => new UnsupportedSkill(name, "unity-package");

        /// <summary>Every default folder holds <c>tdd</c>, installed by the tool at the locked hash, under the given block.</summary>
        static ProjectState TddInstalledEverywhere(params string[] ignored) =>
            new ProjectState(FolderLayout.Default.Folders.Select(f =>
                new FolderState(f, new[] { "tdd" }, new[] { "tdd" },
                    installedHashes: new Dictionary<string, string> { ["tdd"] = LockedHash }, ignored: ignored)));

        /// <summary>Renders an action the way the expectations below are written.</summary>
        static string Describe(PlanAction action) =>
            action.Kind == PlanActionKind.Link
                ? $"Link {action.Folder.RelativePath}/{action.SkillName} -> {action.LinkTarget.RelativePath}/{action.SkillName}"
                : $"{action.Kind} {action.Folder.RelativePath}/{action.SkillName}";

        /// <summary>
        /// A folder's contents, one entry per string: <c>name</c> = managed copy at the locked hash,
        /// <c>name@old</c> = managed copy at another hash, <c>name?</c> = foreign (not in the managed list),
        /// <c>name-</c> = listed as managed but missing on disk.
        /// </summary>
        static FolderState Folder(SkillsFolder folder, string[] entries)
        {
            var present = new List<string>();
            var managed = new List<string>();
            var hashes = new Dictionary<string, string>();
            foreach (var entry in entries)
            {
                if (entry.EndsWith("?")) present.Add(entry.TrimEnd('?'));
                else if (entry.EndsWith("-")) managed.Add(entry.TrimEnd('-'));
                else
                {
                    var stale = entry.EndsWith("@old");
                    var name = stale ? entry.Substring(0, entry.Length - "@old".Length) : entry;
                    present.Add(name);
                    managed.Add(name);
                    hashes[name] = stale ? OldHash : LockedHash;
                }
            }
            return new FolderState(folder, present, managed, hashes);
        }

        static ProjectState Project(string[] canonical, string[] claude)
        {
            var layout = FolderLayout.Default;
            return new ProjectState(new[]
            {
                Folder(layout.Canonical, canonical),
                Folder(layout.Folders.Single(f => f.Role == SkillsFolderRole.Link), claude),
            });
        }

        static TestCaseData Case(string name, string[] locked, string[] canonical, string[] claude, params string[] expected) =>
            new TestCaseData(locked, canonical, claude, expected).SetName("Plan_" + name);

        static readonly string[] None = new string[0];

        static IEnumerable<TestCaseData> Cases()
        {
            // Install
            yield return Case("EmptyProject_EmptyLock_DoesNothing", None, None, None);
            yield return Case("EmptyProject_OneSkill_InstallsCanonicalCopyAndLinksClaude",
                new[] { "tdd" }, None, None,
                "Install .agents/skills/tdd",
                "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Case("EmptyProject_TwoSkills_InstallsAndLinksEach",
                new[] { "tdd", "code-review" }, None, None,
                "Install .agents/skills/tdd",
                "Link .claude/skills/tdd -> .agents/skills/tdd",
                "Install .agents/skills/code-review",
                "Link .claude/skills/code-review -> .agents/skills/code-review");
            yield return Case("NewlyLockedSkill_InstallsOnlyThatSkill",
                new[] { "tdd", "code-review" }, new[] { "tdd" }, new[] { "tdd" },
                "Install .agents/skills/code-review",
                "Link .claude/skills/code-review -> .agents/skills/code-review");
            yield return Case("ManagedEntriesMissingOnDisk_ReinstallsAndRelinks",
                new[] { "tdd" }, new[] { "tdd-" }, new[] { "tdd-" },
                "Install .agents/skills/tdd",
                "Link .claude/skills/tdd -> .agents/skills/tdd");

            // Nothing to do
            yield return Case("SkillInstalledAtLockedHash_DoesNothing",
                new[] { "tdd" }, new[] { "tdd" }, new[] { "tdd" });

            // Update
            yield return Case("InstalledCopyDiffersFromLock_UpdatesCanonicalCopyOnly",
                new[] { "tdd" }, new[] { "tdd@old" }, new[] { "tdd" },
                "Update .agents/skills/tdd");

            // Remove
            yield return Case("ManagedSkillNoLongerLocked_RemovesLinkThenCopy",
                None, new[] { "old" }, new[] { "old" },
                "Remove .claude/skills/old",
                "Remove .agents/skills/old");
            yield return Case("UnlockedSkillManagedOnlyInCanonical_RemovesCopyOnly",
                new[] { "tdd" }, new[] { "tdd", "old" }, new[] { "tdd" },
                "Remove .agents/skills/old");
            yield return Case("UnlockedManagedNameMissingOnDisk_PlansNothing",
                None, new[] { "old-" }, new[] { "old-" });

            // Unlink vs Remove: a managed, unlocked link is unlinked when its canonical entry is gone, removed (with
            // the managed canonical copy) when that copy still exists, and kept when the canonical entry is the
            // project's own (project-authored).
            yield return Case("ManagedLinkWhoseCanonicalEntryIsGone_Unlinks",
                None, new[] { "old-" }, new[] { "old" },
                "Unlink .claude/skills/old");
            yield return Case("ProjectAuthoredSkillDeleted_UnlinksItsLink",
                None, None, new[] { "house-style" },
                "Unlink .claude/skills/house-style");
            yield return Case("CanonicalCopyTakenOverByTheProject_KeepsItsLink",
                None, new[] { "tdd?" }, new[] { "tdd" });

            // LeaveForeign
            yield return Case("ForeignCopyOfLockedSkill_LeftAloneButStillLinked",
                new[] { "tdd" }, new[] { "tdd?" }, None,
                "LeaveForeign .agents/skills/tdd",
                "Link .claude/skills/tdd -> .agents/skills/tdd");
            yield return Case("ForeignClaudeEntryOfLockedSkill_LeftAlone",
                new[] { "tdd" }, new[] { "tdd" }, new[] { "tdd?" },
                "LeaveForeign .claude/skills/tdd");
            yield return Case("ForeignUnlockedEntries_PlanNothing",
                None, new[] { "mine?" }, new[] { "mine?" });
        }

        [TestCaseSource(nameof(Cases))]
        public void Plan(string[] locked, string[] canonical, string[] claude, string[] expectedActions)
        {
            var plan = InstallPlanner.Plan(Lock(locked), Project(canonical, claude), FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(expectedActions));
        }

        static IEnumerable<TestCaseData> ManagedCases()
        {
            yield return new TestCaseData(new[] { "tdd", "code-review" }, None, None, new[] { "code-review", "tdd" }, new[] { "code-review", "tdd" })
                .SetName("Plan_EmptyProject_ManagesEveryLockedSkillInBothFolders");
            yield return new TestCaseData(new[] { "tdd" }, new[] { "tdd@old" }, new[] { "tdd" }, new[] { "tdd" }, new[] { "tdd" })
                .SetName("Plan_Update_KeepsManagingTheSkill");
            yield return new TestCaseData(new[] { "tdd" }, new[] { "tdd", "old" }, new[] { "tdd", "old-" }, new[] { "tdd" }, new[] { "tdd" })
                .SetName("Plan_Remove_DropsTheNameFromEveryManagedList");
            yield return new TestCaseData(new[] { "tdd" }, new[] { "tdd?", "mine?" }, new[] { "tdd?", "mine?" }, None, None)
                .SetName("Plan_ForeignEntries_NeverBecomeManaged");
        }

        [TestCaseSource(nameof(ManagedCases))]
        public void Plan_ManagedNames(string[] locked, string[] canonical, string[] claude, string[] expectedCanonical, string[] expectedClaude)
        {
            var plan = InstallPlanner.Plan(Lock(locked), Project(canonical, claude), FolderLayout.Default);

            var managed = plan.ManagedNames.ToDictionary(m => m.Key.RelativePath, m => m.Value);
            Assert.That(managed.Keys, Is.EquivalentTo(new[] { ".agents/skills", ".claude/skills" }));
            Assert.That(managed[".agents/skills"], Is.EqualTo(expectedCanonical));
            Assert.That(managed[".claude/skills"], Is.EqualTo(expectedClaude));
        }

        /// <summary>The "is the installed copy current?" decision is swappable (e.g. compare against upstream instead).</summary>
        sealed class AlwaysStale : IInstalledCopyCheck
        {
            public bool IsCurrent(LockedSkill skill, string installedHash) => false;
        }

        [Test]
        public void Plan_WithAnotherInstalledCopyCheck_UsesItToDecideUpdates()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), Project(new[] { "tdd" }, new[] { "tdd" }), FolderLayout.Default, new AlwaysStale());

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Update .agents/skills/tdd" }));
        }

        [Test]
        public void Plan_EmptyLockAndStaleManagedNameMissingOnDisk_HasChangesToTheManagedState()
        {
            var plan = InstallPlanner.Plan(Lock(), Project(new[] { "tdd-" }, new[] { "tdd-" }), FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.HasChanges, Is.True);
        }

        [Test]
        public void Plan_EverythingInstalledAndRecorded_HasNoChanges()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), TddInstalledEverywhere("tdd"), FolderLayout.Default);

            Assert.That(plan.HasChanges, Is.False);
        }

        [Test]
        public void Plan_GitignoreBlockMissingALockedSkill_HasChanges()
        {
            var plan = InstallPlanner.Plan(Lock("tdd"), TddInstalledEverywhere(), FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.HasChanges, Is.True);
            Assert.That(plan.IgnoredNames.Values, Has.All.EqualTo(new[] { "tdd" }));
        }

        [TestCase(LockedHash, true)]
        [TestCase("LOCKED-HASH", true)]
        [TestCase(OldHash, false)]
        [TestCase(null, false)]
        public void LockedHashCheck_ComparesInstalledHashWithTheLock(string installedHash, bool current)
        {
            Assert.That(LockedHashCheck.Instance.IsCurrent(Skill("tdd"), installedHash), Is.EqualTo(current));
        }

        [Test]
        public void LockedHashCheck_SkillsShHashedSource_CountsAnInstalledCopyAsCurrent()
        {
            var skill = new LockedSkill("next", "vercel-labs/skills", "github", "skills/next/SKILL.md", "server-hash");

            Assert.That(LockedHashCheck.Instance.IsCurrent(skill, OldHash), Is.True);
        }
    
        static SkillsFolder Agents => FolderLayout.Default.Canonical;
        static SkillsFolder Claude => FolderLayout.Default.Folders.Single(f => f.Role == SkillsFolderRole.Link);

        static ProjectState Project(FolderState agents, FolderState claude) => new ProjectState(new[] { agents, claude });

        static IReadOnlyList<string> ManagedIn(InstallPlan plan, SkillsFolder folder) => plan.ManagedNames[folder];

        [Test]
        public void Plan_ProjectAuthoredSkill_LinksItIntoClaudeAndManagesOnlyTheLink()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "house-style" }, managed: null),
                new FolderState(Claude, entries: null, managed: null));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .claude/skills/house-style -> .agents/skills/house-style" }));
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "house-style" }));
            Assert.That(ManagedIn(plan, Agents), Is.Empty);
        }

        [Test]
        public void Plan_UnsupportedLockEntryInCanonicalFolder_IsLeftAlone()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "unity-pipeline" }, managed: null),
                new FolderState(Claude, entries: null, managed: null));
            var lockfile = new Lockfile(new LockedSkill[0], new[] { Unsupported("unity-pipeline") });

            var plan = InstallPlanner.Plan(lockfile, project, FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(ManagedIn(plan, Claude), Is.Empty);
            Assert.That(plan.IgnoredNames.Keys, Is.EquivalentTo(new[] { Agents, Claude }));
            Assert.That(plan.IgnoredNames.Values, Has.All.Empty);
        }

        [Test]
        public void Plan_GithubAndUnsupportedLockEntries_IgnoresOnlyTheGithubSkill()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "unity-pipeline" }, managed: null),
                new FolderState(Claude, entries: null, managed: null));
            var lockfile = new Lockfile(new[] { Skill("tdd") }, new[] { Unsupported("unity-pipeline") });

            var plan = InstallPlanner.Plan(lockfile, project, FolderLayout.Default);

            Assert.That(plan.Actions.Where(a => a.SkillName == "unity-pipeline"), Is.Empty);
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "tdd" }));
            Assert.That(plan.IgnoredNames.Keys, Is.EquivalentTo(new[] { Agents, Claude }));
            Assert.That(plan.IgnoredNames.Values, Has.All.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Plan_GitignoreBlockListingAnUnsupportedEntry_DropsItAndHasChanges()
        {
            var lockfile = new Lockfile(new[] { Skill("tdd") }, new[] { Unsupported("unity-pipeline") });

            var plan = InstallPlanner.Plan(lockfile, TddInstalledEverywhere("tdd", "unity-pipeline"), FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(plan.HasChanges, Is.True);
            Assert.That(plan.IgnoredNames.Values, Has.All.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillNextToLockedOne_IsOnlyEverLinked()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "house-style" }, managed: null),
                new FolderState(Claude, entries: null, managed: null));

            var plan = InstallPlanner.Plan(Lock("tdd"), project, FolderLayout.Default);

            var forProjectSkill = plan.Actions.Where(a => a.SkillName == "house-style").ToList();
            Assert.That(forProjectSkill.Select(a => a.Kind), Is.EqualTo(new[] { PlanActionKind.Link }));
            Assert.That(ManagedIn(plan, Agents), Is.EqualTo(new[] { "tdd" }));
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "house-style", "tdd" }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillAlreadyLinked_DoesNothingAndKeepsManagingTheLink()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "house-style" }, managed: null),
                new FolderState(Claude, entries: new[] { "house-style" }, managed: new[] { "house-style" }));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "house-style" }));
            Assert.That(ManagedIn(plan, Agents), Is.Empty);
        }

        [Test]
        public void Plan_ProjectAuthoredSkillWithForeignClaudeEntry_LeavesTheEntryAlone()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "house-style" }, managed: null),
                new FolderState(Claude, entries: new[] { "house-style" }, managed: null));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions, Is.Empty);
            Assert.That(ManagedIn(plan, Claude), Is.Empty);
        }

        [Test]
        public void Plan_ProjectAuthoredSkillDeleted_UnlinksItsManagedLink()
        {
            var project = Project(
                new FolderState(Agents, entries: null, managed: null),
                new FolderState(Claude, entries: new[] { "house-style" }, managed: new[] { "house-style" }));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Unlink .claude/skills/house-style" }));
            Assert.That(ManagedIn(plan, Claude), Is.Empty);
        }
    
        [Test]
        public void Plan_PlainFileInCanonicalFolder_IsNotAProjectAuthoredSkill()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "README.md", "house-style" }, managed: null, files: new[] { "README.md" }),
                new FolderState(Claude, entries: null, managed: null));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .claude/skills/house-style -> .agents/skills/house-style" }));
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "house-style" }));
        }

        [Test]
        public void Plan_FolderWithoutSkillMdInCanonicalFolder_IsNotAProjectAuthoredSkill()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "drafts", "house-style" }, managed: null, withoutSkillFile: new[] { "drafts" }),
                new FolderState(Claude, entries: null, managed: null));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions.Select(Describe), Is.EqualTo(new[] { "Link .claude/skills/house-style -> .agents/skills/house-style" }));
            Assert.That(ManagedIn(plan, Claude), Is.EqualTo(new[] { "house-style" }));
        }

        [Test]
        public void Plan_ProjectAuthoredSkillLosesItsSkillMd_RemovesItsManagedLink()
        {
            var project = Project(
                new FolderState(Agents, entries: new[] { "house-style" }, managed: null, withoutSkillFile: new[] { "house-style" }),
                new FolderState(Claude, entries: new[] { "house-style" }, managed: new[] { "house-style" }));

            var plan = InstallPlanner.Plan(Lock(), project, FolderLayout.Default);

            Assert.That(plan.Actions.Select(a => (a.Kind, a.Folder.RelativePath)),
                Is.EqualTo(new[] { (PlanActionKind.Remove, ".claude/skills") }));
            Assert.That(ManagedIn(plan, Claude), Is.Empty);
        }
    }
}
