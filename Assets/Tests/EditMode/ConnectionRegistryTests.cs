using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    public sealed class ConnectionRegistryTests
    {
        [Test]
        public void AddNew_RegistersSession_AndFiresConnectedEvent()
        {
            var reg = new ConnectionRegistry();
            PlayerSession? seen = null;
            reg.PlayerConnected += s => seen = s;

            var session = reg.AddNew();

            Assert.AreEqual(1, reg.ConnectedCount);
            Assert.IsTrue(reg.Contains(session.Id));
            Assert.AreSame(session, seen, "PlayerConnected event must carry the new session.");
        }

        [Test]
        public void Remove_FiresDisconnected_AndUpdatesCount()
        {
            var reg = new ConnectionRegistry();
            var session = reg.AddNew();
            PlayerSession? seen = null;
            reg.PlayerDisconnected += s => seen = s;

            var removed = reg.Remove(session.Id);

            Assert.IsTrue(removed);
            Assert.AreEqual(0, reg.ConnectedCount);
            Assert.AreSame(session, seen);
        }

        [Test]
        public void Remove_UnknownPlayer_ReturnsFalse_NoEvent()
        {
            var reg = new ConnectionRegistry();
            int fired = 0;
            reg.PlayerDisconnected += _ => fired++;

            Assert.IsFalse(reg.Remove(PlayerId.New()));
            Assert.AreEqual(0, fired);
        }

        [Test]
        public void PlayerIds_AreUnique_AcrossConnections()
        {
            var reg = new ConnectionRegistry();
            var a = reg.AddNew();
            var b = reg.AddNew();
            var c = reg.AddNew();
            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreNotEqual(b.Id, c.Id);
            Assert.AreEqual(3, reg.ConnectedCount);
        }
    }
}