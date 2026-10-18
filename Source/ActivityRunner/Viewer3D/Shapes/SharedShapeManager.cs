using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace Orts.ActivityRunner.Viewer3D.Shapes
{
    public class SharedShapeManager
    {
        private readonly Dictionary<string, SharedShape> sharedShapes = new Dictionary<string, SharedShape>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, bool> shapeMarks = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        private ResourceMarkIndex<string, SharedShape> markIndex;

        internal SharedShapeManager(Viewer viewer)
        {
            SharedShape.Initialize(viewer);
            BaseShape.Initialize(viewer);
        }

        public SharedShape Get(string path)
        {
            if (path == null || path == SharedShape.Empty.FilePath)
                return SharedShape.Empty;

            if (!sharedShapes.ContainsKey(path))
            {
                try
                {
                    sharedShapes.Add(path, new SharedShape(path));
                }
                catch (Exception error)
                {
                    Trace.WriteLine(new FileLoadException(path, error));
                    sharedShapes.Add(path, SharedShape.Empty);
                }
            }
            return sharedShapes[path];
        }

        public void Mark()
        {
            (markIndex ??= new ResourceMarkIndex<string, SharedShape>()).Reset(sharedShapes);
            shapeMarks.Clear();
            foreach (var path in sharedShapes.Keys)
                shapeMarks.Add(path, false);
        }

        public void Mark(SharedShape shape)
        {
            markIndex?.Mark(shape, shapeMarks);
        }

        public void Sweep()
        {
            foreach (var path in shapeMarks.Where(kvp => !kvp.Value).Select(kvp => kvp.Key))
            {
                sharedShapes[path].Dispose();
                sharedShapes.Remove(path);
            }
            markIndex?.Clear();
        }

        public string GetStatus()
        {
            return Viewer.Catalog.GetPluralString("{0:F0} shape", "{0:F0} shapes", sharedShapes.Keys.Count);
        }
    }
}
