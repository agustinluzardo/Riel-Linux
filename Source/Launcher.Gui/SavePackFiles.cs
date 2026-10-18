// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel and is distributed under the GNU General Public License v3 or later.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Riel.Launcher.Gui
{
    // Keep archive I/O outside the UI thread, and publish only after every entry is ready.
    public static class SavePackFiles
    {
        public static Task ImportAsync(string pack, string destination, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                Directory.CreateDirectory(destination);
                string staging = Path.Combine(destination, ".riel-import-" + Guid.NewGuid().ToString("N"));
                List<string> published = new List<string>();
                bool retainStaging = false;
                try
                {
                    using ZipArchive archive = ZipFile.OpenRead(pack);
                    ZipArchiveEntry[] entries = archive.Entries.Where(entry => entry.Name.Length > 0).ToArray();
                    if (entries.Length == 0 || !entries.Any(entry => entry.Name.EndsWith(".save", StringComparison.OrdinalIgnoreCase)) ||
                        entries.Select(entry => entry.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Length ||
                        entries.Any(entry => entry.FullName != entry.Name || entry.Name is "." or ".." ||
                            entry.Name.Contains('/') || entry.Name.Contains('\\') ||
                            File.Exists(Path.Combine(destination, entry.Name)) || Directory.Exists(Path.Combine(destination, entry.Name))))
                        throw new InvalidDataException("The pack is empty, contains unsafe paths, or would overwrite an existing save.");

                    Directory.CreateDirectory(staging);
                    foreach (ZipArchiveEntry entry in entries)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string stagedFile = Path.Combine(staging, entry.Name);
                        using (Stream source = entry.Open())
                        using (FileStream target = new FileStream(stagedFile, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                        // Match ExtractToFile: retain the archived save date and ordering.
                        File.SetLastWriteTime(stagedFile, entry.LastWriteTime.DateTime);
                    }
                    // Publish companions before snapshots so a visible save is ready to use.
                    foreach (ZipArchiveEntry entry in entries.OrderBy(entry => entry.Name.EndsWith(".save", StringComparison.OrdinalIgnoreCase)))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        string target = Path.Combine(destination, entry.Name);
                        File.Move(Path.Combine(staging, entry.Name), target); // Never overwrite, including a concurrent writer.
                        published.Add(target);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                }
                catch (Exception failure)
                {
                    List<Exception> rollbackFailures = new List<Exception>();
                    foreach (string file in published)
                        try
                        {
                            File.Delete(file);
                        }
                        catch (Exception rollbackFailure) when (rollbackFailure is IOException || rollbackFailure is UnauthorizedAccessException)
                        {
                            rollbackFailures.Add(rollbackFailure);
                        }
                    if (rollbackFailures.Count > 0)
                    {
                        // Preserve staged files and report the recovery location if disk/permissions
                        // prevent rollback. Never pretend a partial publication succeeded.
                        retainStaging = true;
                        rollbackFailures.Insert(0, failure);
                        throw new IOException("The import failed and some files could not be removed. Remaining staged files: " + staging,
                            new AggregateException(rollbackFailures));
                    }
                    throw;
                }
                finally
                {
                    if (!retainStaging && Directory.Exists(staging))
                        Directory.Delete(staging, recursive: true);
                }
            }, cancellationToken);

        public static Task ExportAsync(string save, string destination, bool overwrite, CancellationToken cancellationToken) =>
            Task.Run(async () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                string stem = Path.GetFileNameWithoutExtension(save);
                string[] files = Directory.EnumerateFiles(Path.GetDirectoryName(save))
                    .Where(file => Path.GetFileNameWithoutExtension(file) == stem || Path.GetFileName(file) == stem + ".evaluation.txt")
                    .ToArray();
                if (!File.Exists(save))
                    throw new FileNotFoundException("The saved game no longer exists.", save);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
                        foreach (string file in files)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            using FileStream source = File.OpenRead(file);
                            ZipArchiveEntry entry = archive.CreateEntry(Path.GetFileName(file));
                            DateTime modified = File.GetLastWriteTime(file);
                            entry.LastWriteTime = modified.Year >= 1980 && modified.Year <= 2107
                                ? modified : new DateTime(1980, 1, 1);
                            using Stream target = entry.Open();
                            await source.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                        }
                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, destination, overwrite);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }, cancellationToken);
    }
}
