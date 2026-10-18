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
    public class WaterTroughTraversalTests
    {
        private SimulationBoundaryFixture fixture;

        [TestInitialize]
        public void Initialize() => fixture = new SimulationBoundaryFixture();

        [TestCleanup]
        public void Cleanup() => fixture.Dispose();

        [TestMethod]
        [DataRow(TrackDirection.Ahead, 10f, 60f, true)]
        [DataRow(TrackDirection.Reverse, 10f, 60f, true)]
        [DataRow(TrackDirection.Ahead, 30f, 60f, false)]
        [DataRow(TrackDirection.Reverse, 30f, 60f, false)]
        [DataRow(TrackDirection.Ahead, 10f, 30f, true)]
        [DataRow(TrackDirection.Reverse, 10f, 30f, true)]
        [DataRow(TrackDirection.Ahead, 10f, 29f, false)]
        [DataRow(TrackDirection.Reverse, 10f, 29f, false)]
        [DataRow(TrackDirection.Ahead, 51f, 60f, false)]
        [DataRow(TrackDirection.Reverse, 51f, 60f, false)]
        [DataRow(TrackDirection.Ahead, -1f, -1f, true)]
        [DataRow(TrackDirection.Reverse, -1f, -1f, true)]
        public void WithinOneSectionPreservesExistingTroughBoundaries(TrackDirection direction, float start, float end, bool expected)
        {
            TrackCircuitSection section = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(section, direction, start, end);
            TrainCar car = Car(direction, 50, 20, section);

            Assert.AreEqual(expected, IsOverTrough(car));
        }

        [TestMethod]
        [DataRow(TrackDirection.Ahead)]
        [DataRow(TrackDirection.Reverse)]
        public void CarSpanningBoundaryFindsTroughInPreviousCircuit(TrackDirection direction)
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(previous, direction, 50, 100);
            TrainCar car = Car(direction, 5, 20, previous, current);

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        [DataRow(5f)]
        [DataRow(4f)]
        public void CompletedCarDoesNotInspectTroughBehindItsRear(float carLength)
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(previous, TrackDirection.Ahead, -1, -1);
            TrainCar car = Car(TrackDirection.Ahead, 5, carLength, previous, current);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        public void PartialPreviousSectionDoesNotInspectTroughBehindCar()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(previous, TrackDirection.Ahead, 50, 84);
            TrainCar car = Car(TrackDirection.Ahead, 5, 20, previous, current);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        public void BoundaryTraversalUsesPreviousRouteElementDirection()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(previous, TrackDirection.Reverse, 80, 100);
            TrainCar car = Car(TrackDirection.Ahead, 5, 20, previous, current);
            SimulationBoundaryFixture.SetProperty(car.Train.ValidRoutes[Direction.Forward][0],
                nameof(TrackCircuitRouteElement.Direction), TrackDirection.Reverse);

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        public void TroughAheadInCurrentSectionDoesNotHidePreviousSection()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(previous, TrackDirection.Ahead, 50, 100);
            AddTrough(current, TrackDirection.Ahead, 40, 60);
            TrainCar car = Car(TrackDirection.Ahead, 5, 20, previous, current);

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        public void RepeatedCircuitTraversesEarlierPositionsUntilCarIsConsumed()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(100, 100);
            TrackCircuitSection repeated = SimulationBoundaryFixture.Section(1, 10);
            TrackCircuitSection between = SimulationBoundaryFixture.Section(101, 10);
            AddTrough(previous, TrackDirection.Ahead, 50, 100);
            // This is ahead of the initial position, so the unfixed code returns
            // false instead of hanging. The fixed traversal must visit the earlier
            // occurrence and continue to the final five metres in circuit 100.
            AddTrough(repeated, TrackDirection.Ahead, 6, 9);
            TrainCar car = Car(TrackDirection.Ahead, 5, 30, previous, repeated, between, repeated);

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        public void RepeatedCircuitWithoutTroughStopsAtRouteStart()
        {
            TrackCircuitSection repeated = SimulationBoundaryFixture.Section(70, 10);
            TrackCircuitSection between = SimulationBoundaryFixture.Section(101, 10);
            // IDs deliberately differ from route positions; this case also
            // terminates against the old implementation without leaked test tasks.
            TrainCar car = Car(TrackDirection.Reverse, 5, 100, repeated, between, repeated);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        [DataRow(TrackDirection.Ahead)]
        [DataRow(TrackDirection.Reverse)]
        public void ZeroLengthSectionsAdvanceWithoutFalseTroughMatch(TrackDirection direction)
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection empty = SimulationBoundaryFixture.Section(50, 0);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 0);
            AddTrough(previous, direction, 50, 100);
            TrainCar car = Car(direction, 0, 20, previous, empty, current);

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        public void ZeroOffsetAtRouteStartHasNoOccupiedTroughLength()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(current, TrackDirection.Ahead, -1, -1);
            TrainCar car = Car(TrackDirection.Ahead, 0, 20, current);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        public void AllZeroLengthSectionsEndAtRouteStart()
        {
            TrackCircuitSection previous = SimulationBoundaryFixture.Section(30, 0);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 0);
            AddTrough(previous, TrackDirection.Ahead, -1, -1);
            AddTrough(current, TrackDirection.Ahead, -1, -1);
            TrainCar car = Car(TrackDirection.Ahead, 0, 20, previous, current);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        public void MissingRouteStillDetectsTroughInCurrentSection()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(current, TrackDirection.Ahead, 10, 60);
            TrainCar car = Car(TrackDirection.Ahead, 50, 20, current);
            car.Train.ValidRoutes[Direction.Forward] = null;

            Assert.IsTrue(IsOverTrough(car));
        }

        [TestMethod]
        public void MissingRouteStopsAtSectionBoundary()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            TrainCar car = Car(TrackDirection.Ahead, 5, 20, current);
            car.Train.ValidRoutes[Direction.Forward] = null;

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        [DataRow(-1)]
        [DataRow(1)]
        [DataRow(9)]
        public void InvalidRouteCursorCannotJumpToUnrelatedCircuit(int cursor)
        {
            TrackCircuitSection unrelated = SimulationBoundaryFixture.Section(30, 100);
            TrackCircuitSection intervening = SimulationBoundaryFixture.Section(50, 100);
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(unrelated, TrackDirection.Ahead, -1, -1);
            AddTrough(intervening, TrackDirection.Ahead, -1, -1);
            TrainCar car = Car(TrackDirection.Ahead, 5, 20, unrelated, intervening, current);
            SimulationBoundaryFixture.SetProperty(car.Train.PresentPosition[Direction.Forward],
                nameof(TrackCircuitPosition.RouteListIndex), cursor);

            Assert.IsFalse(IsOverTrough(car));
        }

        [TestMethod]
        public void CarLaterInConsistKeepsExistingFrontOfTrainAnchor()
        {
            TrackCircuitSection current = SimulationBoundaryFixture.Section(70, 100);
            AddTrough(current, TrackDirection.Ahead, 20, 60);
            TrainCar car = Car(TrackDirection.Ahead, 50, 20, current);
            TrainCar first = SimulationBoundaryFixture.Uninitialized<MSTSWagon>();
            SimulationBoundaryFixture.SetProperty(first, nameof(TrainCar.CarLengthM), 40f);
            car.Train.Cars.Insert(0, first);

            Assert.IsTrue(IsOverTrough(car));
        }

        private static TrainCar Car(TrackDirection direction, float offset, float length, params TrackCircuitSection[] sections)
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
            TrainCar car = SimulationBoundaryFixture.Uninitialized<MSTSWagon>();
            SimulationBoundaryFixture.SetProperty(car, nameof(TrainCar.CarLengthM), length);
            SimulationBoundaryFixture.SetProperty(car, nameof(TrainCar.Train), train);
            train.Cars.Add(car);
            return car;
        }

        private static bool IsOverTrough(TrainCar car) => (bool)SimulationBoundaryFixture.Invoke(car, "IsOverTrough");

        private static void AddTrough(TrackCircuitSection section, TrackDirection direction, float start, float end)
        {
            Type type = typeof(SignalEnvironment).Assembly.GetType("Orts.Simulation.Signalling.TroughInfoData", true);
            float reverseStart = direction == TrackDirection.Reverse ? start : end < 0 ? -1 : section.Length - end;
            float reverseEnd = direction == TrackDirection.Reverse ? end : start < 0 ? -1 : section.Length - start;
            object trough = Activator.CreateInstance(type, reverseStart, reverseEnd, section.Length, section.Length, section.Length, 0f);
            SimulationBoundaryFixture.Invoke(section, "AddTroughData", trough);
        }
    }
}
