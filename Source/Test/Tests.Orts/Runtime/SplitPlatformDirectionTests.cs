using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using Riel.Common;
using Riel.Common.Position;
using Riel.Models.Track;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

using Orts.Formats.Msts.Models;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Tests.Orts.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class SplitPlatformDirectionTests
    {
        [TestMethod]
        [DataRow(true, true, 1, false)]
        [DataRow(false, true, 1, false)]
        [DataRow(true, false, 1, false)]
        [DataRow(false, false, 1, false)]
        [DataRow(true, true, -1, false)]
        [DataRow(false, false, -1, false)]
        [DataRow(true, true, 1, true)]
        [DataRow(false, false, 1, true)]
        public void SplitPlatformUsesSameDominantAxisForBothEndpoints(bool useX, bool firstPartLonger, int sign, bool acrossTiles)
        {
            using SimulationBoundaryFixture fixture = new SimulationBoundaryFixture();
            float origin = acrossTiles ? 1000 : 0;
            float deltaX = sign * (useX ? 100 : 1);
            float deltaZ = sign * (useX ? 1 : 100);
            float nodeX = sign * (useX ? 20 : -10);
            float nodeZ = sign * (useX ? -10 : 20);
            WorldLocation firstLocation = Location(origin, origin);
            WorldLocation secondLocation = Location(origin + deltaX, origin + deltaZ);
            VectorNode firstNode = Node(1, Location(origin + nodeX, origin + nodeZ));
            VectorNode secondNode = Node(2, Location(origin + deltaX + nodeX, origin + deltaZ + nodeZ));
            PlatformTrackItem firstItem = new PlatformTrackItem(firstLocation) { TrackItemIndex = 0, SectionDistance = 10 };
            TrackDatabase database = new TrackDatabase
            {
                TrackItems = ImmutableArray.Create<Riel.Models.Track.TrackItemBase>(firstItem),
                TrackNodes = ImmutableArray.Create<TrackNodeBase>(null, firstNode, secondNode),
            };
            TrackCircuitSection first = SimulationBoundaryFixture.Section(1, firstPartLonger ? 40 : 20);
            TrackCircuitSection firstOther = SimulationBoundaryFixture.Section(2, firstPartLonger ? 60 : 30);
            TrackCircuitSection second = SimulationBoundaryFixture.Section(3, firstPartLonger ? 20 : 40);
            TrackCircuitSection secondOther = SimulationBoundaryFixture.Section(4, firstPartLonger ? 30 : 60);
            SimulationBoundaryFixture.SetProperty(first, nameof(TrackCircuitSection.OriginalIndex), 1);
            Dictionary<int, TrackCircuitCrossReferences> references = new Dictionary<int, TrackCircuitCrossReferences>
            {
                [1] = References(first, firstOther),
                [2] = References(second, secondOther),
            };
            SignalEnvironment environment = SimulationBoundaryFixture.Uninitialized<SignalEnvironment>();
            SimulationBoundaryFixture.SetField(environment, "trackDatabase", database);
            SimulationBoundaryFixture.SetField(environment, "<NodeCrossReferences>k__BackingField", references);
            PlatformDetails platform = new PlatformDetails(0);
            platform.TCSectionIndex.Add(first.Index);
            platform.TrackCircuitOffset[SignalLocation.FarEnd, TrackDirection.Ahead] = 10;
            platform.TrackCircuitOffset[SignalLocation.NearEnd, TrackDirection.Reverse] = 10;

            SimulationBoundaryFixture.Invoke(environment, "ResolveSplitPlatform", platform,
                firstPartLonger ? second.Index : secondOther.Index, 10f, secondLocation, 2);

            CollectionAssert.AreEqual(firstPartLonger ? new[] { 2, 1 } : new[] { 3, 4 }, platform.TCSectionIndex);
            Assert.AreEqual(90f, platform.Length);
            Assert.AreEqual(firstPartLonger ? 0f : 10f, platform.NodeOffset[SignalLocation.NearEnd]);
            Assert.AreEqual(firstPartLonger ? 10f : 100f, platform.NodeOffset[SignalLocation.FarEnd]);
        }

        private static TrackCircuitCrossReferences References(params TrackCircuitSection[] sections)
        {
            TrackCircuitCrossReferences references = new TrackCircuitCrossReferences();
            foreach (TrackCircuitSection section in sections)
                references.Add(new TrackCircuitSectionCrossReference(section.Index, section.Length, 0, 0));
            return references;
        }

        private static VectorNode Node(int index, WorldLocation start)
        {
            WorldLocation end = new WorldLocation(start.TileX, start.TileZ, start.Location.X, 0, start.Location.Z + 100);
            VectorSectionNode section = new VectorSectionNode(start, start.Tile, Vector3.Zero, end) { NodeIndex = index };
            return new VectorNode(start, start.Tile, end)
            {
                NodeIndex = index,
                VectorSections = ImmutableArray.Create(section),
            };
        }

        private static WorldLocation Location(float x, float z)
        {
            int tileX = (int)Math.Floor((x + 1024) / 2048);
            int tileZ = (int)Math.Floor((z + 1024) / 2048);
            return new WorldLocation(tileX + 1, tileZ, x - tileX * 2048, 0, z - tileZ * 2048);
        }
    }
}
