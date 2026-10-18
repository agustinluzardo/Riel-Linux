// COPYRIGHT 2026 by the Riel project.
// This file is part of Riel, a fork of Open Rails, under the GPL version 3 or later.

using System;
using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

using Riel.Models.Content;
using Riel.Models.Handler;
using Riel.Models.Imported.ImportHandler;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Tests.Riel.Models.Imported.Refresh
{
    [TestClass]
    [DoNotParallelize]
    public class ContentImportCancellationTests
    {
        [TestMethod]
        public async Task CancellingAfterTheFirstFolderStopsTheRemainingImportWork()
        {
            string fixtureRoot = Path.Combine(Path.GetTempPath(), "riel-import-cancel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(fixtureRoot);
            string previousRoot = ModelStore.ContentRoot;
            ModelStore.RedirectContentRoot(fixtureRoot);
            ClearContentCache();
            try
            {
                // More folders than the default worker count ensures some are still queued
                // when the first completion cancels the token. No timing or sleeps are needed.
                int folderCount = Math.Max(8, Environment.ProcessorCount * 2 + 2);
                ContentModel content = new ContentModel();
                content = content with
                {
                    ContentFolders = Enumerable.Range(0, folderCount)
                        .Select(index => new FolderModel("Cancel-" + Guid.NewGuid().ToString("N"),
                            Path.Combine(fixtureRoot, index.ToString()), content))
                        .ToImmutableArray(),
                };
                content.Initialize(null);
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                CancelAfterFirstFolder progress = new CancelAfterFirstFolder(cancellation);

                await Assert.ThrowsAsync<OperationCanceledException>(async () =>
                    await ContentModelConverter.SetupContent(content, true, progress, cancellation.Token));

                Assert.IsTrue(progress.CompletedFolders > 0, "Cancellation must happen during folder conversion, not before the import starts.");
                Assert.IsTrue(progress.CompletedFolders < folderCount,
                    $"The import completed all {folderCount} folders after cancellation. The outer loop must observe the caller's token.");
            }
            finally
            {
                ModelStore.RedirectContentRoot(previousRoot);
                ClearContentCache();
                Directory.Delete(fixtureRoot, recursive: true);
            }
        }

        private static void ClearContentCache()
        {
            FieldInfo field = typeof(ContentHandlerBase<ContentModel>).GetField("modelTaskCache", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(field);
            ((ConcurrentDictionary<string, Task<ContentModel>>)field.GetValue(null)).Clear();
        }

        private sealed class CancelAfterFirstFolder : IProgress<int>
        {
            private readonly CancellationTokenSource cancellation;
            private readonly object gate = new object();
            private int completedFolders;

            public int CompletedFolders => Volatile.Read(ref completedFolders);

            public CancelAfterFirstFolder(CancellationTokenSource cancellation)
            {
                this.cancellation = cancellation;
            }

            public void Report(int value)
            {
                // Progress<T> posts asynchronously and could let another folder start before
                // cancellation. This callback runs inside the actual import worker instead.
                lock (gate)
                {
                    if (++completedFolders == 1)
                        cancellation.Cancel();
                }
            }
        }
    }
}
