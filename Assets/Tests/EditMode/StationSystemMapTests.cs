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
    }
}
