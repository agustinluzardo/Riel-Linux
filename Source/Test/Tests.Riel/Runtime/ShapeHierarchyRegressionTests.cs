using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using Riel.Common;
using Riel.Common.Position;
using Riel.Common.Xna;
using Riel.Models.Settings;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

using Orts.ActivityRunner.Processes;
using Orts.ActivityRunner.Viewer3D;
using Orts.ActivityRunner.Viewer3D.Shapes;
using Orts.Formats.Msts.Files;

namespace Tests.Riel.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class ShapeHierarchyRegressionTests
    {
        private Viewer previousShapeViewer;
        private Viewer previousBaseViewer;
        private Matrix previousSkyProjection;
        private Viewer viewer;
        private RenderFrame frame;

        [TestInitialize]
        public void Initialize()
        {
            previousShapeViewer = (Viewer)typeof(SharedShape).GetField("viewer", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            previousBaseViewer = (Viewer)typeof(BaseShape).GetField("viewer", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            previousSkyProjection = Camera.XNASkyProjection;
            ProfileUserSettingsModel settings = new ProfileUserSettingsModel
            {
                DynamicShadows = false,
                ViewingDistance = 10000,
                FarMountainsViewingDistance = 0,
                FieldOfView = 60,
                DetailLevelBias = 0,
                ExtendedDetailLevelView = false,
                ShapeWarnings = false,
            };
            GameHost game = Uninitialized<GameHost>();
            RenderProcess process = Uninitialized<RenderProcess>();
            SetField(typeof(RenderProcess), process, "<DisplaySize>k__BackingField", new Point(800, 480));
            SetField(typeof(GameHost), game, "<RenderProcess>k__BackingField", process);
            SetField(typeof(GameHost), game, "<UserSettings>k__BackingField", settings);
            frame = new RenderFrame(game);
            viewer = Uninitialized<Viewer>();
            SetField(typeof(Viewer), viewer, "<UserSettings>k__BackingField", settings);
            SetField(typeof(Viewer), viewer, "<RenderProcess>k__BackingField", process);
            Camera camera = Uninitialized<FreeRoamCamera>();
            SetField(typeof(Camera), camera, "viewer", viewer);
            SetField(typeof(Camera), camera, "cameraLocation", new WorldLocation(Tile.Zero, Vector3.Zero));
            SetField(typeof(Camera), camera, "NearPlane", 1f);
            camera.FieldOfView = settings.FieldOfView;
            viewer.Camera = camera;
            camera.ScreenChanged();
            camera.PrepareFrame(frame, ElapsedTime.Zero);
            SharedShape.Initialize(viewer);
        }

        [TestCleanup]
        public void Cleanup()
        {
            SharedShape.Initialize(previousShapeViewer);
            BaseShape.Initialize(previousBaseViewer);
            Camera.XNASkyProjection = previousSkyProjection;
        }

        [TestMethod]
        [DataRow(false, false, -1)]
        [DataRow(false, true, -1)]
        [DataRow(true, false, -1)]
        [DataRow(true, true, -1)]
        [DataRow(true, true, 2)]
        [DataRow(true, true, -2)]
        public void InstancedTransformsMatchOrdinarySubmissionIncludingRootAndTruncatedParents(bool child, bool crossTile, int rootParent)
        {
            CaptureMaterial material = new CaptureMaterial();
            // Root translation has already been cleared by the existing MSTS loader
            // compatibility rule; root rotation and scale must still be applied.
            Matrix root = Matrix.CreateScale(1.5f, 2, 0.75f) * Matrix.CreateRotationY(0.6f);
            Matrix childMatrix = Matrix.CreateRotationZ(0.2f) * Matrix.CreateTranslation(3, 4, 5);
            SharedShape shape = Uninitialized<SharedShape>();
            shape.Matrices = new[] { root, childMatrix };
            int[] hierarchy = new[] { rootParent, 0 };
            HeadlessPrimitive primitive = new HeadlessPrimitive(material, hierarchy, child ? 1 : 0);
            SharedShape.SubObject subObject = Uninitialized<SharedShape.SubObject>();
            subObject.ShapePrimitives = new ShapePrimitive[] { primitive };
            SharedShape.DistanceLevel level = Uninitialized<SharedShape.DistanceLevel>();
            level.ViewingDistance = 10000;
            level.ViewSphereRadius = 100;
            level.SubObjects = new[] { subObject };
            SharedShape.LodControl lod = Uninitialized<SharedShape.LodControl>();
            lod.DistanceLevels = new[] { level };
            shape.LodControls = new[] { lod };

            Tile tile = crossTile ? new Tile(0, 1) : Tile.Zero;
            List<BaseShape> copies = new List<BaseShape>
            {
                CreateStaticShape(shape, new WorldPosition(tile, Matrix.CreateRotationY(0.2f) * Matrix.CreateTranslation(-20, 0, -50))),
                CreateStaticShape(shape, new WorldPosition(tile, Matrix.CreateRotationZ(-0.1f) * Matrix.CreateTranslation(20, 5, -70))),
            };
            foreach (BaseShape copy in copies)
                shape.PrepareFrame(frame, copy.WorldPosition, ShapeOptions.None);
            typeof(RenderFrame).GetMethod("DrawSequences", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(frame, new object[] { false });
            Assert.AreEqual(copies.Count, material.Matrices.Count, "Both ordinary shapes must be submitted by the real preparation pass.");

            WorldPosition center = (WorldPosition)typeof(SharedStaticShapeInstance)
                .GetMethod("GetCenterLocation", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, new object[] { copies });
            SharedStaticShapeInstance group = Uninitialized<SharedStaticShapeInstance>();
            group.SharedShape = shape;
            SetField(typeof(StaticShape), group, "worldPosition", center);
            Matrix[] instanceMatrices = (Matrix[])typeof(SharedStaticShapeInstance)
                .GetMethod("GetMatricies", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(group, new object[] { copies, primitive });
            Vector3 relativeCenter = center.Location + (center.Tile - viewer.Camera.Tile).TileVector();
            Matrix groupWorld = Matrix.CreateTranslation(relativeCenter.XnaVector());
            Vector3[] probes = new[] { Vector3.Zero, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ };
            for (int index = 0; index < copies.Count; index++)
                foreach (Vector3 probe in probes)
                    Assert.IsTrue(Vector3.Distance(Vector3.Transform(probe, material.Matrices[index]),
                        Vector3.Transform(probe, instanceMatrices[index] * groupWorld)) < 0.002f,
                        "Instancing must preserve the ordinary origin, orientation and scale across camera tiles.");
        }

        [TestMethod]
        [DataRow("0", 0)]
        [DataRow("1 0", 0)]
        [DataRow("-1 2 1", 1)]
        public void CyclicShapeLoadFailsBeforeGpuWorkAndUsesExistingEmptyFallback(string hierarchy, int matrixIndex)
        {
            using ShapeFixture fixture = new ShapeFixture(hierarchy, matrixIndex);
            InvalidDataException failure = null;
            try
            {
                _ = new SharedShape(fixture.Path);
            }
            catch (InvalidDataException error)
            {
                failure = error;
            }
            Assert.IsNotNull(failure, "A referenced cycle must fail during CPU validation, before viewer.Game or GPU buffers are needed.");
            StringAssert.Contains(failure.Message, "cycle");
            StringAssert.Contains(failure.Message, fixture.Path);

            SharedShapeManager manager = new SharedShapeManager(viewer);
            Assert.AreSame(SharedShape.Empty, manager.Get(fixture.Path));
            Assert.AreSame(SharedShape.Empty, manager.Get(fixture.Path), "The existing failure cache must remain usable after malformed content.");
        }

        [TestMethod]
        [DataRow("-1 0", 1, false)]
        [DataRow("2 0", 1, true)]
        [DataRow("-2 0", 1, true)]
        [DataRow("-1 1", 0, false)]
        [DataRow("-1 0", -1, false)]
        [DataRow("-1 0", 2, true)]
        public void CpuPreflightPreservesTerminatingAndLegacyTruncatedHierarchies(string hierarchy, int matrixIndex, bool warning)
        {
            MethodInfo validate = typeof(SharedShape).GetMethod("ValidatePrimitiveHierarchies", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.IsNotNull(validate, "The production loader must validate before allocating GPU resources.");
            using ShapeFixture fixture = new ShapeFixture(hierarchy, matrixIndex);
            ShapeFile parsed = new ShapeFile(fixture.Path, false);
            using StringWriter messages = new StringWriter();
            using TextWriterTraceListener listener = new TextWriterTraceListener(messages);
            Trace.Listeners.Add(listener);
            try
            {
                validate.Invoke(null, new object[] { parsed.Shape, fixture.Path });
                listener.Flush();
                Assert.AreEqual(warning, messages.ToString().Contains("Ignored invalid primitive hierarchy", StringComparison.Ordinal));
            }
            finally
            {
                Trace.Listeners.Remove(listener);
            }
        }

        private static StaticShape CreateStaticShape(SharedShape shape, WorldPosition position)
        {
            StaticShape copy = Uninitialized<StaticShape>();
            copy.SharedShape = shape;
            SetField(typeof(StaticShape), copy, "worldPosition", position);
            return copy;
        }

        private static T Uninitialized<T>() where T : class
        {
            T value = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            GC.SuppressFinalize(value);
            return value;
        }

        private static void SetField(Type type, object target, string name, object value) =>
            type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(target, value);

        private sealed class HeadlessPrimitive : ShapePrimitive
        {
            internal HeadlessPrimitive(Material material, int[] hierarchy, int matrixIndex)
            {
                Material = material;
                Hierarchy = hierarchy;
                HierarchyIndex = matrixIndex;
                PrimitiveCount = 1;
            }

            public override void Draw() { }
        }

        private sealed class CaptureMaterial : Material
        {
            internal List<Matrix> Matrices { get; } = new List<Matrix>();
            internal CaptureMaterial() : base(null, null) { }
            public override bool GetBlending() => false;
            public override void Render(List<RenderItem> items, ref Matrix view, ref Matrix projection, ref Matrix viewProjection)
            {
                foreach (RenderItem item in items)
                    Matrices.Add(item.XNAMatrix);
            }
        }

        private sealed class ShapeFixture : IDisposable
        {
            private readonly string directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Riel", "ShapeHierarchyTests", Guid.NewGuid().ToString("N"));
            internal string Path { get; }

            internal ShapeFixture(string hierarchy, int matrixIndex)
            {
                Directory.CreateDirectory(directory);
                Path = System.IO.Path.Combine(directory, "hierarchy.s");
                int count = hierarchy.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                File.WriteAllText(Path, $$"""
                    SIMISA@@@@@@@@@@JINX0s1t______
                    shape (
                      shape_header ( 00000000 00000000 )
                      volumes ( 0 ) shader_names ( 0 ) texture_filter_names ( 0 )
                      points ( 0 ) uv_points ( 0 ) normals ( 0 ) sort_vectors ( 0 ) colours ( 0 )
                      matrices ( 3
                        matrix ROOT ( 1 0 0 0 1 0 0 0 1 0 0 0 )
                        matrix CHILD ( 1 0 0 0 1 0 0 0 1 0 0 0 )
                        matrix LAST ( 1 0 0 0 1 0 0 0 1 0 0 0 ) )
                      images ( 0 ) textures ( 0 ) light_materials ( 0 ) light_model_cfgs ( 0 )
                      vtx_states ( 1 vtx_state ( 00000000 {{matrixIndex}} -5 0 00000002 ) )
                      prim_states ( 1 prim_state ( 00000000 0 tex_idxs ( 0 ) 0 0 0 0 1 ) )
                      lod_controls ( 1 lod_control ( distance_levels_header ( 0 ) distance_levels ( 1
                        distance_level ( distance_level_header ( dlevel_selection ( 10000 ) hierarchy ( {{count}} {{hierarchy}} ) )
                          sub_objects ( 1 sub_object (
                            sub_object_header ( 00000000 -1 -1 00000000 00000000
                              geometry_info ( 0 0 0 0 0 0 0 0 0 0 geometry_nodes ( 0 ) geometry_node_map ( 0 ) ) )
                            vertices ( 0 ) vertex_sets ( 0 )
                            primitives ( 2 prim_state_idx ( 0 ) indexed_trilist (
                              vertex_idxs ( 0 ) normal_idxs ( 0 ) flags ( 0 ) ) ) ) ) ) ) ) )
                    )
                    """);
            }

            public void Dispose() => Directory.Delete(directory, true);
        }
    }
}
