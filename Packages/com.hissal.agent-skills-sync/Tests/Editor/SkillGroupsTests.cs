using System;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace Hissal.AgentSkillsSync.Tests
{
    /// <summary>The defaults skill groups give locked skills, and how they list the lock for the window.</summary>
    public class SkillGroupsTests
    {
        static readonly FolderLayout Layout = FolderLayout.Default;

        static SkillGroup Group(string name, bool selectedByDefault, params string[] skills) =>
            new SkillGroup(name, selectedByDefault, skills);

        static Lockfile Lock(string[] names, params UnsupportedSkill[] unsupported) =>
            new Lockfile(names.Select(n => new LockedSkill(n, "owner/repo", "github", $"skills/{n}/SKILL.md", "hash")).ToList(), unsupported);

        static string[] Names(SkillGroupSection section) => section.Skills.Select(s => s.Name).ToArray();

        /// <summary>Prefs of a machine that stored these choices; never saved.</summary>
        static LocalPrefs Prefs(string[] optedOut = null, string[] optedIn = null)
        {
            var prefs = LocalPrefs.Load(Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N")));
            prefs.OptedOutSkills = optedOut ?? new string[0];
            prefs.OptedInSkills = optedIn ?? new string[0];
            return prefs;
        }

        static ProjectState EmptyProject() => new ProjectState(Layout.Folders.Select(f => new FolderState(f, new string[0], new string[0])));

        [Test]
        public void IsSelectedByDefault_NoGroups_SelectsEverySkill()
        {
            Assert.That(SkillGroups.None.IsSelectedByDefault("tdd"), Is.True);
        }

        [Test]
        public void IsSelectedByDefault_GroupedSkill_FollowsItsGroup()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd"), Group("art", false, "ui-uitk") });

            Assert.That(groups.IsSelectedByDefault("tdd"), Is.True);
            Assert.That(groups.IsSelectedByDefault("ui-uitk"), Is.False);
        }

        [Test]
        public void IsSelectedByDefault_SkillInSeveralGroups_IsSelectedWhenAnyGroupSelects()
        {
            var groups = new SkillGroups(new[] { Group("art", false, "ui-uitk"), Group("recommended", true, "ui-uitk") },
                ungroupedSelectedByDefault: false);

            Assert.That(groups.IsSelectedByDefault("ui-uitk"), Is.True);
        }

        [TestCase(true)]
        [TestCase(false)]
        public void IsSelectedByDefault_UngroupedSkill_FollowsTheUngroupedDefault(bool ungrouped)
        {
            var groups = new SkillGroups(new[] { Group("recommended", !ungrouped, "tdd") }, ungrouped);

            Assert.That(groups.IsSelectedByDefault("code-review"), Is.EqualTo(ungrouped));
            Assert.That(groups.IsSelectedByDefault("tdd"), Is.EqualTo(!ungrouped), "a grouped skill ignores the ungrouped default");
        }

        [Test]
        public void Selection_ExplicitChoicesWinOverGroupDefaults()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd", "code-review"), Group("art", false, "ui-uitk", "shaders") });
            var prefs = Prefs(optedOut: new[] { "tdd" }, optedIn: new[] { "ui-uitk" });

            var selection = SkillSelection.From(prefs, groups);

            Assert.That(selection.IsSelected("tdd"), Is.False, "opted out of a recommended skill");
            Assert.That(selection.IsSelected("ui-uitk"), Is.True, "opted in to an optional skill");
            Assert.That(selection.IsSelected("code-review"), Is.True);
            Assert.That(selection.IsSelected("shaders"), Is.False);
        }

        [Test]
        public void Selection_SkillMovedBetweenGroups_ChangesForMachinesThatNeverChose()
        {
            var before = new SkillGroups(new[] { Group("recommended", true, "tdd"), Group("optional", false, "ui-uitk") });
            var after = new SkillGroups(new[] { Group("recommended", true, "ui-uitk"), Group("optional", false, "tdd") });
            var neverChose = Prefs();

            Assert.That(SkillSelection.From(neverChose, before).IsSelected("tdd"), Is.True);
            Assert.That(SkillSelection.From(neverChose, before).IsSelected("ui-uitk"), Is.False);
            Assert.That(SkillSelection.From(neverChose, after).IsSelected("tdd"), Is.False);
            Assert.That(SkillSelection.From(neverChose, after).IsSelected("ui-uitk"), Is.True);
        }

        [Test]
        public void Selection_SkillMovedOutOfEverySelectedGroup_StaysForAMachineThatOptedIn()
        {
            var optional = new SkillGroups(new[] { Group("optional", false, "tdd") });
            var prefs = Prefs();
            SkillSelection.Set(prefs, new[] { "tdd" }, true, optional);
            var recommended = new SkillGroups(new[] { Group("recommended", true, "tdd") });
            var ungroupedOff = new SkillGroups(ungroupedSelectedByDefault: false);

            Assert.That(SkillSelection.From(prefs, recommended).IsSelected("tdd"), Is.True);
            Assert.That(SkillSelection.From(prefs, optional).IsSelected("tdd"), Is.True);
            Assert.That(SkillSelection.From(prefs, ungroupedOff).IsSelected("tdd"), Is.True);
        }

        [Test]
        public void Selection_IsOptedOut_IsOnlyTheContributorsChoice()
        {
            var groups = new SkillGroups(new[] { Group("optional", false, "ui-uitk") });
            var selection = SkillSelection.From(Prefs(optedOut: new[] { "tdd" }), groups);

            Assert.That(selection.IsOptedOut("tdd"), Is.True);
            Assert.That(selection.IsOptedOut("ui-uitk"), Is.False, "unselected by default, not opted out");
        }

        [Test]
        public void Set_WholeGroup_StoresOnlyTheChoicesThatDifferFromEachSkillsDefault()
        {
            // ui-uitk is also recommended, so its default is selected; shaders is only in art.
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd", "ui-uitk"), Group("art", false, "ui-uitk", "shaders") });
            var prefs = Prefs(optedOut: new[] { "ui-uitk" });

            SkillSelection.Set(prefs, new[] { "ui-uitk", "shaders" }, true, groups);

            Assert.That(prefs.OptedInSkills, Is.EqualTo(new[] { "shaders" }));
            Assert.That(prefs.OptedOutSkills, Is.Empty);

            SkillSelection.Set(prefs, new[] { "ui-uitk", "shaders" }, false, groups);

            Assert.That(prefs.OptedInSkills, Is.Empty);
            Assert.That(prefs.OptedOutSkills, Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(SkillSelection.From(prefs, groups).IsSelected("tdd"), Is.True, "not in the group toggled");
        }

        [Test]
        public void Plan_SkillInSeveralGroups_IsInstalledOnce()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd"), Group("programming", true, "tdd") });
            var choices = new MachineChoices(Layout, new[] { Layout.Canonical }, skills: SkillSelection.From(Prefs(), groups));

            var plan = InstallPlanner.Plan(Lock(new[] { "tdd" }), EmptyProject(), choices);

            Assert.That(plan.Actions.Select(a => a.ToString()), Is.EqualTo(new[] { "Install .agents/skills/tdd" }));
        }

        [Test]
        public void Plan_GroupNotSelectedByDefault_IsLeftOutUntilOptedIn()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd"), Group("art", false, "ui-uitk") });
            InstallPlan Plan(LocalPrefs prefs) => InstallPlanner.Plan(Lock(new[] { "tdd", "ui-uitk" }), EmptyProject(),
                new MachineChoices(Layout, new[] { Layout.Canonical }, skills: SkillSelection.From(prefs, groups)));

            Assert.That(Plan(Prefs()).Actions.Select(a => a.ToString()), Is.EqualTo(new[] { "Install .agents/skills/tdd" }));
            Assert.That(Plan(Prefs(optedIn: new[] { "ui-uitk" })).Actions.Select(a => a.ToString()),
                Is.EqualTo(new[] { "Install .agents/skills/tdd", "Install .agents/skills/ui-uitk" }));
        }

        [Test]
        public void Sections_NoGroups_IsOneUngroupedSectionOfEverySkill()
        {
            var section = SkillGroups.None.Sections(Lock(new[] { "tdd", "code-review" })).Single();

            Assert.That(section.Name, Is.Null);
            Assert.That(Names(section), Is.EqualTo(new[] { "tdd", "code-review" }));
        }

        [Test]
        public void Sections_Groups_AreInFileOrderWithUngroupedSkillsLast()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd"), Group("art", false, "ui-uitk") },
                ungroupedSelectedByDefault: false);

            var sections = groups.Sections(Lock(new[] { "code-review", "ui-uitk", "tdd", "shaders" }));

            Assert.That(sections.Select(s => s.Name), Is.EqualTo(new[] { "recommended", "art", null }));
            Assert.That(sections.Select(s => s.SelectedByDefault), Is.EqualTo(new[] { true, false, false }));
            Assert.That(Names(sections[0]), Is.EqualTo(new[] { "tdd" }));
            Assert.That(Names(sections[1]), Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(Names(sections[2]), Is.EqualTo(new[] { "code-review", "shaders" }), "ungrouped, in lock order");
        }

        [Test]
        public void Sections_EverySkillGrouped_HasNoUngroupedSection()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd") });

            Assert.That(groups.Sections(Lock(new[] { "tdd" })).Select(s => s.Name), Is.EqualTo(new[] { "recommended" }));
        }

        [Test]
        public void Sections_SkillInSeveralGroups_IsListedInEach()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd", "ui-uitk"), Group("art", false, "ui-uitk", "ui-uitk") });

            var sections = groups.Sections(Lock(new[] { "tdd", "ui-uitk" }));

            Assert.That(Names(sections[0]), Is.EqualTo(new[] { "tdd", "ui-uitk" }));
            Assert.That(Names(sections[1]), Is.EqualTo(new[] { "ui-uitk" }));
            Assert.That(sections[1].Skills[0], Is.SameAs(sections[0].Skills[1]), "the same locked skill, not a second one");
        }

        [Test]
        public void Sections_NameNotInTheLock_IsReportedAsUnknownNotAsAnError()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd", "tddd"), Group("art", false, "gone") });

            var sections = groups.Sections(Lock(new[] { "tdd" }));

            Assert.That(Names(sections[0]), Is.EqualTo(new[] { "tdd" }));
            Assert.That(sections[0].Unknown, Is.EqualTo(new[] { "tddd" }));
            Assert.That(sections[1].Skills, Is.Empty);
            Assert.That(sections[1].Unknown, Is.EqualTo(new[] { "gone" }));
        }

        [Test]
        public void Sections_UnsupportedEntryInAGroup_IsListedAsUnsupportedNotUnknown()
        {
            var groups = new SkillGroups(new[] { Group("recommended", true, "tdd", "unity-cli") });

            var section = groups.Sections(Lock(new[] { "tdd" }, new UnsupportedSkill("unity-cli", "unity-package"))).Single();

            Assert.That(section.Unsupported.Select(s => s.Name), Is.EqualTo(new[] { "unity-cli" }));
            Assert.That(section.Unknown, Is.Empty);
            Assert.That(Names(section), Is.EqualTo(new[] { "tdd" }));
        }

        [Test]
        public void MachineChoicesRead_WithGroups_SelectsByTheirDefaultsUnderTheStoredChoices()
        {
            var root = Path.Combine(Path.GetTempPath(), "AgentSkillsSyncTests", Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(Path.Combine(root, "home"));
                var groups = new SkillGroups(new[] { Group("art", false, "ui-uitk", "shaders") });
                var prefs = Prefs(optedIn: new[] { "shaders" });

                var choices = MachineChoices.Read(root, prefs, new UserEnvironment(Path.Combine(root, "home"), _ => null), groups: groups);

                Assert.That(choices.Skills.IsSelected("tdd"), Is.True);
                Assert.That(choices.Skills.IsSelected("ui-uitk"), Is.False);
                Assert.That(choices.Skills.IsSelected("shaders"), Is.True);
            }
            finally
            {
                TempDirectory.Delete(root);
            }
        }
    }
}
