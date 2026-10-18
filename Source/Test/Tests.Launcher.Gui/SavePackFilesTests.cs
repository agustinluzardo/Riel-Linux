// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel and is distributed under the GNU General Public License v3 or later.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Riel.Launcher.Gui;

using Xunit;

namespace Tests.Launcher.Gui
{
    public class SavePackFilesTests
    {
        [Fact]
        public async Task SaveAndAllCompanionsRoundTripWithoutIncludingOtherSaves()
        {
            using Fixture fixture = new Fixture();
            string[] extensions = { ".save", ".png", ".replay", ".txt", ".evaluation.txt" };
            foreach (string extension in extensions)
                File.WriteAllText(Path.Combine(fixture.Source, "journey" + extension), extension);
            DateTime archivedDate = new DateTime(2025, 4, 12, 10, 20, 30);
            File.SetLastWriteTime(Path.Combine(fixture.Source, "journey.save"), archivedDate);
            File.WriteAllText(Path.Combine(fixture.Source, "other.save"), "unrelated");

            await SavePackFiles.ExportAsync(Path.Combine(fixture.Source, "journey.save"), fixture.Pack, false, CancellationToken.None);
            await SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, CancellationToken.None);

            Assert.Equal(extensions.Length, Directory.GetFiles(fixture.Destination).Length);
            foreach (string extension in extensions)
                Assert.Equal(extension, File.ReadAllText(Path.Combine(fixture.Destination, "journey" + extension)));
            Assert.Empty(Directory.GetDirectories(fixture.Destination));
            Assert.Equal(archivedDate, File.GetLastWriteTime(Path.Combine(fixture.Destination, "journey.save")));
        }

        [Fact]
        public async Task AFailedLaterEntryLeavesNoPartialImportAndTheCorrectedPackCanBeRetried()
        {
            using Fixture fixture = new Fixture();
            fixture.WritePack();
            byte[] validPack = File.ReadAllBytes(fixture.Pack);
            byte[] brokenPack = (byte[])validPack.Clone();
            // The final entry is deflated. An invalid BTYPE forces decompression to fail
            // after the first entry has been successfully copied to staging.
            int dataOffset;
            using (ZipArchive archive = ZipFile.OpenRead(fixture.Pack))
            {
                Assert.Equal(2, archive.Entries.Count);
                int firstNameLength = BitConverter.ToUInt16(brokenPack, 26);
                int firstExtraLength = BitConverter.ToUInt16(brokenPack, 28);
                int secondHeader = 30 + firstNameLength + firstExtraLength + (int)archive.Entries[0].CompressedLength;
                Assert.Equal(0x04034b50, BitConverter.ToInt32(brokenPack, secondHeader));
                dataOffset = secondHeader + 30 + BitConverter.ToUInt16(brokenPack, secondHeader + 26) + BitConverter.ToUInt16(brokenPack, secondHeader + 28);
            }
            brokenPack[dataOffset] = 0x07;
            File.WriteAllBytes(fixture.Pack, brokenPack);

            await Assert.ThrowsAsync<InvalidDataException>(() => SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, CancellationToken.None));
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));

            File.WriteAllBytes(fixture.Pack, validPack);
            await SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, CancellationToken.None);
            Assert.Equal("snapshot", File.ReadAllText(Path.Combine(fixture.Destination, "journey.save")));
            Assert.Equal(new string('x', 4096), File.ReadAllText(Path.Combine(fixture.Destination, "journey.replay")));
        }

        [Fact]
        public async Task ImportNeverOverwritesAnExistingCompanionOrCreatesTheOtherEntries()
        {
            using Fixture fixture = new Fixture();
            fixture.WritePack();
            string existing = Path.Combine(fixture.Destination, "journey.replay");
            File.WriteAllText(existing, "keep me");
            await Assert.ThrowsAsync<InvalidDataException>(() => SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, CancellationToken.None));
            Assert.Equal("keep me", File.ReadAllText(existing));
            Assert.Single(Directory.GetFileSystemEntries(fixture.Destination));
        }

        [Fact]
        public async Task ExportKeepsSupportingDatesOutsideTheZipTimestampRange()
        {
            using Fixture fixture = new Fixture();
            string save = Path.Combine(fixture.Source, "journey.save");
            File.WriteAllText(save, "snapshot");
            File.SetLastWriteTime(save, new DateTime(1970, 1, 1));
            await SavePackFiles.ExportAsync(save, fixture.Pack, false, CancellationToken.None);
            using ZipArchive archive = ZipFile.OpenRead(fixture.Pack);
            Assert.Equal(1980, archive.Entries.Single().LastWriteTime.Year);
        }

        [Fact]
        public async Task FailedExportPreservesThePreviousPackAndRemovesItsTemporaryFile()
        {
            using Fixture fixture = new Fixture();
            string save = Path.Combine(fixture.Source, "journey.save");
            File.WriteAllText(save, "snapshot");
            File.WriteAllText(fixture.Pack, "previous pack");
            using (FileStream locked = new FileStream(save, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                await Assert.ThrowsAnyAsync<IOException>(() => SavePackFiles.ExportAsync(save, fixture.Pack, true, CancellationToken.None));
            Assert.Equal("previous pack", File.ReadAllText(fixture.Pack));
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        }

        [Fact]
        public async Task ExportDoesNotReplaceAPackWithoutPermission()
        {
            using Fixture fixture = new Fixture();
            string save = Path.Combine(fixture.Source, "journey.save");
            File.WriteAllText(save, "snapshot");
            File.WriteAllText(fixture.Pack, "previous pack");
            await Assert.ThrowsAnyAsync<IOException>(() => SavePackFiles.ExportAsync(save, fixture.Pack, false, CancellationToken.None));
            Assert.Equal("previous pack", File.ReadAllText(fixture.Pack));
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        }

        [Fact]
        public async Task CancelledOperationsLeaveExistingFilesAlone()
        {
            using Fixture fixture = new Fixture();
            fixture.WritePack();
            byte[] original = File.ReadAllBytes(fixture.Pack);
            string save = Path.Combine(fixture.Source, "journey.save");
            File.WriteAllText(save, "snapshot");
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SavePackFiles.ExportAsync(save, fixture.Pack, true, cancelled.Token));
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, cancelled.Token));
            Assert.Equal(original, File.ReadAllBytes(fixture.Pack));
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));
            Assert.Empty(Directory.GetFiles(fixture.Root, "*.tmp"));
        }

        [Theory]
        [InlineData("../journey.save", "journey.replay")]
        [InlineData("journey.save", "JOURNEY.save")]
        public async Task UnsafeOrDuplicatePackNamesRemainRejected(string first, string second)
        {
            using Fixture fixture = new Fixture();
            fixture.WritePack(first, second);
            await Assert.ThrowsAsync<InvalidDataException>(() => SavePackFiles.ImportAsync(fixture.Pack, fixture.Destination, CancellationToken.None));
            Assert.Empty(Directory.GetFileSystemEntries(fixture.Destination));
        }

        private sealed class Fixture : IDisposable
        {
            public string Root { get; } = Path.Combine(Path.GetTempPath(), "riel-pack-test-" + Guid.NewGuid().ToString("N"));
            public string Source => Path.Combine(Root, "source");
            public string Destination => Path.Combine(Root, "imported");
            public string Pack => Path.Combine(Root, "journey.ORSavePack");

            public Fixture()
            {
                Directory.CreateDirectory(Source);
                Directory.CreateDirectory(Destination);
            }

            public void WritePack(string first = "journey.save", string second = "journey.replay")
            {
                using ZipArchive archive = ZipFile.Open(Pack, ZipArchiveMode.Create);
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry(first, CompressionLevel.Optimal).Open()))
                    writer.Write("snapshot");
                using (StreamWriter writer = new StreamWriter(archive.CreateEntry(second, CompressionLevel.Optimal).Open()))
                    writer.Write(new string('x', 4096));
            }

            public void Dispose() => Directory.Delete(Root, recursive: true);
        }
    }
}
