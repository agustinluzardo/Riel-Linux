using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;

using Riel.Common;
using Riel.Common.Position;
using Riel.Models.Content;
using Riel.Runtime.Track;

using Orts.Simulation;
using Orts.Simulation.Physics;
using Orts.Simulation.Track;

namespace Tests.Orts.Runtime
{
    internal sealed class SimulationBoundaryFixture : IDisposable
    {
        private readonly Simulator previousSimulator = Simulator.Instance;
        private readonly TrackCircuitSection[] previousSections = TrackCircuitSection.TrackCircuitList.ToArray();
        private readonly TrackWorld previousWorld = TrackWorld.Instance;

        internal SimulationBoundaryFixture()
        {
            Simulator simulator = Uninitialized<Simulator>();
            SetProperty(simulator, nameof(Simulator.RouteModel), new RouteModel(WorldLocation.None)
            {
                Settings = ImmutableDictionary<string, string>.Empty,
                SpeedRestrictions = new EnumArray<float, SpeedRestrictionType>(30),
            });
            typeof(Simulator).GetProperty(nameof(Simulator.Instance)).SetValue(null, simulator);
            TrackCircuitSection.TrackCircuitList.Clear();
        }

        public void Dispose()
        {
            TrackCircuitSection.TrackCircuitList.Clear();
            TrackCircuitSection.TrackCircuitList.AddRange(previousSections);
            typeof(Simulator).GetProperty(nameof(Simulator.Instance)).SetValue(null, previousSimulator);
            GameService<TrackWorld>.Set(null, previousWorld);
        }

        internal static T Uninitialized<T>() where T : class =>
            (T)RuntimeHelpers.GetUninitializedObject(typeof(T));

        internal static void SetField(object target, string name, object value)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
            }
            throw new MissingFieldException(target.GetType().FullName, name);
        }

        internal static void SetProperty(object target, string name, object value) =>
            target.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).SetValue(target, value);

        internal static object Invoke(object target, string name, params object[] arguments)
        {
            try
            {
                return target.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(target, arguments);
            }
            catch (TargetInvocationException exception) when (exception.InnerException != null)
            {
                ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
                throw;
            }
        }

        internal static TrackCircuitSection Section(int index, float length)
        {
            while (TrackCircuitSection.TrackCircuitList.Count <= index)
                TrackCircuitSection.TrackCircuitList.Add(new TrackCircuitSection(TrackCircuitSection.TrackCircuitList.Count));
            TrackCircuitSection section = new TrackCircuitSection(index);
            SetProperty(section, nameof(TrackCircuitSection.Length), length);
            TrackCircuitSection.TrackCircuitList[index] = section;
            return section;
        }

        internal static Train Train()
        {
            Train train = Uninitialized<Train>();
            SetField(train, "<Cars>k__BackingField", new List<Orts.Simulation.RollingStocks.TrainCar>());
            SetField(train, "<PresentPosition>k__BackingField", new EnumArray<TrackCircuitPosition, Direction>(() => new TrackCircuitPosition()));
            SetField(train, "<ValidRoutes>k__BackingField", new EnumArray<TrackCircuitPartialPathRoute, Direction>());
            return train;
        }
    }
}
