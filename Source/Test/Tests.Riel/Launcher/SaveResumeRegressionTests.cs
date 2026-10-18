// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel, a fork of Open Rails, under the GPL version 3 or later.

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;

using MemoryPack;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Orts.ActivityRunner.Processes;

using Riel.Common;
using Riel.Common.Api;
using Riel.Common.Position;
using Riel.Launcher;
using Riel.Models.Content;
using Riel.Models.Handler;
using Riel.Models.Imported.State;
using Riel.Models.Settings;
using Riel.Models.Shim;

namespace Tests.Riel.Launcher
{
    [TestClass]
    [DoNotParallelize]
    public class SaveResumeRegressionTests
    {
        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task NormalAndTimetableResumeArgumentsKeepTheSelectedSnapshotsPath(bool timetable)
        {
            using ModelFixture fixture = new ModelFixture();
            string file = Path.Combine(fixture.Root, "saved journey.save");
            GameSaveState snapshot = Snapshot(timetable, "fixture-route", "fixture-folder");
            await SaveStateBase.ToFile(file, snapshot, CancellationToken.None);
            string action = timetable ? "-SinglePlayerResumeTimetableGame" : "-SingleplayerResume";
            object state = CreateActivityState();
            try
            {
                MethodInfo parse = state.GetType().GetMethod("ResolveSelectionsFromCommandLine", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(parse);
                string[] arguments = Selections.SavedGameArguments(file, action).ToArray();
                ProfileSelectionsModel selection = await (Task<ProfileSelectionsModel>)parse.Invoke(state, new object[] { arguments });

                Assert.AreEqual(timetable ? GamePlayAction.SinglePlayerResumeTimetableGame : GamePlayAction.SingleplayerResume,
                    selection.GamePlayAction);
                Assert.AreEqual(file, selection.GameSaveFile);
                GameSaveState loaded = await SaveStateBase.FromFile<GameSaveState>(selection.GameSaveFile, CancellationToken.None);
                Assert.AreEqual(true, loaded.Valid);
                Assert.AreEqual(snapshot.Route, loaded.Route);
                Assert.AreEqual(snapshot.ProfileSelections.TimetableTrain, loaded.ProfileSelections.TimetableTrain);
            }
            finally
            {
                ((IDisposable)state).Dispose();
            }
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task SnapshotSelectionRepairPreservesSavedModeInsteadOfTheCurrentProfile(bool timetable)
        {
            using ModelFixture fixture = new ModelFixture();
            string suffix = Guid.NewGuid().ToString("N");
            string folderName = "Saved-folder-" + suffix;
            string routeId = "Saved-route-" + suffix;
            ContentModel content = await ContentModelExtensions.Setup(null,
                new[] { (folderName, Path.Combine(fixture.Root, "source")) }, CancellationToken.None);
            FolderModel folder = content.ContentFolders[0];
            RouteModel route = new RouteModel(WorldLocation.None) { Id = routeId, Name = "Saved route" };
            route.Initialize(folder);
            string routeFile = ModelFileResolver<RouteModelHeader>.FilePath(route) + ContentHandlerBase<RouteModelHeader>.SaveStateExtension;
            Directory.CreateDirectory(Path.GetDirectoryName(routeFile));
            using (FileStream stream = File.Create(routeFile))
                await MemoryPackSerializer.SerializeAsync(stream, route, null, CancellationToken.None);

            ProfileModel profile = new ProfileModel("Resume-profile-" + suffix);
            ProfileSelectionsModel current = new ProfileSelectionsModel
            {
                Id = profile.Name,
                Name = profile.Name,
                RouteId = routeId,
                PathId = "snapshot-path",
                FolderName = folderName,
                ActivityType = timetable ? ActivityType.Explorer : ActivityType.TimeTable,
                WagonSetId = "current-consist-must-not-win",
                TimetableSet = "current-timetable-must-not-win",
            };
            await profile.UpdateSettingsModel(current, CancellationToken.None);

            ProfileUserSettingsModel settings = new ProfileUserSettingsModel();
            settings.Initialize(profile);
            // This routine only reads Game.UserSettings. Avoid constructing a GameHost,
            // which would initialize renderer/audio workers in an otherwise file-only test.
            GameHost host = (GameHost)RuntimeHelpers.GetUninitializedObject(typeof(GameHost));
            GC.SuppressFinalize(host);
            FieldInfo userSettings = typeof(GameHost).GetField("<UserSettings>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(userSettings);
            userSettings.SetValue(host, settings);
            object state = CreateActivityState();
            try
            {
                PropertyInfo game = state.GetType().BaseType.GetProperty("Game", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(game);
                game.SetValue(state, host);
                GameSaveState snapshot = Snapshot(timetable, routeId, folderName);
                string file = Path.Combine(fixture.Root, "snapshot.save");
                await SaveStateBase.ToFile(file, snapshot, CancellationToken.None);
                snapshot = await SaveStateBase.FromFile<GameSaveState>(file, CancellationToken.None);

                MethodInfo repair = state.GetType().GetMethod("ResolveSaveSelections", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.IsNotNull(repair);
                ProfileSelectionsModel restored = await (ValueTask<ProfileSelectionsModel>)repair.Invoke(state,
                    new object[] { snapshot, CancellationToken.None });

                Assert.AreEqual(routeId, restored.RouteId);
                Assert.AreEqual("snapshot-path", restored.PathId);
                Assert.AreEqual(folderName, restored.FolderName);
                Assert.IsNull(restored.GameSaveFile);
                if (timetable)
                {
                    Assert.AreEqual(ActivityType.TimeTable, restored.ActivityType);
                    Assert.AreEqual(GamePlayAction.SinglePlayerTimetableGame, restored.GamePlayAction);
                    Assert.AreEqual("saved-weekday", restored.TimetableSet);
                    Assert.AreEqual("saved-group", restored.TimetableName);
                    Assert.AreEqual("101", restored.TimetableTrain);
                    Assert.AreEqual(DayOfWeek.Saturday, restored.TimetableDay);
                }
                else
                {
                    Assert.AreEqual(ActivityType.Explorer, restored.ActivityType);
                    Assert.AreEqual(GamePlayAction.SingleplayerNewGame, restored.GamePlayAction);
                    Assert.AreEqual("saved-consist", restored.WagonSetId);
                    Assert.AreEqual(new TimeOnly(8, 30), restored.StartTime);
                    Assert.AreEqual(SeasonType.Autumn, restored.Season);
                    Assert.AreEqual(WeatherType.Rain, restored.Weather);
                }
            }
            finally
            {
                ((IDisposable)state).Dispose();
            }
        }

        private static object CreateActivityState()
        {
            Type state = typeof(GameHost).Assembly.GetType("Orts.ActivityRunner.Processes.GameStateRunActivity", throwOnError: true);
            return Activator.CreateInstance(state, new object[] { Array.Empty<string>() });
        }

        private static GameSaveState Snapshot(bool timetable, string routeId, string folderName)
        {
            return new GameSaveState
            {
                Route = routeId,
                Path = "snapshot-path",
                SimulatorSaveState = new SimulatorSaveState
                {
                    ClockTime = 86400 + 8 * 3600 + 30 * 60,
                    Season = SeasonType.Autumn,
                    Weather = WeatherType.Rain,
                    TimetableFile = timetable ? "saved-weekday.tt" : null,
                },
                ProfileSelections = new ProfileSelectionsModel
                {
                    FolderName = folderName,
                    // Old resumed timetable saves can lack the type even though the
                    // simulator snapshot still identifies the timetable unambiguously.
                    ActivityType = timetable ? ActivityType.None : ActivityType.Explorer,
                    WagonSetId = timetable ? null : "saved-consist",
                    TimetableSet = timetable ? "saved-weekday" : null,
                    TimetableName = timetable ? "saved-group" : null,
                    TimetableTrain = timetable ? "101" : null,
                    TimetableDay = DayOfWeek.Saturday,
                    GameSaveFile = "stale-previous-save.save",
                },
            };
        }

        private sealed class ModelFixture : IDisposable
        {
            private readonly string previousRoot = ModelStore.ContentRoot;
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "riel-resume-regression-" + Guid.NewGuid().ToString("N"));

            public ModelFixture()
            {
                Directory.CreateDirectory(Root);
                ModelStore.RedirectContentRoot(Root);
                ClearContentCache();
            }

            public void Dispose()
            {
                ModelStore.RedirectContentRoot(previousRoot);
                ClearContentCache();
                Directory.Delete(Root, recursive: true);
            }

            private static void ClearContentCache()
            {
                FieldInfo field = typeof(ContentHandlerBase<ContentModel>).GetField("modelTaskCache", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(field);
                ((ConcurrentDictionary<string, Task<ContentModel>>)field.GetValue(null)).Clear();
            }
        }
    }
}
