// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel and is distributed under the GNU General Public License v3 or later.

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;

using Riel.Common.Info;
using Riel.Models.Imported.State;

using Riel.Launcher;

using static Riel.Launcher.Gui.Translation;

namespace Riel.Launcher.Gui
{
    public sealed record SavedGameChoice(string File, string Action);

    public sealed class SavedGameItem : INotifyPropertyChanged
    {
        public SavedGameItem(FileInfo file) => File = file;

        public event PropertyChangedEventHandler PropertyChanged;
        public FileInfo File { get; }
        public string FileName => File.Name;
        private string summary = T("Reading save details…");
        public string Summary
        {
            get => summary;
            set
            {
                summary = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Summary)));
            }
        }
        public string Details { get; set; } = T("Reading save details…");
        public bool CanResume { get; set; } = true;
        public string ResumeAction { get; set; } = "-SingleplayerResume";
        public bool DetailsLoaded { get; set; }
        public string Name
        {
            get
            {
                // Open Rails names saves after the activity or route followed by a timestamp.
                // Keep the original file name visible too, so similarly named runs stay distinct.
                string stem = Path.GetFileNameWithoutExtension(File.Name);
                int timestamp = stem.LastIndexOf(" 20", StringComparison.Ordinal);
                return timestamp > 0 ? stem.Substring(0, timestamp) : stem;
            }
        }
        public string SavedAt => File.LastWriteTime.ToString("f", CultureInfo.CurrentCulture);
    }

    public partial class SavedGamesWindow : Window
    {
        private readonly IReadOnlyList<RouteItem> routes;
        private readonly List<SavedGameItem> saves;
        private readonly CancellationTokenSource closing = new CancellationTokenSource();
        private Bitmap preview;

        public SavedGamesWindow(IReadOnlyList<RouteItem> routes)
        {
            this.routes = routes;
            InitializeComponent();
            saves = Selections.SavedGames().Select(file => new SavedGameItem(file)).ToList();
            SaveList.ItemsSource = saves;
            SaveList.SelectionChanged += (_, _) => ShowSelection();
            SaveList.DoubleTapped += (_, _) => Accept();
            ContinueButton.Click += (_, _) => Accept();
            ReplayStartButton.Click += (_, _) => ChooseReplay("-SingleplayerReplay");
            ReplayPreviousButton.Click += (_, _) => ChooseReplay("-SingleplayerReplayFromSave");
            DeleteButton.Click += async (_, _) => await DeleteSelected();
            DeleteInvalidButton.Click += async (_, _) => await DeleteInvalid();
            RestoreButton.Click += async (_, _) => await RestoreDeleted();
            ImportButton.Click += async (_, _) => await ImportSavePack();
            ExportButton.Click += async (_, _) => await ExportSavePack();
            CancelButton.Click += (_, _) => Close(null);
            Opened += async (_, _) => await LoadDetails();
            Closed += (_, _) =>
            {
                closing.Cancel();
                Screenshot.Source = null;
                preview?.Dispose();
            };
            RefreshRestoreButton();
        }

        private async Task LoadDetails()
        {
            // Read snapshots one at a time off the UI thread. After each read, prioritize the
            // selected save so Continue does not wait behind every older/newer snapshot.
            List<SavedGameItem> pending = saves.ToList();
            while (pending.Count > 0)
            {
                if (closing.IsCancellationRequested)
                    return;
                SavedGameItem item = SaveList.SelectedItem as SavedGameItem;
                if (item == null || !pending.Remove(item))
                {
                    item = pending[0];
                    pending.RemoveAt(0);
                }
                if (item.DetailsLoaded || !item.File.Exists)
                    continue;
                try
                {
                    GameSaveState state = await Task.Run(async () =>
                        await GameSaveState.FromFile<GameSaveState>(item.File.FullName, closing.Token), closing.Token);
                    if (closing.IsCancellationRequested)
                        return;

                    string folder = state.ProfileSelections?.FolderName;
                    string routeId = state.Route ?? state.ProfileSelections?.RouteId;
                    RouteItem route = routes.FirstOrDefault(candidate =>
                        string.Equals(candidate.Route.Id, routeId, StringComparison.OrdinalIgnoreCase) &&
                        (folder == null || string.Equals(candidate.Folder.Name, folder, StringComparison.OrdinalIgnoreCase)));
                    string routeName = route?.Name ?? routeId ?? T("Unknown route");
                    string train = state.ProfileSelections?.WagonSetId ?? state.ProfileSelections?.TimetableTrain;
                    if (string.IsNullOrWhiteSpace(train))
                        train = state.SimulatorSaveState?.Trains?.FirstOrDefault()?.TrainName;
                    if (string.IsNullOrWhiteSpace(train))
                        train = state.SimulatorSaveState?.Trains?.FirstOrDefault()?.TrainCars?.FirstOrDefault()?.OriginalConsist;
                    if (string.IsNullOrWhiteSpace(train))
                        train = T("Unknown train");
                    string path = state.Path ?? state.ProfileSelections?.PathId ?? state.ProfileSelections?.ActivityId;
                    string timetable = state.ProfileSelections?.ActivityType == Riel.Common.ActivityType.TimeTable
                        ? state.ProfileSelections?.TimetableName ?? state.ProfileSelections?.TimetableSet
                        : null;
                    string when = TimeSpan.FromSeconds(state.GameTime).ToString(@"hh\:mm\:ss", CultureInfo.CurrentCulture);
                    item.Summary = $"{routeName} · {train} · {when}";
                    item.Details = $"{T("Route")}: {routeName}\n{T("Train")}: {train}\n" +
                        (string.IsNullOrWhiteSpace(timetable) ? string.Empty : $"{T("Timetable")}: {timetable}\n") +
                        (string.IsNullOrWhiteSpace(path) ? string.Empty : $"{T("Path")}: {path}\n") +
                        $"{T("Game time")}: {when}\n{T("Save version")}: {state.GameVersion}";
                    item.CanResume = state.Valid != false;
                    item.ResumeAction = state.ProfileSelections?.ActivityType == Riel.Common.ActivityType.TimeTable ||
                        state.ProfileSelections?.GamePlayAction == Riel.Common.GamePlayAction.SinglePlayerTimetableGame ||
                        state.ProfileSelections?.GamePlayAction == Riel.Common.GamePlayAction.SinglePlayerResumeTimetableGame
                        ? "-SinglePlayerResumeTimetableGame"
                        : "-SingleplayerResume";
                    if (state.Valid == false)
                        item.Details += "\n" + T("This save is incomplete and cannot be continued.");
                }
                catch (OperationCanceledException) when (closing.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // The details panel is optional metadata. A preview/deserialization
                    // failure here must not prevent the simulator from attempting its
                    // normal restore path, especially for saves from older Riel builds.
                    // Only a snapshot which was successfully read and explicitly marked
                    // invalid above is blocked.
                    item.Summary = T("Save details unavailable");
                    item.Details = F("Could not read this save: {0}", ex.Message);
                    item.CanResume = true;
                    item.ResumeAction = "-SingleplayerResume";
                }
                item.DetailsLoaded = true;
                if (SaveList.SelectedItem == item)
                    ShowSelection();
            }
        }

        private void ShowSelection()
        {
            Screenshot.Source = null;
            preview?.Dispose();
            preview = null;
            SavedGameItem chosen = SaveList.SelectedItem as SavedGameItem;
            // The metadata determines both validity and the simulator's resume action.
            // Do not launch using the provisional defaults while that read is pending.
            ContinueButton.IsEnabled = chosen?.DetailsLoaded == true && chosen.CanResume;
            DeleteButton.IsEnabled = chosen != null;
            ExportButton.IsEnabled = chosen != null;
            bool replay = chosen != null && File.Exists(Path.ChangeExtension(chosen.File.FullName, ".replay"));
            ReplayStartButton.IsEnabled = replay;
            ReplayPreviousButton.IsEnabled = replay && chosen.DetailsLoaded && chosen.CanResume;
            DeleteInvalidButton.IsEnabled = saves.Any(item => item.DetailsLoaded && !item.CanResume);
            SelectedDetails.Text = chosen?.Details ?? string.Empty;
            SelectedFile.Text = chosen?.File.FullName ?? string.Empty;

            string screenshot = chosen == null ? null : Path.ChangeExtension(chosen.File.FullName, ".png");
            if (screenshot != null)
            {
                try
                {
                    if (File.Exists(screenshot))
                        Screenshot.Source = preview = new Bitmap(screenshot);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException)
                {
                    // A missing or unreadable screenshot must not prevent resuming the save.
                }
            }
            Screenshot.IsVisible = preview != null;
            NoScreenshot.IsVisible = preview == null;
        }

        private void Accept()
        {
            if (SaveList.SelectedItem is SavedGameItem chosen && chosen.DetailsLoaded && chosen.CanResume)
                Close(new SavedGameChoice(chosen.File.FullName, chosen.ResumeAction));
        }

        private void ChooseReplay(string action)
        {
            if (SaveList.SelectedItem is SavedGameItem chosen &&
                (action != "-SingleplayerReplayFromSave" || chosen.DetailsLoaded && chosen.CanResume) &&
                File.Exists(Path.ChangeExtension(chosen.File.FullName, ".replay")))
                Close(new SavedGameChoice(chosen.File.FullName, action));
        }

        private async Task DeleteInvalid()
        {
            SavedGameItem[] invalid = saves.Where(item => item.DetailsLoaded && !item.CanResume).ToArray();
            if (invalid.Length == 0 || !await Dialogs.Confirm(this, T("Delete invalid saves?"),
                F("Move {0} invalid saves to Deleted Saves?", invalid.Length), T("Delete invalid saves"), T("Cancel")))
                return;
            foreach (SavedGameItem item in invalid)
                await MoveToDeleted(item);
            RefreshSaves();
            RefreshRestoreButton();
            ShowSelection();
        }

        private async Task DeleteSelected()
        {
            if (SaveList.SelectedItem is not SavedGameItem chosen)
                return;
            if (!await Dialogs.Confirm(this, T("Delete saved game?"),
                F("Move {0} and its screenshot and replay to Deleted Saves? You can restore them later.", chosen.File.Name),
                T("Delete save"), T("Cancel")))
                return;

            await MoveToDeleted(chosen);
            RefreshSaves();
            RefreshRestoreButton();
        }

        private async Task MoveToDeleted(SavedGameItem chosen)
        {
            try
            {
                string target = RuntimeInfo.DeletedSaveFolder;
                Directory.CreateDirectory(target);
                string source = chosen.File.FullName;
                Screenshot.Source = null;
                preview?.Dispose();
                preview = null;
                string[] companions = { ".png", ".replay", ".txt", ".evaluation.txt" };
                string[] files = companions.Select(extension => Path.ChangeExtension(source, extension))
                    .Where(File.Exists).Append(source).ToArray();
                if (files.Any(file => File.Exists(Path.Combine(target, Path.GetFileName(file)))))
                    throw new IOException(T("A deleted save with the same name already exists. Restore it first."));
                // Move the save last: a failed companion move cannot hide a playable save.
                foreach (string file in files)
                    File.Move(file, Path.Combine(target, Path.GetFileName(file)));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await Dialogs.Message(this, T("Could not delete saved game"), ex.Message);
            }
        }

        private async Task ExportSavePack()
        {
            if (SaveList.SelectedItem is not SavedGameItem chosen)
                return;
            try
            {
                Directory.CreateDirectory(RuntimeInfo.SavePackFolder);
                string destination = Path.Combine(RuntimeInfo.SavePackFolder,
                    Path.GetFileNameWithoutExtension(chosen.File.Name) + ".ORSavePack");
                string[] files = Directory.EnumerateFiles(RuntimeInfo.UserDataFolder,
                    Path.GetFileNameWithoutExtension(chosen.File.Name) + ".*")
                    .Where(file => Path.GetFileNameWithoutExtension(file) == Path.GetFileNameWithoutExtension(chosen.File.Name) ||
                        Path.GetFileName(file) == Path.GetFileNameWithoutExtension(chosen.File.Name) + ".evaluation.txt")
                    .ToArray();
                if (File.Exists(destination) && !await Dialogs.Confirm(this, T("Replace save pack?"),
                    F("A save pack named {0} already exists. Replace it?", Path.GetFileName(destination)),
                    T("Replace"), T("Cancel")))
                    return;
                string temporary = destination + ".tmp";
                try
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                    using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                        foreach (string file in files)
                            archive.CreateEntryFromFile(file, Path.GetFileName(file));
                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
                await Dialogs.Message(this, T("Save pack exported"), destination);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await Dialogs.Message(this, T("Could not export save pack"), ex.Message);
            }
        }

        private async Task ImportSavePack()
        {
            IReadOnlyList<IStorageFile> picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = T("Import save pack"), AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Open Rails Save Pack") { Patterns = new[] { "*.ORSavePack" } } },
            });
            string selected = picked.FirstOrDefault()?.TryGetLocalPath();
            if (selected == null)
                return;
            try
            {
                using ZipArchive archive = ZipFile.OpenRead(selected);
                ZipArchiveEntry[] entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToArray();
                if (entries.Length == 0 || !entries.Any(entry => entry.Name.EndsWith(".save", StringComparison.OrdinalIgnoreCase)) ||
                    entries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
                    entries.Any(entry => entry.FullName != entry.Name || entry.Name is "." or ".." ||
                        entry.Name.Contains('/') || entry.Name.Contains('\\') ||
                        File.Exists(Path.Combine(RuntimeInfo.UserDataFolder, entry.Name))))
                    throw new InvalidDataException(T("The pack is empty, contains unsafe paths, or would overwrite an existing save."));
                foreach (ZipArchiveEntry entry in entries)
                    entry.ExtractToFile(Path.Combine(RuntimeInfo.UserDataFolder, entry.Name));
                RefreshSaves();
                await LoadDetails();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidDataException)
            {
                await Dialogs.Message(this, T("Could not import save pack"), ex.Message);
            }
        }

        private async Task RestoreDeleted()
        {
            try
            {
                if (!Directory.Exists(RuntimeInfo.DeletedSaveFolder))
                    return;
                int restored = 0;
                foreach (string file in Directory.EnumerateFiles(RuntimeInfo.DeletedSaveFolder))
                {
                    string destination = Path.Combine(RuntimeInfo.UserDataFolder, Path.GetFileName(file));
                    if (File.Exists(destination))
                        continue;
                    File.Move(file, destination);
                    restored++;
                }
                RefreshSaves();
                RefreshRestoreButton();
                await LoadDetails();
                await Dialogs.Message(this, T("Saves restored"), F("Restored {0} files.", restored));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                await Dialogs.Message(this, T("Could not restore saved games"), ex.Message);
            }
        }

        private void RefreshSaves()
        {
            SavedGameItem[] updated = Selections.SavedGames().Select(file =>
                saves.FirstOrDefault(item => item.File.FullName == file.FullName) ?? new SavedGameItem(file)).ToArray();
            saves.Clear();
            saves.AddRange(updated);
            SaveList.ItemsSource = updated;
        }

        private void RefreshRestoreButton() => RestoreButton.IsEnabled =
            Directory.Exists(RuntimeInfo.DeletedSaveFolder) &&
            Directory.EnumerateFiles(RuntimeInfo.DeletedSaveFolder).Any();
    }
}
