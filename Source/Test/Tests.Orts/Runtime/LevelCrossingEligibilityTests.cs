using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using Riel.Common;
using Riel.Common.Position;
using Riel.Models.Track;
using Riel.Runtime.Track;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;

using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.World;

namespace Tests.Orts.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class LevelCrossingEligibilityTests
    {
        [TestMethod]
        public void StaticConsistAtOneCrossingDoesNotQualifyTheFollowingCrossing()
        {
            using SimulationBoundaryFixture fixture = new SimulationBoundaryFixture();
            VectorNode node = InitializeTrack();
            LevelCrossings crossings = Crossings(node, 50, 75);
            // FrozenDictionary enumeration order is deliberately unspecified. Put the
            // car at whichever crossing production will visit first.
            LevelCrossingItem[] order = crossings.TrackCrossingItems.Values.ToArray();
            Train train = Train(node, TrainType.Static);
            TrainCar car = SimulationBoundaryFixture.Uninitialized<MSTSWagon>();
            SimulationBoundaryFixture.SetField(car, "worldPosition", new WorldPosition(order[0].Location));
            train.Cars.Add(car);

            SimulationBoundaryFixture.Invoke(crossings, "UpdateCrossings", train, 0d);

            Assert.IsTrue(order[0].CrossingGroup.HasTrain);
            Assert.IsFalse(order[1].CrossingGroup.HasTrain, "Eligibility at the preceding crossing must not close another crossing without a nearby car.");
        }

        [TestMethod]
        public void RegisteredStaticConsistIsRemovedAfterItLeavesTheCrossing()
        {
            using SimulationBoundaryFixture fixture = new SimulationBoundaryFixture();
            VectorNode node = InitializeTrack();
            LevelCrossings crossings = Crossings(node, 50, 75);
            LevelCrossingItem[] order = crossings.TrackCrossingItems.Values.ToArray();
            Train train = Train(node, TrainType.Static);
            TrainCar car = SimulationBoundaryFixture.Uninitialized<MSTSWagon>();
            SimulationBoundaryFixture.SetField(car, "worldPosition", new WorldPosition(order[0].Location));
            train.Cars.Add(car);
            order[1].AddTrain(train);

            SimulationBoundaryFixture.Invoke(crossings, "UpdateCrossings", train, 0d);

            Assert.IsTrue(order[0].CrossingGroup.HasTrain);
            Assert.IsFalse(order[1].CrossingGroup.HasTrain, "Previously registered crossings must still be evaluated and released.");
        }

        [TestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void MovingTrainKeepsNearCrossingActiveAndReleasesDistantCrossing(bool reverseInsertion)
        {
            using SimulationBoundaryFixture fixture = new SimulationBoundaryFixture();
            VectorNode node = InitializeTrack();
            LevelCrossings crossings = reverseInsertion ? Crossings(node, 350, 150) : Crossings(node, 150, 350);
            LevelCrossingItem near = crossings.TrackCrossingItems.Values.Single(item => item.Location.Location.Z == 150);
            LevelCrossingItem far = crossings.TrackCrossingItems.Values.Single(item => item.Location.Location.Z == 350);
            Train train = Train(node, TrainType.Player);
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.SpeedMpS), 10f);
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.AllowedMaxSpeedMpS), 10f);
            far.AddTrain(train);

            SimulationBoundaryFixture.Invoke(crossings, "UpdateCrossings", train, 0d);

            Assert.IsTrue(near.CrossingGroup.HasTrain);
            Assert.IsFalse(far.CrossingGroup.HasTrain);
        }

        private static Train Train(VectorNode node, TrainType type)
        {
            Train train = SimulationBoundaryFixture.Train();
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.TrainType), type);
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.Length), 100f);
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.FrontTrackTraveller),
                TrackTraveller.InitializeTraveller(new WorldLocation(1, 0, 0, 0, 100), node).Value);
            SimulationBoundaryFixture.SetProperty(train, nameof(Train.RearTrackTraveller),
                TrackTraveller.InitializeTraveller(new WorldLocation(1, 0, 0, 0, 0), node).Value.Reverse());
            return train;
        }

        private static LevelCrossings Crossings(VectorNode node, params float[] positions)
        {
            Dictionary<int, LevelCrossingItem> items = new Dictionary<int, LevelCrossingItem>();
            for (int i = 0; i < positions.Length; i++)
            {
                LevelCrossingItem item = new LevelCrossingItem(node,
                    new LevelCrossingTrackItem(new WorldLocation(1, 0, 0, 0, positions[i])) { TrackItemIndex = i });
                SimulationBoundaryFixture.Invoke(item, "SnapToTrack", TrackDataBaseType.Rail);
                _ = new LevelCrossing(new[] { item }, 10, 10);
                items.Add(i, item);
            }
            LevelCrossings crossings = SimulationBoundaryFixture.Uninitialized<LevelCrossings>();
            SimulationBoundaryFixture.SetField(crossings, "<TrackCrossingItems>k__BackingField", items.ToFrozenDictionary());
            return crossings;
        }

        private static VectorNode InitializeTrack()
        {
            WorldLocation start = new WorldLocation(1, 0, 0, 0, 0);
            WorldLocation end = new WorldLocation(1, 0, 0, 0, 500);
            VectorSectionNode section = new VectorSectionNode(start, start.Tile, Vector3.Zero, end) { NodeIndex = 1 };
            VectorNode node = new VectorNode(start, start.Tile, end)
            {
                NodeIndex = 1,
                VectorSections = ImmutableArray.Create(section),
            };
            TrackDatabase database = new TrackDatabase
            {
                TrackNodes = ImmutableArray.Create<TrackNodeBase>(null, node),
                TrackNodeConnectors = ImmutableArray.Create(new TrackNodeConnectorIndex(), new TrackNodeConnectorIndex()),
                TrackItems = ImmutableArray<Riel.Models.Track.TrackItemBase>.Empty,
                TrackItemSelectors = ImmutableDictionary<int, TrackItemIndex>.Empty.Add(1,
                    new TrackItemIndex { TrackItems = ImmutableArray<int>.Empty }),
            };
            SimulationBoundaryFixture.Invoke(database, "OnSerializing");
            SimulationBoundaryFixture.Invoke(database, "OnSerialized");
            TrackWorld.Initialize(null, new TrackModel { TrackDatabase = database }, new TrackSectionModel
            {
                TrackSections = ImmutableDictionary<int, TrackSection>.Empty.Add(1,
                    new TrackSection { SectionIndex = 1, Gauge = 1.435f, Length = 500 }),
            });
            return node;
        }
    }
}
