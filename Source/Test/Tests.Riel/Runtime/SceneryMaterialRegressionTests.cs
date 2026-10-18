using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

using Orts.ActivityRunner.Viewer3D;
using Orts.ActivityRunner.Viewer3D.Common;

namespace Tests.Riel.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class SceneryMaterialRegressionTests
    {
        // Exercise the same private factory used by SceneryMaterial's constructor.
        // SamplerState allocation does not require a GraphicsDevice.
        private static readonly Func<float, SceneryMaterialOptions, SamplerState> GetSampler =
            typeof(SceneryMaterial).GetMethod("GetShadowTextureAddressMode", BindingFlags.Static | BindingFlags.NonPublic)
                .CreateDelegate<Func<float, SceneryMaterialOptions, SamplerState>>();

        [TestMethod]
        [DataRow(SceneryMaterialOptions.TextureAddressModeWrap, TextureAddressMode.Wrap, false)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeMirror, TextureAddressMode.Mirror, false)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeClamp, TextureAddressMode.Clamp, false)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeBorder, TextureAddressMode.Border, false)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeWrap, TextureAddressMode.Wrap, true)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeMirror, TextureAddressMode.Mirror, true)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeClamp, TextureAddressMode.Clamp, true)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeBorder, TextureAddressMode.Border, true)]
        public void AuthoredAddressModesKeepTheirMeaningAndSamplerQuality(SceneryMaterialOptions mode,
            TextureAddressMode expectedMode, bool otherFlags)
        {
            SceneryMaterialOptions options = otherFlags
                ? mode | SceneryMaterialOptions.Diffuse | SceneryMaterialOptions.AlphaTest
                    | SceneryMaterialOptions.NightTexture | SceneryMaterialOptions.UndergroundTexture
                : mode;

            SamplerState sampler = GetSampler(0.25f, options);

            Assert.AreEqual(expectedMode, sampler.AddressU);
            Assert.AreEqual(expectedMode, sampler.AddressV);
            Assert.AreEqual(TextureFilter.Anisotropic, sampler.Filter);
            Assert.AreEqual(16, sampler.MaxAnisotropy);
            Assert.AreEqual(0.25f, sampler.MipMapLevelOfDetailBias);
            Assert.AreSame(sampler, GetSampler(0.25f, mode),
                "Unrelated shader flags must not create another sampler for the same UV mode and bias.");
        }

        [TestMethod]
        [DataRow(SceneryMaterialOptions.TextureAddressModeWrap)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeMirror)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeClamp)]
        [DataRow(SceneryMaterialOptions.TextureAddressModeBorder)]
        public void ExistingMinimumMipBiasAndCacheReuseArePreserved(SceneryMaterialOptions mode)
        {
            SamplerState minimum = GetSampler(-1, mode);

            Assert.AreEqual(-1f, minimum.MipMapLevelOfDetailBias);
            Assert.AreSame(minimum, GetSampler(-4, mode));
            Assert.AreSame(minimum, GetSampler(-1, mode));
        }

        [TestMethod]
        public void DifferentAddressModesAndMipBiasesKeepSeparateSamplers()
        {
            SamplerState wrap = GetSampler(0, SceneryMaterialOptions.TextureAddressModeWrap);
            SamplerState mirror = GetSampler(0, SceneryMaterialOptions.TextureAddressModeMirror);
            SamplerState clamp = GetSampler(0, SceneryMaterialOptions.TextureAddressModeClamp);
            SamplerState border = GetSampler(0, SceneryMaterialOptions.TextureAddressModeBorder);
            Assert.AreNotSame(wrap, mirror);
            Assert.AreNotSame(wrap, clamp);
            Assert.AreNotSame(wrap, border);
            Assert.AreNotSame(mirror, clamp);
            Assert.AreNotSame(mirror, border);
            Assert.AreNotSame(clamp, border);

            SamplerState biasedWrap = GetSampler(0.75f, SceneryMaterialOptions.TextureAddressModeWrap);
            Assert.AreNotSame(wrap, biasedWrap);
            Assert.AreEqual(0f, wrap.MipMapLevelOfDetailBias);
            Assert.AreEqual(0.75f, biasedWrap.MipMapLevelOfDetailBias);
            Assert.AreSame(biasedWrap, GetSampler(0.75f, SceneryMaterialOptions.TextureAddressModeWrap));
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DeferredNightLoadChangesProductionSelectionOnlyAfterDark(bool parentNightDirectory)
        {
            using NightTextureFixture fixture = new NightTextureFixture(true, parentNightDirectory);

            fixture.SetSun(Vector3.Up);
            Assert.AreSame(fixture.DayTexture, fixture.Material.GetShadowTexture());
            Assert.IsTrue(fixture.Material.LoadNightTexture(), "The authored night texture should load during dusk preparation.");
            Assert.AreSame(fixture.DayTexture, fixture.Material.GetShadowTexture(),
                "Loading a night texture must not change the daylight selection policy.");

            fixture.SetSun(Vector3.Down);
            Assert.AreSame(fixture.NightTexture, fixture.Material.GetShadowTexture(),
                "A texture successfully loaded after material creation must become eligible at night.");
            Assert.IsFalse(fixture.Material.LoadNightTexture(), "An already loaded night texture must not be loaded again.");

            fixture.SetSun(Vector3.Up);
            Assert.AreSame(fixture.DayTexture, fixture.Material.GetShadowTexture());
        }

        [TestMethod]
        public void MissingNightTextureRetainsTheDayTextureFallback()
        {
            using NightTextureFixture fixture = new NightTextureFixture(false);
            fixture.SetSun(Vector3.Down);

            Assert.IsFalse(fixture.Material.LoadNightTexture());
            Assert.AreSame(fixture.DayTexture, fixture.Material.GetShadowTexture());
        }

        [TestMethod]
        public void DayOnlyMaterialDoesNotStartUsingAnAvailableNightTexture()
        {
            using NightTextureFixture fixture = new NightTextureFixture(true, options: SceneryMaterialOptions.None);
            fixture.SetSun(Vector3.Down);

            Assert.IsFalse(fixture.Material.LoadNightTexture());
            Assert.AreSame(fixture.DayTexture, fixture.Material.GetShadowTexture());
        }

        private sealed class NightTextureFixture : IDisposable
        {
            private readonly string directory;
            private readonly SharedMaterialManager manager;

            internal SceneryMaterial Material { get; }
            internal Texture2D DayTexture { get; } = Uninitialized<Texture2D>();
            internal Texture2D NightTexture { get; } = Uninitialized<Texture2D>();

            internal NightTextureFixture(bool createNightTexture, bool parentNightDirectory = false,
                SceneryMaterialOptions options = SceneryMaterialOptions.NightTexture)
            {
                directory = Path.Combine(Path.GetTempPath(), "riel-material-tests-" + Guid.NewGuid().ToString("N"));
                string texturesDirectory = Path.Combine(directory, "Textures");
                string dayDirectory = parentNightDirectory ? Path.Combine(texturesDirectory, "Snow") : texturesDirectory;
                Directory.CreateDirectory(dayDirectory);
                string dayPath = Path.Combine(dayDirectory, "building.ace");

                if (createNightTexture)
                {
                    string nightDirectory = Path.Combine(texturesDirectory, "Night");
                    Directory.CreateDirectory(nightDirectory);
                    File.WriteAllBytes(Path.Combine(nightDirectory, "building.ace"), Array.Empty<byte>());
                }

                Viewer viewer = Uninitialized<Viewer>();
                viewer.Camera = Uninitialized<FreeRoamCamera>();
                manager = Uninitialized<SharedMaterialManager>();
                SetField(typeof(Viewer), viewer, "<MaterialManager>k__BackingField", manager);
                SharedTextureManager textures = new SharedTextureManager(viewer, null);
                SetField(typeof(Viewer), viewer, "<TextureManager>k__BackingField", textures);

                if (createNightTexture)
                {
                    // Use the real authored-content discovery and texture-cache key.
                    // The cached texture sentinels bypass GPU upload, while the actual
                    // LoadNightTexture and SharedTextureManager.Get paths still execute.
                    string resolvedNightPath = Helpers.GetNightTextureFile(dayPath);
                    Assert.IsNotNull(resolvedNightPath);
                    SetField(typeof(SharedTextureManager), textures, "Textures",
                        new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase)
                        {
                            [Path.GetFullPath(resolvedNightPath)] = NightTexture,
                        });
                }

                // Bypass only graphics/shader initialization. Establish the same
                // deferred state produced by daytime material construction, then
                // exercise production loading and selection methods unchanged.
                Material = Uninitialized<SceneryMaterial>();
                SetField(typeof(Material), Material, "viewer", viewer);
                SetField(typeof(SceneryMaterial), Material, "options", options);
                SetField(typeof(SceneryMaterial), Material, "texturePath", dayPath);
                SetField(typeof(SceneryMaterial), Material, "dayTexture", DayTexture);
                SetField(typeof(SceneryMaterial), Material, "nightTexture", SharedMaterialManager.MissingTexture);
            }

            internal void SetSun(Vector3 direction) =>
                SetField(typeof(SharedMaterialManager), manager, "sunDirection", direction);

            public void Dispose() => Directory.Delete(directory, recursive: true);
        }

        private static T Uninitialized<T>() where T : class
        {
            T instance = (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
            GC.SuppressFinalize(instance);
            return instance;
        }

        private static void SetField(Type declaringType, object target, string name, object value) =>
            declaringType.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);
    }
}
