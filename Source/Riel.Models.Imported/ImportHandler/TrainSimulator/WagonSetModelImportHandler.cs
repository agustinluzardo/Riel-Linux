using System;
using Riel.Common.Native;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Riel.Models.Content;
using Riel.Models.Handler;
using Riel.Models.Imported.Shim;

using Orts.Formats.Msts.Files;

namespace Riel.Models.Imported.ImportHandler.TrainSimulator
{
    internal sealed class WagonSetModelImportHandler : ContentHandlerBase<WagonSetModel>
    {
        internal const string SourceNameKey = "MstsSourceConsist";

        public static WagonSetModel Missing = new WagonSetModel()
        {
            Id = "<unknown>",
            Name = "Missing",
            TrainCars = ImmutableArray<WagonReferenceModel>.Empty
        };

        public static Task<ImmutableArray<WagonSetModel>> ExpandWagonSetModels(FolderModel folderModel, CancellationToken cancellationToken)
            => ExpandWagonSetModels(folderModel, cancellationToken, file => new EngineFile(file));

        internal static async Task<ImmutableArray<WagonSetModel>> ExpandWagonSetModels(FolderModel folderModel, CancellationToken cancellationToken,
            Func<string, EngineFile> readEngine)
        {
            ArgumentNullException.ThrowIfNull(folderModel, nameof(folderModel));
            ArgumentNullException.ThrowIfNull(readEngine, nameof(readEngine));

            ConcurrentBag<WagonSetModel> results = new ConcurrentBag<WagonSetModel>();
            // Many consists reference the same locomotive. Read its display metadata once
            // per scan instead of seeking through the same .eng file for every occurrence.
            // This cache ends with this scan: edits and newly installed engines are read again
            // on the next refresh. Resolved paths keep distinct case-sensitive files distinct.
            ConcurrentDictionary<string, Lazy<EngineFile>> engineFiles = new ConcurrentDictionary<string, Lazy<EngineFile>>(StringComparer.Ordinal);
            EngineFile ReadEngine(string path)
            {
                string resolved = ContentIO.ResolveFile(path);
                return resolved == null ? null : engineFiles.GetOrAdd(Path.GetFullPath(resolved), file =>
                    new Lazy<EngineFile>(() => readEngine(file), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
            }

            string sourceFolder = folderModel.MstsContentFolder().ConsistsFolder;

            if (ContentIO.DirectoryExists(sourceFolder))
            {
                // load existing MSTS files
                ConcurrentBag<string> consistFiles = new ConcurrentBag<string>(ContentIO.EnumerateFiles(sourceFolder, "*.con"));

                await Parallel.ForEachAsync(consistFiles, cancellationToken, async (consistFile, token) =>
                {
                    Task<WagonSetModel> modelTask = Convert(consistFile, folderModel, ReadEngine, token);

                    WagonSetModel wagonSetModel = await ImportFailures.Guard(modelTask, "consist", consistFile).ConfigureAwait(false);
                    if (wagonSetModel == null)
                        return;
                    string key = wagonSetModel.Hierarchy();
                    results.Add(wagonSetModel);
                    modelTaskCache[key] = modelTask;
                }).ConfigureAwait(false);
            }
            ImmutableArray<WagonSetModel> result = results.ToImmutableArray();
            string key = folderModel.Hierarchy();
            modelSetTaskCache[key] = Task.FromResult(result);
            _ = collectionUpdateRequired.TryRemove(key, out _);
            return result;
        }

        private static async Task<WagonSetModel> Convert(string filePath, FolderModel folderModel, Func<string, EngineFile> readEngine,
            CancellationToken cancellationToken)
        {
            ArgumentException.ThrowIfNullOrEmpty(filePath, nameof(filePath));
            ArgumentNullException.ThrowIfNull(folderModel, nameof(folderModel));

            if (ContentIO.FileExists(filePath))
            {
                ConsistFile consistFile = new ConsistFile(filePath);

                List<WagonReferenceModel> trainCars = consistFile.Train.Wagons.OrderBy(w => w.UiD).Select((w, index) => new WagonReferenceModel()
                {
                    TrainCarType = w.IsEOT ? Common.TrainCarType.Eot : w.IsEngine ? Common.TrainCarType.Engine : Common.TrainCarType.Wagon,
                    Uid = index,//w.UiD,
                    Reverse = w.Flip,
                    Name = w.Name,
                    Reference = w.Folder,
                }).ToList();

                WagonReferenceModel locomotive = null;
                for (int i = 0; i < trainCars.Count; i++)
                {
                    if (trainCars[i].TrainCarType == Common.TrainCarType.Engine)
                    {
                        locomotive = trainCars[i];
                        string engineFileName = folderModel.MstsContentFolder().EngineFile(locomotive.Reference, locomotive.Name);

                        EngineFile engFile = readEngine(engineFileName);
                        if (engFile != null)
                        {
                            locomotive = !string.IsNullOrEmpty(engFile.CabViewFile)
                                ? (locomotive with
                                {
                                    Name = engFile.Name?.Trim(),
                                    Description = engFile.Description?.Trim(),
                                })
                                : locomotive;
                            trainCars[i] = locomotive;
                        }
                    }
                }

                WagonSetModel wagonSetModel = new WagonSetModel()
                {
                    Id = consistFile.Train.Id.Trim(),
                    Name = consistFile.Train.Name.Trim(),
                    MaximumSpeed = consistFile.Train.MaxVelocity.A,
                    AccelerationFactor = consistFile.Train.MaxVelocity.B,
                    Durability = consistFile.Train.Durability,
                    Tags = new Dictionary<string, string> { { SourceNameKey, Path.GetFileNameWithoutExtension(filePath) } }.ToImmutableDictionary(),
                    TrainCars = trainCars.ToImmutableArray(),
                };
                //this is the case where a file may have been renamed but not the consist id, ie. in case of copy cloning, so adopting the filename as id
                if (string.IsNullOrEmpty(wagonSetModel.Id) || !string.Equals(wagonSetModel.Tags[SourceNameKey].Trim(), wagonSetModel.Id, StringComparison.OrdinalIgnoreCase))
                {
                    Trace.TraceWarning($"Consist file {filePath} refers to consist Id {wagonSetModel.Id}. Renaming to {wagonSetModel.Tags[SourceNameKey]}");
                    wagonSetModel = wagonSetModel with { Id = wagonSetModel.Tags[SourceNameKey] };
                }
                await Create(wagonSetModel, folderModel, cancellationToken).ConfigureAwait(false);
                return wagonSetModel;
            }
            else
            {
                Trace.TraceWarning($"Consist file {filePath} refers to non-existing file.");
                return null;
            }
        }
    }
}
