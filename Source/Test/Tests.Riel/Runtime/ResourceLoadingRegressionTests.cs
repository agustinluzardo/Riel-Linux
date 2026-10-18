using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D;
using Orts.ActivityRunner.Viewer3D.Shapes;

namespace Tests.Riel.Runtime
{
    [TestClass]
    public class ResourceLoadingRegressionTests
    {
        [TestMethod]
        [DataRow(0L, 0L)]
        [DataRow(-1L, 0L)]
        [DataRow(536870912L, 469762048L)]
        [DataRow(2147483648L, 1879048192L)]
        [DataRow(17179869184L, 16642998272L)]
        public void OptionalTexturePreloadLeavesHeadroomOnSmallAndLargeSystems(long available, long expected)
        {
            Type policy = typeof(Viewer).Assembly.GetType("Orts.ActivityRunner.Viewer3D.TexturePreloadBudget", true);
            long limit = (long)policy.GetMethod("Limit", BindingFlags.Static | BindingFlags.NonPublic)
                .Invoke(null, new object[] { available });
            Assert.AreEqual(expected, limit);
        }

        [TestMethod]
        public void StationaryAiChangesAreCoalescedButEventuallyTriggerCleanup()
        {
            Type type = typeof(Viewer).Assembly.GetType("Orts.ActivityRunner.Viewer3D.ResourceSweepSchedule", true);
            object schedule = Activator.CreateInstance(type, nonPublic: true);
            MethodInfo sweep = type.GetMethod("ShouldSweep", BindingFlags.Instance | BindingFlags.NonPublic);
            object first = new object();
            object second = new object();
            bool Check(bool tile, object resources, int seconds) =>
                (bool)sweep.Invoke(schedule, new[] { (object)tile, resources, TimeSpan.FromSeconds(seconds) });

            Assert.IsTrue(Check(false, first, 0));
            Assert.IsFalse(Check(false, first, 60), "An unchanged scene must not be swept repeatedly.");
            Assert.IsTrue(Check(false, second, 60), "Departed AI resources must be reclaimed while parked.");
            Assert.IsFalse(Check(false, first, 61), "Rapid churn must not cause a sweep every loader pass.");
            Assert.IsTrue(Check(false, first, 90), "Pending changes must survive the cooldown.");
            Assert.IsTrue(Check(true, first, 91), "Tile changes must keep immediate cleanup.");
        }

        [TestMethod]
        public void TextureMarkingRetainsEveryAliasOfALiveTexture()
        {
            Texture2D live = Uninitialized<Texture2D>();
            Texture2D unused = Uninitialized<Texture2D>();
            SharedTextureManager manager = Uninitialized<SharedTextureManager>();
            Dictionary<string, bool> marks = new(StringComparer.OrdinalIgnoreCase);
            Set(manager, "Textures", new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase)
            {
                ["first.ace"] = live, ["alias.dds"] = live, ["unused.ace"] = unused,
            });
            Set(manager, "TextureMarks", marks);
            manager.Mark();
            manager.Mark(live);
            Assert.IsTrue(marks["first.ace"]);
            Assert.IsTrue(marks["alias.dds"]);
            Assert.IsFalse(marks["unused.ace"]);
            manager.Mark();
            Assert.IsFalse(marks["first.ace"], "Each mark pass must reset liveness.");
        }

        [TestMethod]
        public void ShapeMarkingRetainsLiveAliasesAndSweepsOnlyTheUnusedShape()
        {
            SharedShape live = Uninitialized<SharedShape>();
            SharedShape unused = Uninitialized<SharedShape>();
            unused.LodControls = Array.Empty<SharedShape.LodControl>();
            SharedShapeManager manager = Uninitialized<SharedShapeManager>();
            Dictionary<string, SharedShape> shapes = new(StringComparer.OrdinalIgnoreCase)
            {
                ["first.s"] = live, ["alias.s"] = live, ["unused.s"] = unused,
            };
            Set(manager, "sharedShapes", shapes);
            Set(manager, "shapeMarks", new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase));
            manager.Mark();
            manager.Mark(live);
            manager.Sweep();
            Assert.AreEqual(2, shapes.Count);
            Assert.AreSame(live, shapes["first.s"]);
            Assert.AreSame(live, shapes["alias.s"]);
        }

        [TestMethod]
        public void MaterialMarkingRetainsAliasesAndDoesNotLoadWithoutMemoryHeadroom()
        {
            Viewer viewer = Uninitialized<Viewer>();
            viewer.LoadMemoryThreshold = 0;
            SharedMaterialManager manager = Uninitialized<SharedMaterialManager>();
            SceneryMaterial live = Uninitialized<SceneryMaterial>();
            Set(live, "options", SceneryMaterialOptions.NightTexture);
            Set(live, "nightTexture", SharedMaterialManager.MissingTexture);
            Set(live, "dayTexture", SharedMaterialManager.MissingTexture);
            Set(live, "texturePath", "pending.ace");
            var materials = new Dictionary<(string, string, int, float, Effect), Material>
            {
                [("Scenery", "first.ace", 0, 0, null)] = live,
                [("Scenery", "alias.ace", 0, 0, null)] = live,
                [("Scenery", "unused.ace", 0, 0, null)] = Uninitialized<SceneryMaterial>(),
            };
            Set(manager, "Viewer", viewer);
            Set(manager, "Materials", materials);
            Set(manager, "MaterialMarks", new Dictionary<(string, string, int, float, Effect), bool>());
            manager.Mark();
            manager.Mark(live);
            manager.Sweep();
            Assert.AreEqual(2, materials.Count);
            Assert.IsFalse(manager.LoadNightTextures(), "Stop before the first upload when memory is exhausted.");
            Assert.IsFalse(manager.LoadDayTextures());
            Set(live, "options", SceneryMaterialOptions.None);
            Set(live, "dayTexture", Uninitialized<Texture2D>());
            Assert.IsTrue(manager.LoadNightTextures(), "An already-loaded scene must not be reported as pending.");
            Assert.IsTrue(manager.LoadDayTextures());
        }

        private static T Uninitialized<T>() where T : class
        {
            T instance = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            GC.SuppressFinalize(instance);
            return instance;
        }

        private static void Set(object instance, string field, object value) =>
            instance.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(instance, value);
    }
}
