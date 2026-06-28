using NUnit.Framework;
using KSPClone.SimCore;
using KSPClone.Net;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// End-to-end server → wire → client over the in-process loopback transport:
    /// handshake on connect, snapshot stream, and a client warp command routed
    /// back to the authoritative sim. Exercises the full WireCodec path without
    /// sockets (the LiteNetLib transport is a drop-in for the same interfaces).
    /// </summary>
    public sealed class NetLoopbackTests
    {
        private static ServerSimulation NewSim()
        {
            var world = new SimWorld(WorldSeed.CreateBodies());
            WorldSeed.Seed(world);
            return new ServerSimulation(world);
        }

        [Test]
        public void Connect_DeliversHandshake_ToClient()
        {
            var sim = NewSim();
            var lb = new LoopbackTransport();
            var host = new ServerNetHost(lb.Server, sim);
            var client = new ClientNetPeer(lb.Client);

            host.Poll();    // peer connects → handshake queued
            client.Poll();  // client drains → applies handshake

            Assert.AreEqual(1, sim.Connections.ConnectedCount);
            Assert.AreEqual(1, client.World.Vessels.Count);
            Assert.IsTrue(client.World.Vessels.ContainsKey(WorldSeed.SeedVesselId));
            Assert.AreEqual(0.0, client.ServerGameTime, 1e-9);
        }

        [Test]
        public void Snapshots_StreamToClient_AfterAdvance()
        {
            var sim = NewSim();
            var lb = new LoopbackTransport();
            var host = new ServerNetHost(lb.Server, sim);
            var client = new ClientNetPeer(lb.Client);

            host.Poll();
            client.Poll();

            for (int i = 0; i < 30; i++) sim.Advance(SimScheduler.FixedDt); // ~0.5 s → snapshots queued
            client.Poll();

            Assert.Greater(client.ServerGameTime, 0.0, "client clock advanced from snapshots");
            Assert.IsTrue(client.TrySampleVessel(WorldSeed.SeedVesselId, out var pos), "client received snapshots");
            Assert.Greater(pos.Length, 0.0);
        }

        [Test]
        public void ClientWarpRequest_RoutesToServer_AndActivates()
        {
            var sim = NewSim();
            var lb = new LoopbackTransport();
            var host = new ServerNetHost(lb.Server, sim);
            var client = new ClientNetPeer(lb.Client);

            host.Poll();
            client.Poll();

            client.RequestWarp(1000.0, WarpKind.OnRails);
            host.Poll();   // server drains the command → applies

            Assert.AreEqual(WarpState.Active, sim.Warp.State);
            Assert.AreEqual(1000.0, sim.World.Clock.Rate);
        }
    }
}
