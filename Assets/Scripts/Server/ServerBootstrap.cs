using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    public sealed class ServerBootstrap : MonoBehaviour
    {
        public SimWorld World { get; private set; }
        public SimScheduler Scheduler { get; private set; }

        private void Awake()
        {
            World = new SimWorld();
            Scheduler = new SimScheduler(World);
            Debug.Log("[server] SimWorld constructed, authoritative=true");
        }

        private void Update()
        {
            Scheduler.Advance(Time.unscaledDeltaTime);
        }
    }
}