using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Protocol;
using UnityEngine;

namespace Multiplayer.Game
{
    public enum NetConnectionState
    {
        Disconnected,
        Connecting,
        Connected
    }

    public sealed class NetworkClient : MonoBehaviour
    {
        [SerializeField] string host = "127.0.0.1";
        [SerializeField] int port = NetConfig.Port;

        EventBasedNetListener listener;
        NetManager net;
        NetPeer server;
        NetConnectionState state = NetConnectionState.Disconnected;
        readonly NetDataWriter writer = new NetDataWriter();
        readonly WorldSnapshotMessage snapshot = new WorldSnapshotMessage();
        readonly GameEventBatchMessage eventBatch = new GameEventBatchMessage();
        bool dropEvents;

        public NetConnectionState State
        {
            get { return state; }
        }

        public string Endpoint
        {
            get { return host + ":" + port; }
        }

        public bool IsWelcomed { get; private set; }
        public int PlayerId { get; private set; }
        public Color OwnerColor { get; private set; }

        public event Action<NetConnectionState> StateChanged;
        public event Action Welcomed;
        public event Action<PlayerJoinedMessage> PlayerJoined;
        public event Action<int> PlayerLeft;
        public event Action<WorldSnapshotMessage> SnapshotReceived;
        public event Action<GameEventMessage> GameEventReceived;

        public void Send(MessageId id, INetSerializable body)
        {
            if (server == null || state != NetConnectionState.Connected)
                return;

            writer.Reset();
            writer.Put((byte)id);
            body.Serialize(writer);
            server.Send(writer, DeliveryMethod.ReliableOrdered);
        }

        void Start()
        {
            ApplyCommandLine(Environment.GetCommandLineArgs());

            listener = new EventBasedNetListener();
            listener.PeerConnectedEvent += OnPeerConnected;
            listener.PeerDisconnectedEvent += OnPeerDisconnected;
            listener.NetworkReceiveEvent += OnNetworkReceive;
            listener.NetworkErrorEvent += OnNetworkError;

            net = new NetManager(listener);
            if (!net.Start())
            {
                Debug.LogError("NetworkClient: failed to open a UDP socket.");
                net = null;
                return;
            }

            SetState(NetConnectionState.Connecting);
            server = net.Connect(host, port, NetConfig.ConnectionKey);
        }

        // Only "--" options are ours; Unity's own "-" switches are left alone.
        void ApplyCommandLine(string[] args)
        {
            List<string> ignored = new List<string>();
            for (int i = 1; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                    continue;

                // Test switch: discard every game event, to see what the replicated state alone shows.
                if (arg == "--drop-events")
                {
                    dropEvents = true;
                    continue;
                }

                bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal);
                if (arg == "--host" && hasValue)
                {
                    host = args[++i];
                    continue;
                }

                int parsedPort;
                if (arg == "--port" && hasValue && int.TryParse(args[i + 1], out parsedPort) && parsedPort > 0 && parsedPort <= 65535)
                {
                    port = parsedPort;
                    i++;
                    continue;
                }

                ignored.Add(hasValue ? arg + " " + args[++i] : arg);
            }

            if (ignored.Count > 0)
                Debug.Log("NetworkClient: ignored command line args: " + string.Join(", ", ignored));
        }

        void Update()
        {
            if (net != null)
                net.PollEvents();
        }

        void OnDestroy()
        {
            Shutdown();
        }

        void OnApplicationQuit()
        {
            Shutdown();
        }

        void Shutdown()
        {
            if (net == null)
                return;

            net.Stop();
            net = null;
            server = null;
            IsWelcomed = false;
            // Scene teardown: listeners may already be destroyed, so no StateChanged here.
            state = NetConnectionState.Disconnected;
        }

        void OnPeerConnected(NetPeer peer)
        {
            server = peer;
            SetState(NetConnectionState.Connected);
        }

        void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            Debug.Log("NetworkClient: disconnected, reason " + info.Reason);
            server = null;
            IsWelcomed = false;
            SetState(NetConnectionState.Disconnected);
        }

        void OnNetworkError(IPEndPoint endPoint, SocketError error)
        {
            Debug.Log("NetworkClient: socket error " + error);
        }

        void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            try
            {
                if (reader.AvailableBytes < 1)
                    return;

                MessageId id = (MessageId)reader.GetByte();
                switch (id)
                {
                    case MessageId.Welcome:
                        WelcomeMessage welcome = default;
                        welcome.Deserialize(reader);
                        HandleWelcome(welcome);
                        break;
                    case MessageId.PlayerJoined:
                        PlayerJoinedMessage joined = default;
                        joined.Deserialize(reader);
                        Raise(PlayerJoined, joined);
                        break;
                    case MessageId.PlayerLeft:
                        PlayerLeftMessage left = default;
                        left.Deserialize(reader);
                        Raise(PlayerLeft, left.PlayerId);
                        break;
                    case MessageId.WorldSnapshot:
                        snapshot.Deserialize(reader);
                        Raise(SnapshotReceived, snapshot);
                        break;
                    case MessageId.GameEvent:
                        eventBatch.Deserialize(reader);
                        if (eventBatch.Truncated)
                            Debug.Log("NetworkClient: unknown game event type, rest of the batch skipped");
                        for (int i = 0; i < eventBatch.Events.Count && !dropEvents; i++)
                            Raise(GameEventReceived, eventBatch.Events[i]);
                        break;
                    default:
                        Debug.Log("NetworkClient: unhandled message " + id);
                        break;
                }
            }
            finally
            {
                reader.Recycle();
            }
        }

        void HandleWelcome(WelcomeMessage welcome)
        {
            PlayerId = welcome.PlayerId;
            OwnerColor = new Color32(welcome.R, welcome.G, welcome.B, 255);
            IsWelcomed = true;

            Action handler = Welcomed;
            if (handler != null)
                handler();
        }

        void SetState(NetConnectionState next)
        {
            if (state == next)
                return;

            state = next;
            Raise(StateChanged, next);
        }

        static void Raise<T>(Action<T> handler, T arg)
        {
            if (handler != null)
                handler(arg);
        }
    }
}
