#nullable enable annotations

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// The station→system partition is provably disjoint and complete
    /// (M2-T04, CREW-1, Art. 6): no system owned by two stations, every
    /// system owned by one.
    /// </summary>
    public sealed class StationSystemMapTests
    {
        private static readonly Station[] AllStations =
            (Station[])Enum.GetValues(typeof(Station));

        [Test]
        public void EveryStationPair_HasDisjointSystems()
        {
            foreach (var a in AllStations)
                foreach (var b in AllStations)
                {
                    if (a == b) continue;
                    var shared = StationSystemMap.SystemsOf(a).Intersect(StationSystemMap.SystemsOf(b));
                    CollectionAssert.IsEmpty(shared, $"{a} and {b} must own disjoint systems.");
                }
        }

        [Test]
        public void UnionOfStationSystems_CoversEverySystem()
        {
            var covered = new HashSet<ControllableSystem>();
            foreach (var s in AllStations)
                covered.UnionWith(StationSystemMap.SystemsOf(s));

            var all = (ControllableSystem[])Enum.GetValues(typeof(ControllableSystem));
            CollectionAssert.AreEquivalent(all, covered, "Every controllable system must be owned by some station.");
        }

        [Test]
        public void OwnershipMatchesTheSpecPartition()
        {
            Assert.AreEqual(Station.Pilot, StationSystemMap.OwnerOf(ControllableSystem.Attitude));
            Assert.AreEqual(Station.Pilot, StationSystemMap.OwnerOf(ControllableSystem.Throttle));
            Assert.AreEqual(Station.Engineer, StationSystemMap.OwnerOf(ControllableSystem.Staging));
            Assert.AreEqual(Station.Navigator, StationSystemMap.OwnerOf(ControllableSystem.ManeuverNode));

            Assert.IsTrue(StationSystemMap.Owns(Station.Pilot, ControllableSystem.Throttle));
            Assert.IsFalse(StationSystemMap.Owns(Station.Pilot, ControllableSystem.Staging));
        }

        [Test]
        public void Validate_PassesForTheShippedMap()
        {
            Assert.DoesNotThrow(StationSystemMap.Validate);
        }

        [Test]
        public void ValidatePartition_Throws_WhenASystemIsOwnedByTwoStations()
        {
            // M2-T04 acceptance: "deliberately duplicating a system across two
            // stations fails the check." Attitude assigned to both Pilot and
            // Engineer → the disjointness assertion must throw.
            var notDisjoint = new Dictionary<Station, ControllableSystem[]>
            {
                { Station.Pilot,     new[] { ControllableSystem.Attitude, ControllableSystem.Throttle } },
                { Station.Engineer,  new[] { ControllableSystem.Attitude, ControllableSystem.Staging, ControllableSystem.Resources, ControllableSystem.Power, ControllableSystem.Abort } },
                { Station.Navigator, new[] { ControllableSystem.ManeuverNode, ControllableSystem.MapPlanning } },
            };
            var ex = Assert.Throws<InvalidOperationException>(
                () => StationSystemMap.ValidatePartition(notDisjoint));
            StringAssert.Contains("not disjoint", ex!.Message);
        }

        [Test]
        public void ValidatePartition_Throws_WhenASystemIsUnowned()
        {
            // Coverage half of the self-check: drop Abort → must throw incomplete.
            var incomplete = new Dictionary<Station, ControllableSystem[]>
            {
                { Station.Pilot,     new[] { ControllableSystem.Attitude, ControllableSystem.Throttle } },
                { Station.Engineer,  new[] { ControllableSystem.Staging, ControllableSystem.Resources, ControllableSystem.Power } },
                { Station.Navigator, new[] { ControllableSystem.ManeuverNode, ControllableSystem.MapPlanning } },
            };
            var ex = Assert.Throws<InvalidOperationException>(
                () => StationSystemMap.ValidatePartition(incomplete));
            StringAssert.Contains("incomplete", ex!.Message);
        }
    }
}
