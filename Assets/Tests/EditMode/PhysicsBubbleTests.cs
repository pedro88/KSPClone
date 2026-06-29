using NUnit.Framework;
using KSPClone.SimCore;

namespace KSPClone.SimCore.Tests
{
    [TestFixture]
    public sealed class PhysicsBubbleTests
    {
        [Test]
        public void NewBubble_StartsEmpty_WithStableId()
        {
            var origin = new Vector3d(1e9, 0, 0);
            var b = new PhysicsBubble(BubbleId.New(), origin);

            Assert.AreEqual(0, b.MemberCount);
            Assert.AreEqual(BubbleLifecycle.Empty, b.Lifecycle);
            Assert.AreEqual(origin, b.GlobalOrigin);
            Assert.IsFalse(b.Members.GetEnumerator().MoveNext());
        }

        [Test]
        public void Add_TransitionsBubbleToLive()
        {
            var b = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var v = VesselId.New();

            b.Add(v);

            Assert.AreEqual(1, b.MemberCount);
            Assert.IsTrue(b.Contains(v));
            Assert.AreEqual(BubbleLifecycle.Live, b.Lifecycle);
        }

        [Test]
        public void Remove_LastMember_TransitionsBubbleToEmpty()
        {
            var b = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            var v = VesselId.New();
            b.Add(v);

            Assert.IsTrue(b.Remove(v));

            Assert.AreEqual(0, b.MemberCount);
            Assert.AreEqual(BubbleLifecycle.Empty, b.Lifecycle);
        }

        [Test]
        public void Remove_NonMember_ReturnsFalse()
        {
            var b = new PhysicsBubble(BubbleId.New(), Vector3d.Zero);
            Assert.IsFalse(b.Remove(VesselId.New()));
        }

        [Test]
        public void Rebase_TranslatesGlobalOrigin_PreservesMembership()
        {
            var origin = new Vector3d(100, 200, 300);
            var b = new PhysicsBubble(BubbleId.New(), origin);
            var v = VesselId.New();
            b.Add(v);

            b.Rebase(new Vector3d(50, -20, 10));

            Assert.AreEqual(new Vector3d(150, 180, 310), b.GlobalOrigin);
            Assert.IsTrue(b.Contains(v));
            Assert.AreEqual(BubbleLifecycle.Live, b.Lifecycle);
        }
    }

    [TestFixture]
    public sealed class BubbleRegistryTests
    {
        [Test]
        public void Create_AddsBubble()
        {
            var registry = new BubbleRegistry();
            var b = registry.Create(Vector3d.Zero);

            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(b.Id, out var fetched));
            Assert.AreSame(b, fetched);
        }

        [Test]
        public void Create_ThreeBubbles_AllHaveDistinctStableIds()
        {
            var registry = new BubbleRegistry();
            var a = registry.Create(Vector3d.Zero);
            var b = registry.Create(new Vector3d(100, 0, 0));
            var c = registry.Create(new Vector3d(0, 100, 0));

            Assert.AreNotEqual(a.Id, b.Id);
            Assert.AreNotEqual(b.Id, c.Id);
            Assert.AreNotEqual(a.Id, c.Id);
            Assert.AreEqual(3, registry.Count);
        }

        [Test]
        public void Destroy_EmptyBubble_RemovesIt()
        {
            var registry = new BubbleRegistry();
            var b = registry.Create(Vector3d.Zero);

            Assert.IsTrue(registry.Destroy(b.Id));
            Assert.AreEqual(0, registry.Count);
            Assert.IsFalse(registry.TryGet(b.Id, out _));
        }

        [Test]
        public void Destroy_BubbleWithMembers_Throws()
        {
            var registry = new BubbleRegistry();
            var b = registry.Create(Vector3d.Zero);
            b.Add(VesselId.New());

            Assert.Throws<System.InvalidOperationException>(() => registry.Destroy(b.Id));
            Assert.AreEqual(1, registry.Count, "Bubble must survive a refused destroy.");
        }

        [Test]
        public void Destroy_UnknownId_ReturnsFalse()
        {
            var registry = new BubbleRegistry();
            Assert.IsFalse(registry.Destroy(BubbleId.New()));
        }

        [Test]
        public void CollectEmpty_RemovesAllEmptyBubbles_KeepsLiveOnes()
        {
            var registry = new BubbleRegistry();
            var empty1 = registry.Create(Vector3d.Zero);
            var live = registry.Create(Vector3d.Zero);
            live.Add(VesselId.New());
            var empty2 = registry.Create(new Vector3d(100, 0, 0));

            var removed = registry.CollectEmpty();

            Assert.AreEqual(2, removed);
            Assert.AreEqual(1, registry.Count);
            Assert.IsTrue(registry.TryGet(live.Id, out _));
            Assert.IsFalse(registry.TryGet(empty1.Id, out _));
            Assert.IsFalse(registry.TryGet(empty2.Id, out _));
        }

        [Test]
        public void TryGet_UnknownId_ReturnsFalse()
        {
            var registry = new BubbleRegistry();
            Assert.IsFalse(registry.TryGet(BubbleId.New(), out _));
        }

        [Test]
        public void SimCore_DoesNotReferenceUnityEngine()
        {
            using var _ = new NoUnityEngineAssertion();
        }
    }
}