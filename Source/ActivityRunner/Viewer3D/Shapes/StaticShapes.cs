using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Riel.Common;
using Riel.Common.Position;
using Riel.Common.Xna;

using Microsoft.Xna.Framework;

namespace Orts.ActivityRunner.Viewer3D.Shapes
{

    /// <summary>
    /// Static shapes have a fixed position in the world, i.e. the position once given does not change
    /// </summary>
    public class StaticShape: BaseShape
    {
        protected readonly WorldPosition worldPosition;

        /// <summary>
        /// Construct and initialize the class
        /// This constructor is for objects described by a MSTS shape file
        /// </summary>
        public StaticShape(string path, in WorldPosition position, ShapeOptions flags):
            base(path, flags)
        {
            worldPosition = position;
        }

        public override ref readonly WorldPosition WorldPosition => ref worldPosition;

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            SharedShape.PrepareFrame(frame, worldPosition, Flags);
        }
    }

    public class StaticTrackShape : StaticShape
    {
        public StaticTrackShape(string path, in WorldPosition position)
            : base(path, in position, ShapeOptions.AutoZBias)
        {
        }
    }

    public class SharedStaticShapeInstance : StaticShape
    {
        private readonly bool nightObjectEnabled;
        private readonly float objectRadius;
        private readonly float instanceSpread;
        private readonly ShapePrimitiveInstances[][][] primitives;

        public SharedStaticShapeInstance(string path, List<BaseShape> shapes)
            : base(path, GetCenterLocation(shapes), GetShapeFlags(shapes[0]))
        {
            nightObjectEnabled = SharedShape.HasNightSubObj;
            instanceSpread = shapes.Max(s => (WorldPosition.Location - s.WorldPosition.Location).Length());
            objectRadius = instanceSpread + SharedShape.LodControls.SelectMany(lod => lod.DistanceLevels)
                .Select(level => level.ViewSphereRadius).DefaultIfEmpty(0).Max();

            // Keep immutable instance buffers for every authored LOD. Frame preparation
            // only selects buffers, so it cannot mutate a frame still being rendered.
            primitives = new ShapePrimitiveInstances[SharedShape.LodControls.Length][][];
            for (int control = 0; control < SharedShape.LodControls.Length; control++)
            {
                var levels = SharedShape.LodControls[control].DistanceLevels;
                primitives[control] = new ShapePrimitiveInstances[levels.Length][];
                for (int level = 0; level < levels.Length; level++)
                {
                    List<ShapePrimitiveInstances> list = new List<ShapePrimitiveInstances>();
                    for (int subObject = 0; subObject < levels[level].SubObjects.Length; subObject++)
                        foreach (var primitive in levels[level].SubObjects[subObject].ShapePrimitives)
                            list.Add(new ShapePrimitiveInstances(viewer.Game.GraphicsDevice, primitive,
                                GetMatricies(shapes, primitive), subObject));
                    primitives[control][level] = list.ToArray();
                }
            }
        }

        private int SelectDetailLevel(SharedShape.DistanceLevel[] levels, Vector3 location)
        {
            int level = levels.Length - 1;
            if (viewer.UserSettings.DetailLevelBias == 100)
                return 0;
            float bias = (float)viewer.UserSettings.DetailLevelBias / 100 + 1;
            if (viewer.UserSettings.DetailLevelBias > -100)
                // A whole group may only drop detail when all its instances are far
                // enough away. The envelope preserves detail on the nearest copy.
                while (level > 0 && viewer.Camera.InRange(location,
                    instanceSpread + levels[level - 1].ViewSphereRadius, levels[level - 1].ViewingDistance * bias))
                    level--;
            return level;
        }

        private static WorldPosition GetCenterLocation(List<BaseShape> shapes)
        {
            int tileX = shapes.Min(s => s.WorldPosition.Tile.X);
            int tileZ = shapes.Min(s => s.WorldPosition.Tile.Z);
            Debug.Assert(tileX == shapes.Max(s => s.WorldPosition.Tile.X));
            Debug.Assert(tileZ == shapes.Max(s => s.WorldPosition.Tile.Z));

            float minX = shapes.Min(s => s.WorldPosition.Location.X);
            float maxX = shapes.Max(s => s.WorldPosition.Location.X);
            float minY = shapes.Min(s => s.WorldPosition.Location.Y);
            float maxY = shapes.Max(s => s.WorldPosition.Location.Y);
            float minZ = shapes.Min(s => s.WorldPosition.Location.Z);
            float maxZ = shapes.Max(s => s.WorldPosition.Location.Z);
            return new WorldPosition(new Tile(tileX, tileZ), Matrix.Identity).SetTranslation((minX + maxX) / 2, (minY + maxY) / 2, - (minZ + maxZ) / 2);
        }

        private Matrix[] GetMatricies(List<BaseShape> shapes, ShapePrimitive shapePrimitive)
        {
            Matrix matrix = Matrix.Identity;
            int hi = shapePrimitive.HierarchyIndex;
            // Include the terminal root, just as SharedShape.PrepareFrame does.
            while (hi >= 0 && hi < shapePrimitive.Hierarchy.Length)
            {
                matrix = MatrixExtension.Multiply(matrix, SharedShape.Matrices[hi]);
                hi = shapePrimitive.Hierarchy[hi];
            }

            var matricies = new Matrix[shapes.Count];
            for (var i = 0; i < shapes.Count; i++)
                matricies[i] = MatrixExtension.Multiply(
                    MatrixExtension.Multiply(matrix, shapes[i].WorldPosition.XNAMatrix), 
                    Matrix.CreateTranslation(-WorldPosition.Location.X, -WorldPosition.Location.Y, WorldPosition.Location.Z));

            return matricies;
        }

        public override void PrepareFrame(RenderFrame frame, in ElapsedTime elapsedTime)
        {
            Vector3 mstsLocation = worldPosition.Location + (worldPosition.Tile - viewer.Camera.Tile).TileVector();
            Matrix xnaMatrix = Matrix.CreateTranslation(mstsLocation.XnaVector());

            float bias = (float)viewer.UserSettings.DetailLevelBias / 100 + 1;
            for (int control = 0; control < primitives.Length; control++)
            {
                var levels = SharedShape.LodControls[control].DistanceLevels;
                int level = SelectDetailLevel(levels, mstsLocation);
                var distanceLevel = levels[viewer.UserSettings.DetailLevelBias == 100 ? levels.Length - 1 : level];
                float viewingDistance = viewer.UserSettings.ExtendedDetailLevelView && level == levels.Length - 1
                    ? float.MaxValue : distanceLevel.ViewingDistance;
                foreach (var primitive in primitives[control][level])
                    if (primitive.SubObjectIndex != 1 || !nightObjectEnabled || viewer.MaterialManager.sunDirection.Y < 0)
                        frame.AddAutoPrimitive(mstsLocation, objectRadius, viewingDistance * bias,
                            primitive.Material, primitive, RenderPrimitiveGroup.World, ref xnaMatrix, Flags);
            }
        }
    }



}
