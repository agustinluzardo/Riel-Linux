using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using Riel.Common.Position;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Viewer3D;

namespace Tests.Riel.Runtime
{
    [TestClass]
    public class SceneryQueryRegressionTests
    {
        [TestMethod]
        [DataRow(2112f, 0f, 1, 0, 64f, 0f)]
        [DataRow(-2112f, 0f, -1, 0, -64f, 0f)]
        [DataRow(0f, 2112f, 0, 1, 0f, 64f)]
        [DataRow(2112f, -2112f, 1, -1, 64f, -64f)]
        public void RoofQueriesUseTheNormalizedParticleTileAndCoordinates(float x, float z, int tileX, int tileZ, float normalizedX, float normalizedZ)
        {
            SceneryDrawer scenery = new SceneryDrawer(null);
            scenery.WorldFiles.Add(CreateWorldFile(Tile.Zero, 0, 0, 3));
            scenery.WorldFiles.Add(CreateWorldFile(new Tile(tileX, tileZ), normalizedX, normalizedZ, 12));

            float height = scenery.GetBoundingBoxTop(new WorldLocation(0, 0, x, 0, z), 16);

            Assert.AreEqual(12f, height, "The camera tile's roof must not replace the sampled tile's roof.");
        }

        [TestMethod]
        public void SameTileRoofQueriesKeepTheirExistingHeight()
        {
            SceneryDrawer scenery = new SceneryDrawer(null);
            scenery.WorldFiles.Add(CreateWorldFile(Tile.Zero, 32, 64, 9));

            Assert.AreEqual(9f, scenery.GetBoundingBoxTop(new WorldLocation(0, 0, 32, 0, 64), 16));
        }

        [TestMethod]
        public void MissingParticleTileReturnsNoRoofInsteadOfTheCameraTileRoof()
        {
            SceneryDrawer scenery = new SceneryDrawer(null);
            scenery.WorldFiles.Add(CreateWorldFile(Tile.Zero, 0, 0, 3));

            Assert.AreEqual(float.MinValue, scenery.GetBoundingBoxTop(new WorldLocation(2, 0, 0, 0, 0), 16));
        }

        private static WorldFile CreateWorldFile(Tile tile, float x, float z, float roofHeight)
        {
            WorldFile worldFile = (WorldFile)RuntimeHelpers.GetUninitializedObject(typeof(WorldFile));
            SetField(worldFile, "tile", tile);
            Orts.ActivityRunner.Viewer3D.BoundingBox box = (Orts.ActivityRunner.Viewer3D.BoundingBox)Activator.CreateInstance(
                typeof(Orts.ActivityRunner.Viewer3D.BoundingBox), BindingFlags.Instance | BindingFlags.NonPublic,
                null, new object[] { Matrix.CreateTranslation(-x, 0, z), new Vector3(20, 6, 20), roofHeight - 6 }, null);
            SetField(worldFile, "<BoundingBoxes>k__BackingField", new List<Orts.ActivityRunner.Viewer3D.BoundingBox> { box });
            return worldFile;
        }

        private static void SetField(object target, string name, object value) =>
            typeof(WorldFile).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);
    }
}
