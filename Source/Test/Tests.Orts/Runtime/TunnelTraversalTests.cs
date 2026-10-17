using System;

using Riel.Common;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Orts.Simulation.Physics;
using Orts.Simulation.RollingStocks;
using Orts.Simulation.Signalling;
using Orts.Simulation.Track;

namespace Tests.Orts.Runtime
{
    [TestClass]
    [DoNotParallelize]
    public class TunnelTraversalTests
    {
        private SimulationBoundaryFixture fixture;

        [TestInitialize]
        public void Initialize() => fixture = new SimulationBoundaryFixture();

        [TestCleanup]
        public void Cleanup() => fixture.Dispose();

        [TestMethod]
        [DataRow(TrackDirection.Ahead)]
        [DataRow(TrackDirection.Reverse)]
        public void CarsWithinOneSectionKeepTheirExistingTunnelPositions(TrackDirection direction)
        {
            TrackCircuitSection section = SimulationBoundaryFixture.Section(7, 100);
            AddTunnel(section);
            Train train = Train(direction, 50, section);
            TrainCar first = AddCar(train, 20);
            TrainCar second = AddCar(train, 10);

            Process(train);

            Assert.AreEqual(50f + DirectionOffset(direction), first.TunnelFrontPositionBeyondStart.Value);
            Assert.AreEqual(30f + DirectionOffset(direction), second.TunnelFrontPositionBeyondStart.Value);
            Assert.AreEqual(1000f - first.TunnelFrontPositionBeyondStart.Value, first.TunnelLengthAheadFront.Value);
            Assert.AreEqual(first.TunnelFrontPositionBeyondStart.Value - 20, first.TunnelLengthBehindRear.Value);
        }

        [TestMethod]
        [DataRow(TrackDirection.Ahead)]
        [DataRow(TrackDirection.Reverse)]
        public void CarSpanningBoundaryFindsPreviousCircuitAndPositionsNextCar(TrackDirection direction)
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTunnel(previous);
            Train train = Train(direction, 5, previous, current);
            TrainCar first = AddCar(train, 20);
            TrainCar second = AddCar(train, 10);

            Process(train);

            Assert.AreEqual(100f + DirectionOffset(direction), first.TunnelFrontPositionBeyondStart.Value);
            Assert.AreEqual(85f + DirectionOffset(direction), second.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        public void FindingTunnelStillConsumesWholeCarBeforePositioningFollowingCar()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTunnel(previous, 300);
            AddTunnel(current, 100);
            Train train = Train(TrackDirection.Ahead, 5, previous, current);
            TrainCar first = AddCar(train, 20);
            TrainCar second = AddCar(train, 10);

            Process(train);

            Assert.AreEqual(105f, first.TunnelFrontPositionBeyondStart.Value);
            Assert.AreEqual(385f, second.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        public void TunnelInFirstPartialSectionDoesNotLeakToFollowingCarAfterThreeSections()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection between = SimulationBoundaryFixture.Section(50, 10);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 10);
            AddTunnel(current);
            Train train = Train(TrackDirection.Ahead, 5, previous, between, current);
            TrainCar first = AddCar(train, 25);
            TrainCar second = AddCar(train, 10);

            Process(train);

            Assert.AreEqual(15f, first.TunnelFrontPositionBeyondStart.Value);
            Assert.IsFalse(second.TunnelFrontPositionBeyondStart.HasValue);
        }

        [TestMethod]
        public void BoundaryTraversalUsesDirectionOfThePreviousRouteElement()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTunnel(previous);
            Train train = Train(TrackDirection.Ahead, 5, previous, current);
            SimulationBoundaryFixture.SetProperty(train.ValidRoutes[Direction.Forward][0],
                nameof(TrackCircuitRouteElement.Direction), TrackDirection.Reverse);
            TrainCar car = AddCar(train, 20);

            Process(train);

            Assert.AreEqual(100f + DirectionOffset(TrackDirection.Reverse), car.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        [Timeout(2000)]
        public void RevisitedCircuitUsesPreviousRoutePositionWithoutLooping()
        {
            TrackCircuitSection tunnel = SimulationBoundaryFixture.Section(100, 50);
            TrackCircuitSection repeated = SimulationBoundaryFixture.Section(1, 10);
            TrackCircuitSection between = SimulationBoundaryFixture.Section(101, 10);
            AddTunnel(tunnel);
            Train train = Train(TrackDirection.Ahead, 5, tunnel, repeated, between, repeated);
            TrainCar car = AddCar(train, 30);

            Process(train);

            Assert.AreEqual(50f + DirectionOffset(TrackDirection.Ahead), car.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        [Timeout(2000)]
        public void ZeroLengthSectionsAdvanceToAnEarlierRoutePosition()
        {
            TrackCircuitSection tunnel = SimulationBoundaryFixture.Section(9, 100);
            TrackCircuitSection empty = SimulationBoundaryFixture.Section(8, 0);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(7, 0);
            AddTunnel(tunnel);
            Train train = Train(TrackDirection.Reverse, 0, tunnel, empty, current);
            TrainCar car = AddCar(train, 20);

            Process(train);

            Assert.AreEqual(100f + DirectionOffset(TrackDirection.Reverse), car.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        [Timeout(2000)]
        public void ZeroOffsetAtBeginningOfRouteTerminatesWithoutTunnelState()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(7, 100);
            AddTunnel(current);
            Train train = Train(TrackDirection.Ahead, 0, current);
            TrainCar car = AddCar(train, 20);

            Process(train);

            Assert.IsFalse(car.TunnelFrontPositionBeyondStart.HasValue);
        }

        [TestMethod]
        public void FollowingCarAtExactBoundaryUsesPreviousCircuit()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTunnel(previous, 300);
            AddTunnel(current, 100);
            Train train = Train(TrackDirection.Ahead, 20, previous, current);
            TrainCar first = AddCar(train, 20);
            TrainCar second = AddCar(train, 10);

            Process(train);

            Assert.AreEqual(120f, first.TunnelFrontPositionBeyondStart.Value);
            Assert.AreEqual(400f, second.TunnelFrontPositionBeyondStart.Value);
        }

        [TestMethod]
        [Timeout(2000)]
        public void MissingRouteStopsAtBoundaryAndClearsOldTunnelState()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(7, 100);
            Train train = Train(TrackDirection.Ahead, 5, current);
            train.ValidRoutes[Direction.Forward] = null;
            TrainCar car = AddCar(train, 20);
            SimulationBoundaryFixture.SetProperty(car, nameof(TrainCar.TunnelFrontPositionBeyondStart), 10f);

            Process(train);

            Assert.IsFalse(car.TunnelFrontPositionBeyondStart.HasValue);
            Assert.IsFalse(car.TunnelLengthAheadFront.HasValue);
            Assert.IsFalse(car.TunnelLengthBehindRear.HasValue);
        }

        [TestMethod]
        public void StaleRouteCursorDoesNotJumpToAnUnrelatedCircuit()
        {
            TrackCircuitSection unrelated = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTunnel(unrelated);
            Train train = Train(TrackDirection.Ahead, 5, unrelated, current);
            SimulationBoundaryFixture.SetProperty(train.PresentPosition[Direction.Forward], nameof(TrackCircuitPosition.RouteListIndex), 0);
            TrainCar car = AddCar(train, 20);

            Process(train);

            Assert.IsFalse(car.TunnelFrontPositionBeyondStart.HasValue);
        }

        private static Train Train(TrackDirection direction, float offset, params TrackCircuitSection[] sections)
        {
            Train train = SimulationBoundaryFixture.Train();
            TrackCircuitPartialPathRoute route = new TrackCircuitPartialPathRoute();
            int previous = -1;
            foreach (TrackCircuitSection section in sections)
            {
                route.Add(new TrackCircuitRouteElement(section, direction, previous));
                previous = section.Index;
            }
            train.ValidRoutes[Direction.Forward] = route;
            TrackCircuitPosition position = train.PresentPosition[Direction.Forward];
            SimulationBoundaryFixture.SetProperty(position, nameof(TrackCircuitPosition.TrackCircuitSectionIndex), sections[^1].Index);
            SimulationBoundaryFixture.SetProperty(position, nameof(TrackCircuitPosition.RouteListIndex), sections.Length - 1);
            SimulationBoundaryFixture.SetProperty(position, nameof(TrackCircuitPosition.Direction), direction);
            SimulationBoundaryFixture.SetProperty(position, nameof(TrackCircuitPosition.Offset), offset);
            return train;
        }

        private static TrainCar AddCar(Train train, float length)
        {
            TrainCar car = SimulationBoundaryFixture.Uninitialized<MSTSWagon>();
            SimulationBoundaryFixture.SetProperty(car, nameof(TrainCar.CarLengthM), length);
            train.Cars.Add(car);
            return car;
        }

        private static void Process(Train train) => SimulationBoundaryFixture.Invoke(train, "ProcessTunnels");

        private static float DirectionOffset(TrackDirection direction) => direction == TrackDirection.Ahead ? 10 : 20;

        private static void AddTunnel(TrackCircuitSection section, float? offset = null)
        {
            Type type = typeof(SignalEnvironment).Assembly.GetType("Orts.Simulation.Signalling.TunnelInfoData", true);
            object tunnel = Activator.CreateInstance(type, 1, -1f, -1f, section.Length, 1000f, section.Length, 0f);
            EnumArray<float, TrackDirection> offsets = (EnumArray<float, TrackDirection>)type.GetProperty("SectionStartOffset").GetValue(tunnel);
            offsets[TrackDirection.Ahead] = offset ?? DirectionOffset(TrackDirection.Ahead);
            offsets[TrackDirection.Reverse] = offset ?? DirectionOffset(TrackDirection.Reverse);
            SimulationBoundaryFixture.Invoke(section, "AddTunnelData", tunnel);
        }
    }
}
