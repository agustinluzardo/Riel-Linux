// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel, a fork of Open Rails, under the GPL version 3 or later.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;

using Riel.Common;
using Riel.Common.Api;
using Riel.Launcher;
using Riel.Launcher.Gui;
using Riel.Models.Imported.State;
using Riel.Models.Settings;

using Xunit;

namespace Tests.Launcher.Gui
{
    public class SavedGamesRegressionTests
    {
        [AvaloniaFact]
        public async Task ContinueAndReplayPreviousWaitForTheSelectedSnapshotsMetadata()
        {
            using SaveFixture fixture = new SaveFixture();
            SavedGameItem item = await fixture.WriteSave(ActivityType.TimeTable);
            File.WriteAllBytes(Path.ChangeExtension(item.File.FullName, ".replay"), Array.Empty<byte>());
            SavedGamesWindow window = CreateWindow(item);
            try
            {
                Assert.False(Control<Button>(window, "ContinueButton").IsEnabled);
                Assert.False(Control<Button>(window, "ReplayPreviousButton").IsEnabled);

                // Double-click and keyboard/default-button paths must enforce readiness too,
                // even when they reach the handler without going through a disabled button.
                Call(window, "Accept");
                Call(window, "ChooseReplay", "-SingleplayerReplayFromSave");
                Assert.False(ClosingToken(window).IsCancellationRequested);

                await (Task)Call(window, "LoadDetails");
                Assert.True(item.DetailsLoaded);
                Assert.Equal("-SinglePlayerResumeTimetableGame", item.ResumeAction);
                Assert.True(Control<Button>(window, "ContinueButton").IsEnabled);
                Assert.True(Control<Button>(window, "ReplayPreviousButton").IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaTheory]
        [InlineData(ActivityType.Activity, "-SingleplayerResume")]
        [InlineData(ActivityType.Explorer, "-SingleplayerResume")]
        [InlineData(ActivityType.TimeTable, "-SinglePlayerResumeTimetableGame")]
        public async Task ALoadedSnapshotReturnsItsActualResumeAction(ActivityType type, string action)
        {
            using SaveFixture fixture = new SaveFixture();
            SavedGameItem item = await fixture.WriteSave(type);
            SavedGamesWindow window = CreateWindow(item);
            Window owner = new Window();
            try
            {
                await (Task)Call(window, "LoadDetails");
                Assert.True(item.DetailsLoaded);
                Assert.True(item.CanResume);
                Assert.Equal(action, item.ResumeAction);
                Assert.True(Control<Button>(window, "ContinueButton").IsEnabled);

                // Only this saved-game dialog is shown. Its Opened handler reads fixture
                // snapshots; a MainWindow would scan installations and check for updates.
                owner.Show();
                Task<SavedGameChoice> result = window.ShowDialog<SavedGameChoice>(owner);
                Call(window, "Accept");
                SavedGameChoice choice = await result;

                Assert.Equal(item.File.FullName, choice.File);
                Assert.Equal(action, choice.Action);
                Assert.Equal(new[] { action, item.File.FullName }, Selections.SavedGameArguments(choice.File, choice.Action));
            }
            finally
            {
                window.Close();
                owner.Close();
            }
        }

        [AvaloniaFact]
        public async Task AnExplicitlyInvalidSnapshotCannotContinueOrReplayPrevious()
        {
            using SaveFixture fixture = new SaveFixture();
            SavedGameItem item = await fixture.WriteSave(ActivityType.TimeTable, valid: false);
            File.WriteAllBytes(Path.ChangeExtension(item.File.FullName, ".replay"), Array.Empty<byte>());
            SavedGamesWindow window = CreateWindow(item);
            try
            {
                await (Task)Call(window, "LoadDetails");
                Assert.True(item.DetailsLoaded);
                Assert.False(item.CanResume);
                Assert.False(Control<Button>(window, "ContinueButton").IsEnabled);
                Assert.False(Control<Button>(window, "ReplayPreviousButton").IsEnabled);
                Assert.True(Control<Button>(window, "DeleteInvalidButton").IsEnabled);

                Call(window, "Accept");
                Call(window, "ChooseReplay", "-SingleplayerReplayFromSave");
                Assert.False(ClosingToken(window).IsCancellationRequested);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task UnreadableOptionalMetadataStillAllowsTheSimulatorsNormalRestoreAttempt()
        {
            using SaveFixture fixture = new SaveFixture();
            string file = Path.Combine(fixture.Directory, "older-format.save");
            File.WriteAllBytes(file, new byte[] { 1, 2, 3 });
            SavedGameItem item = new SavedGameItem(new FileInfo(file));
            SavedGamesWindow window = CreateWindow(item);
            try
            {
                Assert.False(Control<Button>(window, "ContinueButton").IsEnabled);
                await (Task)Call(window, "LoadDetails");

                Assert.True(item.DetailsLoaded);
                Assert.True(item.CanResume);
                Assert.Equal("-SingleplayerResume", item.ResumeAction);
                Assert.True(Control<Button>(window, "ContinueButton").IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        [AvaloniaFact]
        public async Task ANewSelectionIsReadImmediatelyAfterTheCurrentSnapshot()
        {
            using SaveFixture fixture = new SaveFixture();
            SavedGameItem first = await fixture.WriteSave(ActivityType.Activity);
            SavedGameItem second = await fixture.WriteSave(ActivityType.Explorer);
            SavedGameItem selected = await fixture.WriteSave(ActivityType.TimeTable);
            SavedGamesWindow window = CreateWindow(first, second, selected);
            List<SavedGameItem> loaded = new List<SavedGameItem>();
            first.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SavedGameItem.Summary))
                {
                    loaded.Add(first);
                    Control<ListBox>(window, "SaveList").SelectedItem = selected;
                }
            };
            second.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SavedGameItem.Summary))
                    loaded.Add(second);
            };
            selected.PropertyChanged += (_, change) =>
            {
                if (change.PropertyName == nameof(SavedGameItem.Summary))
                    loaded.Add(selected);
            };
            try
            {
                await (Task)Call(window, "LoadDetails");

                Assert.Equal(new[] { first, selected, second }, loaded);
                Assert.True(selected.DetailsLoaded);
                Assert.Equal("-SinglePlayerResumeTimetableGame", selected.ResumeAction);
                Assert.True(Control<Button>(window, "ContinueButton").IsEnabled);
            }
            finally
            {
                window.Close();
            }
        }

        private static SavedGamesWindow CreateWindow(params SavedGameItem[] items)
        {
            SavedGamesWindow window = new SavedGamesWindow(Array.Empty<RouteItem>());
            FieldInfo field = typeof(SavedGamesWindow).GetField("saves", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            List<SavedGameItem> saves = Assert.IsType<List<SavedGameItem>>(field.GetValue(window));
            saves.Clear();
            saves.AddRange(items);
            Control<ListBox>(window, "SaveList").ItemsSource = saves;
            Control<ListBox>(window, "SaveList").SelectedItem = items[0];
            Call(window, "ShowSelection");
            return window;
        }

        private static CancellationTokenSource ClosingToken(SavedGamesWindow window)
        {
            FieldInfo field = typeof(SavedGamesWindow).GetField("closing", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return Assert.IsType<CancellationTokenSource>(field.GetValue(window));
        }

        private static T Control<T>(SavedGamesWindow window, string name) where T : Control
        {
            return Assert.IsAssignableFrom<T>(window.FindControl<T>(name));
        }

        private static object Call(SavedGamesWindow window, string name, params object[] arguments)
        {
            MethodInfo method = typeof(SavedGamesWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return method.Invoke(window, arguments);
        }

        private sealed class SaveFixture : IDisposable
        {
            public string Directory { get; } = Path.Combine(Path.GetTempPath(), "riel-save-picker-" + Guid.NewGuid().ToString("N"));

            public SaveFixture()
            {
                System.IO.Directory.CreateDirectory(Directory);
            }

            public async Task<SavedGameItem> WriteSave(ActivityType type, bool valid = true)
            {
                GameSaveState snapshot = new GameSaveState
                {
                    GameVersion = "regression-test",
                    Route = "coast",
                    Path = "main-line",
                    GameTime = 8 * 3600,
                    ProfileSelections = new ProfileSelectionsModel
                    {
                        ActivityType = type,
                        GamePlayAction = type == ActivityType.TimeTable
                            ? GamePlayAction.SinglePlayerTimetableGame : GamePlayAction.SingleplayerNewGame,
                        FolderName = "Fixture",
                        RouteId = "coast",
                        WagonSetId = "passenger",
                        TimetableSet = type == ActivityType.TimeTable ? "weekday" : null,
                        TimetableName = type == ActivityType.TimeTable ? "morning" : null,
                        TimetableTrain = type == ActivityType.TimeTable ? "101" : null,
                    },
                };
                if (!valid)
                {
                    FieldInfo marker = typeof(GameSaveState).GetField("finalMarker", BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(marker);
                    marker.SetValue(snapshot, "incomplete snapshot");
                }
                string file = Path.Combine(Directory, "fixture-" + Guid.NewGuid().ToString("N") + ".save");
                await SaveStateBase.ToFile(file, snapshot, CancellationToken.None);
                return new SavedGameItem(new FileInfo(file));
            }

            public void Dispose()
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
    }
}
