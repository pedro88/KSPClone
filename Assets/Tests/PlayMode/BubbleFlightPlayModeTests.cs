#nullable enable annotations

using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using KSPClone.SimCore;
using KSPClone.Server;

namespace KSPClone.PlayModeTests
{
    /// <summary>
    /// Runtime validation of the one thing the headless EditMode suite cannot
    /// exercise: the engine-coupled active-physics step (M1-T21). Builds the
    /// real server active-physics column — UnityBubbleHost (a PhysicsScene per
    /// bubble) + BubbleIntegrator injected as the IBubbleStepper + the
    /// rigid-body lifecycle — drives the fixed tick, and asserts that PhysX
    /// actually moves the vessel under gravity and produces the expected
    /// delta-v under thrust (PHYS-2/4). No transport, no Postgres: the loop is
    /// driven in-process so the test is deterministic apart from PhysX itself.
    /// </summary>
    public sealed class BubbleFlightPlayModeTests
    {
        /// <summary>Owns one fully-wired server sim and its Unity scenes; disposes them on exit.</summary>
        private sealed class Harness : IDisposable
        {
            public ServerSimulation Sim = null!;
            public UnityBubbleHost Host = null!;
            public ServerVesselBodies Bodies = null!;

            public void Dispose()
            {
                Bodies?.Dispose();
                Host?.Dispose();
            }
        }

        private static Harness Build()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            var sim = new ServerSimulation(world);
            sim.Masses.Set(WorldSeed.SeedVesselId, WorldSeed.CreateMass());
            sim.Engines.Set(WorldSeed.SeedVesselId, WorldSeed.CreateEngines());

            var host = new UnityBubbleHost(sim.Bubbles);
            var integrator = new BubbleIntegrator(
                sim.World, sim.Bubbles, host, new FloatingOriginManager(), sim.Engines, sim.Masses);
            sim.SetBubbleStepper(new BubbleIntegratorStepper(integrator));
            var bodies = new ServerVesselBodies(sim, host);

            return new Harness { Sim = sim, Host = host, Bodies = bodies };
        }

        private static void Tick(ServerSimulation sim, int ticks)
        {
            for (int i = 0; i < ticks; i++)
                sim.Advance(SimScheduler.FixedDt);
        }

        [UnityTest]
        public IEnumerator OccupyingPilot_SpawnsRigidBody_AndItCoastsUnderGravity()
        {
            yield return null; // enter a stable play-mode physics frame

            using var h = Build();
            var pilot = PlayerId.New();
            Assert.IsTrue(h.Sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot));

            Tick(h.Sim, 1); // promotion + bubble scene + rigid body, all on tick 1

            var v = h.Sim.World.Vessels[WorldSeed.SeedVesselId];
            Assert.AreEqual(VesselState.ActivePhysics, v.State, "Occupying Pilot promotes the vessel.");
            Assert.AreEqual(1, h.Bodies.ActiveBodyCount, "A RigidVesselBody exists in the bubble scene.");
            Assert.IsTrue(v.CachedLocalPosition.HasValue, "The integrator wrote back a local transform.");

            var before = v.CachedWorldPosition!.Value;
            Tick(h.Sim, 120); // 2 s of free coast under SOI gravity
            var after = v.CachedWorldPosition!.Value;

            Assert.Greater((after - before).Length, 1.0,
                "PhysX integrates the vessel under gravity (it must move).");
        }

        [UnityTest]
        public IEnumerator FullThrottle_ProducesTsiolkovskyDeltaV()
        {
            yield return null;

            const int ticks = 120; // 2 s
            var coastDv = RunBurn(throttle: 0f, ticks);   // gravity only
            var burnDv = RunBurn(throttle: 1f, ticks);    // gravity + full thrust

            // Gravity is common to both runs (trajectories diverge by only metres),
            // so the difference isolates the thrust delta-v.
            var measured = (burnDv - coastDv).Length;

            var ve = WorldSeed.SeedEngineIspS * EngineModule.G0;
            var mdot = WorldSeed.SeedEngineThrustN / ve;
            var t = ticks * SimScheduler.FixedDt;
            var m1 = WorldSeed.SeedWetMassKg - mdot * t;
            var expected = ve * Math.Log(WorldSeed.SeedWetMassKg / m1);

            Assert.That(measured, Is.EqualTo(expected).Within(0.05 * expected),
                $"Thrust delta-v must match Tsiolkovsky (expected ~{expected:F1} m/s, got {measured:F1}).");
        }

        // Run a single burn and return the controlled vessel's velocity change.
        private Vector3d RunBurn(float throttle, int ticks)
        {
            using var h = Build();
            var pilot = PlayerId.New();
            h.Sim.OccupyStation(pilot, WorldSeed.SeedVesselId, Station.Pilot);
            Tick(h.Sim, 1);

            if (throttle > 0f)
            {
                h.Sim.SubmitPilotInput(pilot, new PilotInputMessage(
                    WorldSeed.SeedVesselId, clientTick: 1, throttle: throttle,
                    pitchRate: 0, yawRate: 0, rollRate: 0));
            }

            var v0 = h.Sim.World.Vessels[WorldSeed.SeedVesselId].CachedWorldVelocity!.Value;
            Tick(h.Sim, ticks);
            var v1 = h.Sim.World.Vessels[WorldSeed.SeedVesselId].CachedWorldVelocity!.Value;
            return v1 - v0;
        }
    }
}
