using System;
using System.Collections.Generic;
using System.IO;
using KSPClone.SimCore;

namespace KSPClone.Net
{
    /// <summary>
    /// The M0 wire format (plan P-1). Encodes/decodes the wire-agnostic SimCore
    /// messages to/from <c>byte[]</c> so any transport (loopback, LiteNetLib)
    /// ships the same bytes. Pure full snapshots — no delta/baseline yet
    /// (added with the prediction loop in M1). Each payload begins with a
    /// <see cref="MessageType"/> tag byte so the receiver can dispatch.
    ///
    /// Lives outside SimCore (ADR-0009): serialization is transport concern.
    /// </summary>
    public static class WireCodec
    {
        public static MessageType PeekType(byte[] payload) => (MessageType)payload[0];

        // ---- server → client: world handshake ----

        public static byte[] EncodeHandshake(WorldHandshakeMessage handshake)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.Handshake);
            w.Write(handshake.GameTimeSeconds);
            w.Write(handshake.Vessels.Count);
            foreach (var v in handshake.Vessels)
            {
                WriteVesselId(w, v.Id);
                WriteOrbit(w, v.Orbit);
                w.Write(v.OnRails);
            }
            return ms.ToArray();
        }

        public static WorldHandshakeMessage DecodeHandshake(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms);
            r.ReadByte(); // type tag
            var gameTime = r.ReadDouble();
            var count = r.ReadInt32();
            var vessels = new List<HandshakeVessel>(count);
            for (var i = 0; i < count; i++)
            {
                var id = ReadVesselId(r);
                var orbit = ReadOrbit(r);
                var onRails = r.ReadBoolean();
                vessels.Add(new HandshakeVessel(id, orbit, onRails));
            }
            return new WorldHandshakeMessage(gameTime, vessels);
        }

        // ---- server → client: snapshot bundle ----

        public static byte[] EncodeSnapshot(SnapshotBundle bundle)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.Snapshot);
            w.Write(bundle.GameTime);
            w.Write(bundle.Seq);
            w.Write(bundle.Vessels.Count);
            foreach (var s in bundle.Vessels)
            {
                WriteVesselId(w, s.VesselId);
                w.Write(s.GameTime);
                w.Write(s.Seq);
                WriteVector(w, s.Position);
                WriteVector(w, s.Velocity);
                WriteVector(w, s.AngularVelocity);
                WriteQuaternion(w, s.Orientation);
                w.Write(s.LastProcessedClientTick);
            }
            return ms.ToArray();
        }

        public static SnapshotBundle DecodeSnapshot(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms);
            r.ReadByte(); // type tag
            var gameTime = r.ReadDouble();
            var seq = r.ReadInt64();
            var count = r.ReadInt32();
            var vessels = new List<VesselSnapshot>(count);
            for (var i = 0; i < count; i++)
            {
                var id = ReadVesselId(r);
                var t = r.ReadDouble();
                var s = r.ReadInt64();
                var pos = ReadVector(r);
                var vel = ReadVector(r);
                var angVel = ReadVector(r);
                var orient = ReadQuaternion(r);
                var lastTick = r.ReadInt64();
                vessels.Add(new VesselSnapshot(id, t, s, pos, vel, angVel, orient, lastTick));
            }
            return new SnapshotBundle(gameTime, seq, vessels);
        }

        // ---- client → server: command ----

        public static byte[] EncodeClientCommand(ClientCommand cmd)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.ClientCommand);
            w.Write((byte)cmd.Type);
            w.Write(cmd.Multiplier);
            w.Write((int)cmd.Kind);
            WriteVesselId(w, cmd.VesselId); // OccupyStation payload (zero/ignored for warp)
            w.Write((int)cmd.Station);
            return ms.ToArray();
        }

        public static ClientCommand DecodeClientCommand(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms);
            r.ReadByte(); // type tag
            var type = (ClientCommandType)r.ReadByte();
            var multiplier = r.ReadDouble();
            var kind = (WarpKind)r.ReadInt32();
            var vesselId = ReadVesselId(r);
            var station = (Station)r.ReadInt32();
            return new ClientCommand(type, multiplier, kind, vesselId, station);
        }

        // ---- client → server: pilot input (M1-T08) ----

        public static byte[] EncodePilotInput(PilotInputMessage input)
        {
            using var ms = new MemoryStream();
            using var w = new BinaryWriter(ms);
            w.Write((byte)MessageType.PilotInput);
            WriteVesselId(w, input.VesselId);
            w.Write(input.ClientTick);
            w.Write(input.Throttle);
            w.Write(input.PitchRate);
            w.Write(input.YawRate);
            w.Write(input.RollRate);
            return ms.ToArray();
        }

        public static PilotInputMessage DecodePilotInput(byte[] payload)
        {
            using var ms = new MemoryStream(payload);
            using var r = new BinaryReader(ms);
            r.ReadByte(); // type tag
            var id = ReadVesselId(r);
            var tick = r.ReadInt64();
            var throttle = r.ReadDouble();
            var pitch = r.ReadDouble();
            var yaw = r.ReadDouble();
            var roll = r.ReadDouble();
            return new PilotInputMessage(id, tick, throttle, pitch, yaw, roll);
        }

        // ---- primitives ----

        private static void WriteVesselId(BinaryWriter w, VesselId id) => w.Write(id.Value.ToByteArray());
        private static VesselId ReadVesselId(BinaryReader r) => new(new Guid(r.ReadBytes(16)));

        private static void WriteVector(BinaryWriter w, Vector3d v)
        {
            w.Write(v.X); w.Write(v.Y); w.Write(v.Z);
        }

        private static Vector3d ReadVector(BinaryReader r) =>
            new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

        private static void WriteQuaternion(BinaryWriter w, Quaterniond q)
        {
            w.Write(q.X); w.Write(q.Y); w.Write(q.Z); w.Write(q.W);
        }

        private static Quaterniond ReadQuaternion(BinaryReader r) =>
            new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble());

        private static void WriteOrbit(BinaryWriter w, Orbit o)
        {
            w.Write((int)o.ParentBody);
            w.Write(o.SemiMajorAxis);
            w.Write(o.Eccentricity);
            w.Write(o.Inclination);
            w.Write(o.LongitudeOfAscendingNode);
            w.Write(o.ArgumentOfPeriapsis);
            w.Write(o.MeanAnomalyAtEpoch);
            w.Write(o.EpochGameTime);
        }

        private static Orbit ReadOrbit(BinaryReader r)
        {
            var parent = (CelestialBodyId)r.ReadInt32();
            var a = r.ReadDouble();
            var e = r.ReadDouble();
            var i = r.ReadDouble();
            var raan = r.ReadDouble();
            var argp = r.ReadDouble();
            var m0 = r.ReadDouble();
            var epoch = r.ReadDouble();
            return new Orbit(a, e, i, raan, argp, m0, epoch, parent);
        }
    }

    public enum MessageType : byte
    {
        Handshake = 1,
        Snapshot = 2,
        ClientCommand = 3,
        PilotInput = 4,
        // Design/VAB replication channel (M3-T04) — logically separate from the
        // vessel snapshot/interpolation path (Art. 7).
        JoinDesign = 5,
        LeaveDesign = 6,
        DesignSnapshot = 7,
        EditOpSubmit = 8,
        EditOpAck = 9,
        EditOpBroadcast = 10,
    }
}
