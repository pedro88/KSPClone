using UnityEngine;
using KSPClone.Net;

namespace KSPClone.Client
{
    /// <summary>
    /// Unity host for an observing client. Opens a LiteNetLib UDP connection to
    /// the server, receives the world handshake + snapshot stream into a
    /// <see cref="ClientNetPeer"/>, and pumps it each frame. Receive/observe only
    /// — the server is the sole authority (NET-1). Can run in the same Play
    /// session as the server (talks over 127.0.0.1) or as its own build/process.
    /// </summary>
    public sealed class ClientBootstrap : MonoBehaviour
    {
        [SerializeField] private string _host = "127.0.0.1";
        [SerializeField] private int _port = 9050;

        public ClientNetPeer Peer { get; private set; }

        private LiteNetLibClientTransport _transport;

        // Start (not Awake): if the server lives in the same scene, its Awake
        // has already started the listener by the time we connect here.
        private void Start()
        {
            _transport = new LiteNetLibClientTransport();
            Peer = new ClientNetPeer(_transport);
            _transport.Connect(_host, _port);
            Debug.Log($"[client] connecting to {_host}:{_port}");
        }

        private void Update()
        {
            Peer?.Poll();
        }

        private void OnDestroy()
        {
            _transport?.Dispose();
        }
    }
}
