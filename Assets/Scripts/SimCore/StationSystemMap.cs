#nullable enable annotations

using System;
using System.Collections.Generic;

namespace KSPClone.SimCore
{
    /// <summary>
    /// A controllable system on a vessel. Each one is owned by exactly one
    /// <see cref="Station"/> (CREW-1, Art. 6). M1 only acts on the Pilot
    /// systems (Attitude, Throttle); the rest gain real subsystems in later
    /// slices, but the partition is fixed now so input routing can be proven
    /// disjoint by construction.
    /// </summary>
    public enum ControllableSystem
    {
        Attitude,
        Throttle,
        Staging,
        Resources,
        Power,
        Abort,
        ManeuverNode,
        MapPlanning,
    }

    /// <summary>
    /// The fixed partition of every <see cref="ControllableSystem"/> across the
    /// three <see cref="Station"/>s (M2-T04, CREW-1):
    ///   Pilot     = Attitude, Throttle
    ///   Engineer  = Staging, Resources, Power, Abort
    ///   Navigator = ManeuverNode, MapPlanning
    ///
    /// The map is a function system → station, so two stations can never own
    /// the same system: disjointness holds by construction. The static
    /// constructor still self-checks the derived per-station partition for
    /// BOTH pairwise disjointness AND full coverage (M2-T04), so adding a
    /// system without mapping it — or, in any partition fed to
    /// <see cref="ValidatePartition"/>, assigning one to two stations — fails
    /// fast.
    /// </summary>
    public static class StationSystemMap
    {
        private static readonly Dictionary<ControllableSystem, Station> Owner = new()
        {
            { ControllableSystem.Attitude,     Station.Pilot },
            { ControllableSystem.Throttle,     Station.Pilot },
            { ControllableSystem.Staging,      Station.Engineer },
            { ControllableSystem.Resources,    Station.Engineer },
            { ControllableSystem.Power,        Station.Engineer },
            { ControllableSystem.Abort,        Station.Engineer },
            { ControllableSystem.ManeuverNode, Station.Navigator },
            { ControllableSystem.MapPlanning,  Station.Navigator },
        };

        private static readonly Dictionary<Station, ControllableSystem[]> Systems = BuildSystems();

        static StationSystemMap() => Validate();

        /// <summary>The station that owns this system.</summary>
        public static Station OwnerOf(ControllableSystem system) => Owner[system];

        /// <summary>True iff <paramref name="station"/> owns <paramref name="system"/>.</summary>
        public static bool Owns(Station station, ControllableSystem system) => Owner[system] == station;

        /// <summary>The systems a station owns.</summary>
        public static IReadOnlyList<ControllableSystem> SystemsOf(Station station) => Systems[station];

        /// <summary>
        /// Self-check (called at load): the live partition is pairwise
        /// disjoint and covers every <see cref="ControllableSystem"/>.
        /// </summary>
        public static void Validate() => ValidatePartition(Systems);

        /// <summary>
        /// Assert that <paramref name="partition"/> assigns every
        /// <see cref="ControllableSystem"/> to exactly one station (Art. 6):
        /// no system owned by two stations (pairwise disjoint) and none left
        /// unowned (full coverage). Throws <see cref="InvalidOperationException"/>
        /// otherwise. Exposed so the partition contract is testable with a
        /// deliberately-bad partition (M2-T04 acceptance).
        /// </summary>
        public static void ValidatePartition(IReadOnlyDictionary<Station, ControllableSystem[]> partition)
        {
            var owner = new Dictionary<ControllableSystem, Station>();
            foreach (var kv in partition)
                foreach (var system in kv.Value)
                {
                    if (owner.TryGetValue(system, out var other))
                        throw new InvalidOperationException(
                            $"StationSystemMap partition is not disjoint: {system} is owned by both {other} and {kv.Key}.");
                    owner[system] = kv.Key;
                }

            foreach (ControllableSystem system in Enum.GetValues(typeof(ControllableSystem)))
                if (!owner.ContainsKey(system))
                    throw new InvalidOperationException(
                        $"StationSystemMap is incomplete: {system} is not assigned to a station.");
        }

        private static Dictionary<Station, ControllableSystem[]> BuildSystems()
        {
            var byStation = new Dictionary<Station, List<ControllableSystem>>();
            foreach (var kv in Owner)
            {
                if (!byStation.TryGetValue(kv.Value, out var list))
                    byStation[kv.Value] = list = new List<ControllableSystem>();
                list.Add(kv.Key);
            }
            var result = new Dictionary<Station, ControllableSystem[]>();
            foreach (var kv in byStation) result[kv.Key] = kv.Value.ToArray();
            return result;
        }
    }
}
