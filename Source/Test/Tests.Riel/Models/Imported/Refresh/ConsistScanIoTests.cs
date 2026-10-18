// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel, a fork of Open Rails, under the GPL version 3 or later.

using System;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Riel.Models.Content;
using Riel.Models.Handler;
using Riel.Models.Imported.ImportHandler;
using Riel.Models.Imported.ImportHandler.TrainSimulator;

using Orts.Formats.Msts.Files;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Riel.Models.Imported.Refresh
{
    [TestClass]
    [DoNotParallelize]
    public class ConsistScanIoTests
    {
        [TestMethod]
        public async Task SharedLocomotiveIsReadOnceAcrossAllConsistsAndDuplicateEngines()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("SHARED", "Shared locomotive", "Shared description");
            for (int index = 0; index < 100; index++)
                fixture.WriteConsist("Train" + index, "SHARED", "SHARED");
            int reads = 0;

            ImmutableArray<WagonSetModel> result = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder,
                CancellationToken.None, path =>
                {
                    Interlocked.Increment(ref reads);
                    return new EngineFile(path);
                });

            Assert.HasCount(100, result);
            Assert.AreEqual(1, reads, "200 locomotive references should cause one source parse during this scan.");
            foreach (WagonSetModel consist in result)
            {
                Assert.HasCount(2, consist.TrainCars);
                Assert.AreEqual("Shared locomotive", consist.TrainCars[0].Name);
                Assert.AreEqual("Shared description", consist.TrainCars[1].Description);
                Assert.AreEqual("TestStock", consist.TrainCars[0].Reference);
                Assert.AreEqual(0, consist.TrainCars[0].Uid);
                Assert.AreEqual(1, consist.TrainCars[1].Uid);
                Assert.IsFalse(consist.TrainCars[0].Reverse);
                Assert.IsTrue(consist.TrainCars[1].Reverse);
                Assert.AreEqual(40f, consist.MaximumSpeed);
            }
        }

        [TestMethod]
        public async Task NextScanReadsEditedEngineAndDiscoversNewConsist()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("SHARED", "Before edit", "Old description");
            fixture.WriteConsist("Original", "SHARED");
            ImmutableArray<WagonSetModel> first = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder, CancellationToken.None);
            Assert.AreEqual("Before edit", first.Single().TrainCars.Single().Name);

            fixture.WriteEngine("SHARED", "After edit", "Updated description");
            fixture.WriteConsist("NewTrain", "SHARED");
            int reads = 0;
            ImmutableArray<WagonSetModel> second = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder,
                CancellationToken.None, path =>
                {
                    Interlocked.Increment(ref reads);
                    return new EngineFile(path);
                });

            Assert.HasCount(2, second);
            Assert.AreEqual(1, reads, "A new scan must read the source again, even when its path was already imported.");
            Assert.IsTrue(second.All(consist => consist.TrainCars.Single().Name == "After edit"));
            Assert.IsTrue(second.All(consist => consist.TrainCars.Single().Description == "Updated description"));
        }

        [TestMethod]
        public async Task MissingAndCablessEnginesKeepTheirOriginalReferenceNames()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("CABLESS", "Unusable display name", "Cabless description", hasCab: false);
            fixture.WriteConsist("Missing", "ABSENT");
            fixture.WriteConsist("Cabless", "CABLESS");

            ImmutableArray<WagonSetModel> result = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder, CancellationToken.None);

            Assert.HasCount(2, result);
            Assert.AreEqual("ABSENT", result.Single(consist => consist.Id == "Missing").TrainCars.Single().Name);
            Assert.AreEqual("CABLESS", result.Single(consist => consist.Id == "Cabless").TrainCars.Single().Name);
            Assert.IsNull(result.Single(consist => consist.Id == "Cabless").TrainCars.Single().Description);
        }

        [TestMethod]
        public async Task DistinctEngineFilesAreReadSeparately()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("FIRST", "First locomotive", "First description");
            fixture.WriteEngine("SECOND", "Second locomotive", "Second description");
            fixture.WriteConsist("Mixed", "FIRST", "SECOND");
            int reads = 0;

            ImmutableArray<WagonSetModel> result = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder,
                CancellationToken.None, path =>
                {
                    Interlocked.Increment(ref reads);
                    return new EngineFile(path);
                });

            Assert.AreEqual(2, reads);
            Assert.AreEqual("First locomotive", result.Single().TrainCars[0].Name);
            Assert.AreEqual("Second locomotive", result.Single().TrainCars[1].Name);
        }

        [TestMethod]
        public async Task UnreadableEngineSkipsOnlyItsConsistsAndNextScanRetriesIt()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("BROKEN", "Recovered locomotive", "Recovered description");
            fixture.WriteEngine("VALID", "Valid locomotive", "Valid description");
            fixture.WriteConsist("BrokenOne", "BROKEN");
            fixture.WriteConsist("BrokenTwo", "BROKEN");
            fixture.WriteConsist("ValidTrain", "VALID");
            int failedReads = 0;
            _ = ImportFailures.Take();
            try
            {
                ImmutableArray<WagonSetModel> first = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder,
                    CancellationToken.None, path =>
                    {
                        if (Path.GetFileNameWithoutExtension(path) == "BROKEN")
                        {
                            Interlocked.Increment(ref failedReads);
                            throw new IOException("Simulated engine read failure");
                        }
                        return new EngineFile(path);
                    });

                Assert.AreEqual(1, failedReads);
                Assert.AreEqual("ValidTrain", first.Single().Id);
                Assert.AreEqual("Valid locomotive", first.Single().TrainCars.Single().Name);
                var skipped = ImportFailures.Take();
                Assert.HasCount(2, skipped);
                Assert.IsTrue(skipped.All(file => file.Kind == "consist" && file.Reason == "Simulated engine read failure"));

                int recoveredReads = 0;
                ImmutableArray<WagonSetModel> second = await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder,
                    CancellationToken.None, path =>
                    {
                        if (Path.GetFileNameWithoutExtension(path) == "BROKEN")
                            Interlocked.Increment(ref recoveredReads);
                        return new EngineFile(path);
                    });

                Assert.HasCount(3, second);
                Assert.AreEqual(1, recoveredReads, "A failed engine read must not stay cached across scans.");
                Assert.AreEqual("Recovered locomotive", second.Single(consist => consist.Id == "BrokenOne").TrainCars.Single().Name);
                Assert.HasCount(0, ImportFailures.Take());
            }
            finally
            {
                _ = ImportFailures.Take();
            }
        }

        [TestMethod]
        public async Task CancellationWhileReadingEnginePropagatesWithoutRecordingBadContent()
        {
            using Fixture fixture = new Fixture();
            fixture.WriteEngine("SHARED", "Shared locomotive", "Shared description");
            fixture.WriteConsist("Cancelled", "SHARED");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            int reads = 0;
            _ = ImportFailures.Take();
            try
            {
                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await WagonSetModelImportHandler.ExpandWagonSetModels(fixture.Folder, cancellation.Token, path =>
                    {
                        Interlocked.Increment(ref reads);
                        cancellation.Cancel();
                        cancellation.Token.ThrowIfCancellationRequested();
                        return new EngineFile(path);
                    }));

                Assert.AreEqual(1, reads, "The fixture cancels during the source read, after scanning has started.");
                Assert.HasCount(0, ImportFailures.Take(), "Cancellation is not an unreadable content file.");
            }
            finally
            {
                _ = ImportFailures.Take();
            }
        }

        private sealed class Fixture : IDisposable
        {
            private readonly string root = Path.Combine(Path.GetTempPath(), "riel-consist-io-" + Guid.NewGuid().ToString("N"));
            private readonly string previousRoot = ModelStore.ContentRoot;

            internal FolderModel Folder { get; }

            internal Fixture()
            {
                Directory.CreateDirectory(Path.Combine(root, "source", "TRAINS", "CONSISTS"));
                Directory.CreateDirectory(Path.Combine(root, "source", "TRAINS", "TRAINSET", "TestStock"));
                ModelStore.RedirectContentRoot(Path.Combine(root, "cache"));
                Folder = new FolderModel("ConsistIo-" + Guid.NewGuid().ToString("N"), Path.Combine(root, "source"), new ContentModel());
            }

            internal void WriteEngine(string id, string name, string description, bool hasCab = true)
            {
                string cab = hasCab ? "CabView ( test.cvf )" : string.Empty;
                File.WriteAllText(Path.Combine(root, "source", "TRAINS", "TRAINSET", "TestStock", id + ".eng"),
                    $"SIMISA@@@@@@@@@@JINX0D0t______\nEngine ( {id} Name ( \"{name}\" ) Description ( \"{description}\" ) {cab} )\n", new UTF8Encoding(false));
            }

            internal void WriteConsist(string id, params string[] engines)
            {
                string cars = string.Join("\n", engines.Select((engine, index) =>
                    $"Engine ( UiD ( {index} ) {(index % 2 == 1 ? "Flip ( )" : string.Empty)} EngineData ( {engine} TestStock ) )"));
                File.WriteAllText(Path.Combine(root, "source", "TRAINS", "CONSISTS", id + ".con"),
                    $"SIMISA@@@@@@@@@@JINX0D0t______\nTrain ( TrainCfg ( \"{id}\" Name ( \"{id}\" ) MaxVelocity ( 40 1 ) Durability ( 1 ) {cars} ) )\n", new UTF8Encoding(false));
            }

            public void Dispose()
            {
                ModelStore.RedirectContentRoot(previousRoot);
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
