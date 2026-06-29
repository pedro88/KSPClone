using System.Collections.Generic;
using NUnit.Framework;
using KSPClone.SimCore;
using KSPClone.Net;

namespace KSPClone.SimCore.Tests
{
    /// <summary>
    /// P-1 wire-format round-trips. Full snapshots, raw doubles → encode/decode
    /// must be lossless (the reconciliation path in M1 depends on it).
    /// </summary>
    public sealed class WireCodecTests
    {
        [Test]
        public void Handshake_RoundTrips_AllVesselElements()
        {
            var id = VesselId.New();
            var orbit = new Orbit(7_000_000.5, 0.3, 0.5, 1.0, 2.0, 0.7, 123.0, CelestialBodyId.Planet);
            var msg = new WorldHandshakeMessage(456.0,
                new List<HandshakeVessel> { new(id, orbit, onRails: true) });

            var bytes = WireCodec.EncodeHandshake(msg);
            Assert.AreEqual(MessageType.Handshake, WireCodec.PeekType(bytes));

            var back = WireCodec.DecodeHandshake(bytes);
            Assert.AreEqual(456.0, back.GameTimeSeconds);
            Assert.AreEqual(1, back.Vessels.Count);
            var v = back.Vessels[0];
            Assert.AreEqual(id.Value, v.Id.Value);
            Assert.IsTrue(v.OnRails);
            Assert.AreEqual(CelestialBodyId.Planet, v.Orbit.ParentBody);
            Assert.AreEqual(orbit.SemiMajorAxis, v.Orbit.SemiMajorAxis);
            Assert.AreEqual(orbit.Eccentricity, v.Orbit.Eccentricity);
            Assert.AreEqual(orbit.Inclination, v.Orbit.Inclination);
            Assert.AreEqual(orbit.LongitudeOfAscendingNode, v.Orbit.LongitudeOfAscendingNode);
            Assert.AreEqual(orbit.ArgumentOfPeriapsis, v.Orbit.ArgumentOfPeriapsis);
            Assert.AreEqual(orbit.MeanAnomalyAtEpoch, v.Orbit.MeanAnomalyAtEpoch);
            Assert.AreEqual(orbit.EpochGameTime, v.Orbit.EpochGameTime);
        }

        [Test]
        public void Snapshot_RoundTrips_PositionVelocitySeq()
        {
            var id = VesselId.New();
            var pos = new Vector3d(1_234.5, -6_789.0, 42.0);
            var vel = new Vector3d(-1.5, 2.5, -3.5);
            var bundle = new SnapshotBundle(789.0, 42L,
                new List<VesselSnapshot> { new(id, 789.0, 42L, pos, vel) });

            var bytes = WireCodec.EncodeSnapshot(bundle);
            Assert.AreEqual(MessageType.Snapshot, WireCodec.PeekType(bytes));

            var back = WireCodec.DecodeSnapshot(bytes);
            Assert.AreEqual(789.0, back.GameTime);
            Assert.AreEqual(42L, back.Seq);
            Assert.AreEqual(1, back.Vessels.Count);
            var s = back.Vessels[0];
            Assert.AreEqual(id.Value, s.VesselId.Value);
            Assert.AreEqual(42L, s.Seq);
            Assert.AreEqual(pos.X, s.Position.X);
            Assert.AreEqual(pos.Y, s.Position.Y);
            Assert.AreEqual(pos.Z, s.Position.Z);
            Assert.AreEqual(vel.X, s.Velocity.X);
            Assert.AreEqual(vel.Y, s.Velocity.Y);
            Assert.AreEqual(vel.Z, s.Velocity.Z);
        }

        [Test]
        public void Snapshot_RoundTrips_AngularVelocityAndAck()
        {
            var id = VesselId.New();
            var angVel = new Vector3d(0.1, -0.2, 0.3);
            var bundle = new SnapshotBundle(1.0, 7L, new List<VesselSnapshot>
            {
                new(id, 1.0, 7L, Vector3d.Zero, Vector3d.Zero, angVel, lastProcessedClientTick: 99L),
            });

            var back = WireCodec.DecodeSnapshot(WireCodec.EncodeSnapshot(bundle));
            var s = back.Vessels[0];
            Assert.AreEqual(angVel.X, s.AngularVelocity.X);
            Assert.AreEqual(angVel.Y, s.AngularVelocity.Y);
            Assert.AreEqual(angVel.Z, s.AngularVelocity.Z);
            Assert.AreEqual(99L, s.LastProcessedClientTick, "The reconciliation ack must survive the wire.");
        }

        [Test]
        public void ClientCommand_RoundTrips_RequestAndApprove()
        {
            var req = WireCodec.EncodeClientCommand(ClientCommand.RequestWarp(1000.0, WarpKind.OnRails));
            Assert.AreEqual(MessageType.ClientCommand, WireCodec.PeekType(req));
            var rb = WireCodec.DecodeClientCommand(req);
            Assert.AreEqual(ClientCommandType.RequestWarp, rb.Type);
            Assert.AreEqual(1000.0, rb.Multiplier);
            Assert.AreEqual(WarpKind.OnRails, rb.Kind);

            var ab = WireCodec.DecodeClientCommand(
                WireCodec.EncodeClientCommand(ClientCommand.ApproveWarp()));
            Assert.AreEqual(ClientCommandType.ApproveWarp, ab.Type);
        }
    }
}
