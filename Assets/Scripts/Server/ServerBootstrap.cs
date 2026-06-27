using UnityEngine;
using KSPClone.SimCore;

namespace KSPClone.Server
{
    public sealed class ServerBootstrap : MonoBehaviour
    {
        public SimWorld World { get; private set; }

        private void Awake()
        {
            World = new SimWorld();
            Debug.Log("[server] SimWorld constructed, authoritative=true");
        }
    }
}