using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using LiteNetLib;
using LiteNetLib.Utils;
using Multiplayer.Protocol;

namespace Multiplayer.Server
{
    public sealed class GameServer
    {
        const float WorldHalfExtent = 50f;
        const int MaxEntitiesPerPlayer = 50;
        const float FixedDt = 1f / NetConfig.TickRate;

        readonly EventBasedNetListener listener = new EventBasedNetListener();
        readonly NetManager net;
        readonly PlayerRegistry players = new PlayerRegistry();
        readonly World world = new World();
        readonly NetDataWriter writer = new NetDataWriter();
        readonly WorldSnapshotMessage snapshot = new WorldSnapshotMessage();
        uint tick;

        public GameServer()
        {
            net = new NetManager(listener);
            listener.ConnectionRequestEvent += OnConnectionRequest;
            listener.PeerConnectedEvent += OnPeerConnected;
            listener.PeerDisconnectedEvent += OnPeerDisconnected;
            listener.NetworkReceiveEvent += OnNetworkReceive;
            listener.NetworkErrorEvent += OnNetworkError;
        }

        public bool Start()
        {
            if (!net.Start(NetConfig.Port))
            {
                Log("Failed to bind UDP port " + NetConfig.Port);
                return false;
            }

            Log("Listening on UDP port " + NetConfig.Port + ", tick rate " + NetConfig.TickRate + ". Press Ctrl+C to stop.");
            return true;
        }

        public void Run(ManualResetEventSlim stopRequested)
        {
            long ticksPerStep = Stopwatch.Frequency / NetConfig.TickRate;
            Stopwatch clock = Stopwatch.StartNew();
            long nextTick = clock.ElapsedTicks;

            while (!stopRequested.IsSet)
            {
                long now = clock.ElapsedTicks;
                if (now < nextTick)
                {
                    int waitMs = (int)((nextTick - now) * 1000 / Stopwatch.Frequency);
                    if (waitMs > 1)
                        stopRequested.Wait(waitMs - 1);
                    continue;
                }

                Tick();
                nextTick += ticksPerStep;

                // Fell far behind (debugger pause, machine sleep): resync instead of bursting ticks.
                if (now - nextTick > ticksPerStep * NetConfig.TickRate)
                    nextTick = now;
            }
        }

        public void Stop()
        {
            net.Stop();
            Log("Stopped.");
        }

        void Tick()
        {
            tick++;
            net.PollEvents();
            world.Integrate(FixedDt);
            BroadcastSnapshot();
        }

        void BroadcastSnapshot()
        {
            if (players.Count == 0)
                return;

            snapshot.Tick = tick;
            snapshot.Entities.Clear();
            foreach (Entity entity in world.Entities)
            {
                EntityState state;
                state.EntityId = entity.EntityId;
                state.OwnerId = entity.OwnerId;
                state.UnitType = (byte)entity.UnitType;
                state.Health = entity.Health;
                state.Position = entity.Position;
                snapshot.Entities.Add(state);
            }

            BeginMessage(MessageId.WorldSnapshot, snapshot);
            foreach (NetPeer peer in net)
            {
                PlayerInfo info;
                if (!players.TryGet(peer.Id, out info))
                    continue;

                // Sequenced cannot fragment and throws past one MTU. Oversized snapshots go on a
                // fragmenting channel instead; the client drops stale ticks either way.
                bool fits = writer.Length <= peer.GetMaxSinglePacketSize(DeliveryMethod.Sequenced);
                peer.Send(writer, fits ? DeliveryMethod.Sequenced : DeliveryMethod.ReliableUnordered);
            }
        }

        void OnConnectionRequest(ConnectionRequest request)
        {
            if (request.AcceptIfKey(NetConfig.ConnectionKey) == null)
                Log("Rejected connection from " + request.RemoteEndPoint + " (bad key)");
        }

        void OnPeerConnected(NetPeer peer)
        {
            PlayerInfo info = players.Add(peer.Id);

            WelcomeMessage welcome;
            welcome.PlayerId = info.PlayerId;
            welcome.R = info.Color.R;
            welcome.G = info.Color.G;
            welcome.B = info.Color.B;
            BeginMessage(MessageId.Welcome, welcome);
            peer.Send(writer, DeliveryMethod.ReliableOrdered);

            foreach (PlayerInfo other in players.Players)
            {
                BeginMessage(MessageId.PlayerJoined, ToJoined(other));
                peer.Send(writer, DeliveryMethod.ReliableOrdered);
            }

            BeginMessage(MessageId.PlayerJoined, ToJoined(info));
            SendToRegisteredExcept(peer, DeliveryMethod.ReliableOrdered);

            Log("Player " + info.PlayerId + " connected, colour " + info.Color + ", from " + peer + ", players online " + players.Count);
        }

        void OnPeerDisconnected(NetPeer peer, DisconnectInfo disconnectInfo)
        {
            PlayerInfo info;
            if (!players.Remove(peer.Id, out info))
            {
                Log("Unknown peer " + peer + " disconnected, reason " + disconnectInfo.Reason);
                return;
            }

            int removed = world.RemoveByOwner(info.PlayerId);

            PlayerLeftMessage left;
            left.PlayerId = info.PlayerId;
            BeginMessage(MessageId.PlayerLeft, left);
            SendToRegisteredExcept(null, DeliveryMethod.ReliableOrdered);

            Log("Player " + info.PlayerId + " disconnected, reason " + disconnectInfo.Reason + ", removed " + removed + " entities, players online " + players.Count);
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
                    case MessageId.PlaceStructureRequest:
                        PlaceStructureRequestMessage place = default;
                        place.Deserialize(reader);
                        HandlePlaceStructureRequest(peer, place);
                        break;
                    case MessageId.BuildUnitRequest:
                        BuildUnitRequestMessage build = default;
                        build.Deserialize(reader);
                        HandleBuildUnitRequest(peer, build);
                        break;
                    case MessageId.MoveRequest:
                        MoveRequestMessage move = default;
                        move.Deserialize(reader);
                        HandleMoveRequest(peer, move);
                        break;
                    default:
                        Log("Unhandled message " + id + " from peer " + peer.Id);
                        break;
                }
            }
            catch (Exception e)
            {
                Log("Malformed packet from peer " + peer.Id + ": " + e.Message);
            }
            finally
            {
                reader.Recycle();
            }
        }

        void HandlePlaceStructureRequest(NetPeer peer, PlaceStructureRequestMessage request)
        {
            PlayerInfo info;
            if (!players.TryGet(peer.Id, out info))
            {
                Log("Place structure rejected: peer " + peer.Id + " is not a registered player");
                return;
            }

            if (world.HasStructure(info.PlayerId))
            {
                Log("Place structure rejected: player " + info.PlayerId + " already has a structure");
                return;
            }

            Vec2 pos = request.Position;
            if (float.IsNaN(pos.X) || float.IsNaN(pos.Z)
                || Math.Abs(pos.X) > WorldHalfExtent || Math.Abs(pos.Z) > WorldHalfExtent)
            {
                Log("Place structure rejected: player " + info.PlayerId + " position " + Format(pos) + " out of bounds");
                return;
            }

            Entity entity = world.Spawn(info.PlayerId, UnitType.Structure, pos);
            Log("Placed structure " + entity.EntityId + " for player " + info.PlayerId + " at " + Format(pos));
        }

        void HandleBuildUnitRequest(NetPeer peer, BuildUnitRequestMessage request)
        {
            PlayerInfo info;
            if (!players.TryGet(peer.Id, out info))
            {
                Log("Build rejected: peer " + peer.Id + " is not a registered player");
                return;
            }

            Entity structure;
            if (!world.TryGet(request.StructureId, out structure))
            {
                Log("Build rejected: structure " + request.StructureId + " does not exist");
                return;
            }

            if (info.PlayerId != structure.OwnerId)
            {
                Log("Build rejected: player " + info.PlayerId + " does not own structure " + request.StructureId + " (owner " + structure.OwnerId + ")");
                return;
            }

            if (structure.UnitType != UnitType.Structure)
            {
                Log("Build rejected: entity " + request.StructureId + " is a " + structure.UnitType + ", not a structure");
                return;
            }

            UnitType unitType = (UnitType)request.UnitType;
            if (unitType != UnitType.Soldier && unitType != UnitType.Tank)
            {
                Log("Build rejected: player " + info.PlayerId + " requested unbuildable unit type " + unitType);
                return;
            }

            int owned = world.CountByOwner(info.PlayerId);
            if (owned >= MaxEntitiesPerPlayer)
            {
                Log("Build rejected: player " + info.PlayerId + " already owns " + owned + " entities");
                return;
            }

            Vec2 pos;
            if (!world.TryFindSpawnPoint(structure, WorldHalfExtent, out pos))
            {
                Log("Build rejected: no free spawn point around structure " + structure.EntityId);
                return;
            }

            Entity entity = world.Spawn(info.PlayerId, unitType, pos);
            Log("Built " + unitType + " " + entity.EntityId + " for player " + info.PlayerId + " at " + Format(pos));
        }

        void HandleMoveRequest(NetPeer peer, MoveRequestMessage request)
        {
            Entity entity;
            if (!world.TryGet(request.EntityId, out entity))
            {
                Log("Move rejected: entity " + request.EntityId + " does not exist");
                return;
            }

            PlayerInfo info;
            if (!players.TryGet(peer.Id, out info))
            {
                Log("Move rejected: peer " + peer.Id + " is not a registered player");
                return;
            }

            if (info.PlayerId != entity.OwnerId)
            {
                Log("Move rejected: player " + info.PlayerId + " does not own entity " + request.EntityId + " (owner " + entity.OwnerId + ")");
                return;
            }

            if (UnitDefs.Get(entity.UnitType).Speed <= 0f)
            {
                Log("Move rejected: entity " + request.EntityId + " is a " + entity.UnitType + " and cannot move");
                return;
            }

            Vec2 target = request.Target;
            if (Math.Abs(target.X) > WorldHalfExtent || Math.Abs(target.Z) > WorldHalfExtent)
            {
                Log("Move rejected: player " + info.PlayerId + " target " + Format(target) + " out of bounds");
                return;
            }

            if (float.IsNaN(target.X) || float.IsNaN(target.Z))
            {
                Log("Move rejected: player " + info.PlayerId + " target is NaN");
                return;
            }

            world.SetTarget(entity.EntityId, target);
        }

        void BeginMessage(MessageId id, INetSerializable body)
        {
            writer.Reset();
            writer.Put((byte)id);
            body.Serialize(writer);
        }

        void SendToRegisteredExcept(NetPeer excluded, DeliveryMethod method)
        {
            foreach (NetPeer peer in net)
            {
                PlayerInfo info;
                if (peer != excluded && players.TryGet(peer.Id, out info))
                    peer.Send(writer, method);
            }
        }

        void OnNetworkError(IPEndPoint endPoint, SocketError socketError)
        {
            Log("Socket error " + socketError + " from " + endPoint);
        }

        static PlayerJoinedMessage ToJoined(PlayerInfo info)
        {
            PlayerJoinedMessage joined;
            joined.PlayerId = info.PlayerId;
            joined.R = info.Color.R;
            joined.G = info.Color.G;
            joined.B = info.Color.B;
            return joined;
        }

        static string Format(Vec2 v)
        {
            return "(" + v.X.ToString("0.00") + ", " + v.Z.ToString("0.00") + ")";
        }

        static void Log(string message)
        {
            Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + message);
        }
    }
}
