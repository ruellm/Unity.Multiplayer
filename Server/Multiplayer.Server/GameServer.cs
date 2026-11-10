using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
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
        const float WorldHalfExtent = Footprint.WorldHalfExtent;
        const int MaxEntitiesPerPlayer = 50;
        const float FixedDt = 1f / NetConfig.TickRate;
        const int SpinMarginMs = 2;
        const int MaxLagTicks = 2;
        const int StatsPeriodSeconds = 10;

        // Interval statistics between two reports.
        sealed class TickStats
        {
            int count;
            double min = double.MaxValue;
            double max;
            double sum;
            double sumSquares;

            public void Add(double ms)
            {
                count++;
                if (ms < min) min = ms;
                if (ms > max) max = ms;
                sum += ms;
                sumSquares += ms * ms;
            }

            public string Report()
            {
                if (count == 0)
                    return "Tick stats: no ticks";

                double mean = sum / count;
                double variance = Math.Max(0.0, sumSquares / count - mean * mean);
                return string.Format(CultureInfo.InvariantCulture,
                    "Tick stats over {0} ticks: mean {1:F2} ms, min {2:F2} ms, max {3:F2} ms, stddev {4:F2} ms",
                    count, mean, min, max, Math.Sqrt(variance));
            }

            public void Reset()
            {
                count = 0;
                min = double.MaxValue;
                max = 0.0;
                sum = 0.0;
                sumSquares = 0.0;
            }
        }

        readonly EventBasedNetListener listener = new EventBasedNetListener();
        readonly NetManager net;
        readonly PlayerRegistry players = new PlayerRegistry();
        readonly World world = new World();
        readonly NetDataWriter writer = new NetDataWriter();
        readonly WorldSnapshotMessage snapshot = new WorldSnapshotMessage();
        readonly GameEventBatchMessage eventBatch = new GameEventBatchMessage();
        readonly TickStats tickStats;
        uint tick;

        public GameServer() : this(false)
        {
        }

        public GameServer(bool reportTickStats)
        {
            tickStats = reportTickStats ? new TickStats() : null;
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

        // Every tick has an absolute deadline, origin + index * step. The loop sleeps until shortly
        // before it and spins the rest. Sleeps overshoot by the OS timer granularity, so the worst
        // overshoot seen so far is taken off the sleep as well and covered by spinning.
        public void Run(ManualResetEventSlim stopRequested)
        {
            long frequency = Stopwatch.Frequency;
            long step = frequency / NetConfig.TickRate;
            long spinMargin = frequency * SpinMarginMs / 1000;
            long maxLag = step * MaxLagTicks;
            long statsPeriod = frequency * StatsPeriodSeconds;

            Stopwatch clock = Stopwatch.StartNew();
            long origin = clock.ElapsedTicks;
            long index = 0;
            long sleepSlop = 0;
            long lastTickAt = -1;
            long nextStatsAt = origin + statsPeriod;

            while (!stopRequested.IsSet)
            {
                long deadline = origin + index * step;
                long remaining = deadline - clock.ElapsedTicks;

                if (remaining > spinMargin)
                {
                    int sleepMs = (int)((remaining - spinMargin - sleepSlop) * 1000 / frequency);
                    if (sleepMs > 0)
                    {
                        long before = clock.ElapsedTicks;
                        stopRequested.Wait(sleepMs);
                        long overshoot = clock.ElapsedTicks - before - sleepMs * frequency / 1000;
                        if (overshoot > sleepSlop)
                            sleepSlop = Math.Min(overshoot, step / 2);
                        continue;
                    }
                }

                while (clock.ElapsedTicks < deadline)
                    Thread.SpinWait(50);

                long now = clock.ElapsedTicks;
                if (now - deadline > maxLag)
                {
                    Log("Fell " + (now - deadline) * 1000 / frequency + " ms behind the tick schedule, resyncing instead of catching up");
                    origin = now - index * step;
                }

                Tick();

                if (tickStats != null)
                {
                    if (lastTickAt >= 0)
                        tickStats.Add((now - lastTickAt) * 1000.0 / frequency);
                    lastTickAt = now;

                    if (now >= nextStatsAt)
                    {
                        Log(tickStats.Report());
                        tickStats.Reset();
                        nextStatsAt += statsPeriod;
                    }
                }

                index++;
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
            world.Integrate(tick, FixedDt);
            BroadcastEvents();
            BroadcastSnapshot();
        }

        void BroadcastEvents()
        {
            List<GameEventMessage> events = world.Events;
            if (events.Count == 0)
                return;

            eventBatch.Events.Clear();
            for (int i = 0; i < events.Count; i++)
            {
                GameEventMessage gameEvent = events[i];
                eventBatch.Events.Add(gameEvent);

                Entity dead;
                if (gameEvent.EventType == (byte)GameEventType.EntityDied && world.TryGet(gameEvent.EntityDied.EntityId, out dead))
                    Log(dead.UnitType + " " + dead.EntityId + " of player " + dead.OwnerId + " died, cause " + (DeathCause)gameEvent.EntityDied.Cause);
            }
            events.Clear();

            BeginMessage(MessageId.GameEvent, eventBatch);
            SendToRegisteredExcept(null, DeliveryMethod.ReliableOrdered);
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
                state.ActionState = (byte)entity.ActionState;
                state.TargetEntityId = entity.TargetEntityId;
                state.Position = entity.Position;
                state.Yaw = entity.Yaw;
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
                    case MessageId.AttackRequest:
                        AttackRequestMessage attack = default;
                        attack.Deserialize(reader);
                        HandleAttackRequest(peer, attack);
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

            UnitType unitType = (UnitType)request.UnitType;
            Vec2 pos = request.Position;

            PlacementError error = world.CheckPlacement(info.PlayerId, unitType, pos);
            if (error != PlacementError.None)
            {
                Log("Place structure rejected: player " + info.PlayerId + " " + unitType + " at " + Format(pos) + ", " + Describe(error));
                return;
            }

            int owned = world.CountByOwner(info.PlayerId);
            if (owned >= MaxEntitiesPerPlayer)
            {
                Log("Place structure rejected: player " + info.PlayerId + " already owns " + owned + " entities");
                return;
            }

            Entity entity = world.Spawn(info.PlayerId, unitType, pos, request.Yaw);
            Log("Placed " + unitType + " " + entity.EntityId + " for player " + info.PlayerId + " at " + Format(pos) + " yaw " + YawByte.ToDegrees(request.Yaw).ToString("0"));
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

            if (structure.ActionState == ActionState.Dying)
            {
                Log("Build rejected: structure " + request.StructureId + " is dying");
                return;
            }

            if (structure.UnitType != UnitType.HQ)
            {
                Log("Build rejected: entity " + request.StructureId + " is a " + structure.UnitType + ", not an HQ");
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
            if (!world.TryFindSpawnPoint(structure, unitType, WorldHalfExtent, out pos))
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

            if (entity.ActionState == ActionState.Dying)
            {
                Log("Move rejected: entity " + request.EntityId + " is dying");
                return;
            }

            if (entity.UnitType.IsStructure() || UnitDefs.Get(entity.UnitType).Speed <= 0f)
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

        void HandleAttackRequest(NetPeer peer, AttackRequestMessage request)
        {
            PlayerInfo info;
            if (!players.TryGet(peer.Id, out info))
            {
                Log("Attack rejected: peer " + peer.Id + " is not a registered player");
                return;
            }

            Entity attacker;
            if (!world.TryGet(request.AttackerId, out attacker))
            {
                Log("Attack rejected: attacker " + request.AttackerId + " does not exist");
                return;
            }

            if (info.PlayerId != attacker.OwnerId)
            {
                Log("Attack rejected: player " + info.PlayerId + " does not own attacker " + request.AttackerId + " (owner " + attacker.OwnerId + ")");
                return;
            }

            if (attacker.ActionState == ActionState.Dying)
            {
                Log("Attack rejected: attacker " + request.AttackerId + " is dying");
                return;
            }

            if (request.AttackerId == request.TargetEntityId)
            {
                Log("Attack rejected: attacker and target are the same entity " + request.AttackerId);
                return;
            }

            if (attacker.UnitType.IsStructure())
            {
                Log("Attack rejected: entity " + request.AttackerId + " is a " + attacker.UnitType + " and takes no orders");
                return;
            }

            if (UnitDefs.Get(attacker.UnitType).Range <= 0f)
            {
                Log("Attack rejected: entity " + request.AttackerId + " is a " + attacker.UnitType + " and cannot attack");
                return;
            }

            Entity target;
            if (!world.TryGet(request.TargetEntityId, out target))
            {
                Log("Attack rejected: target " + request.TargetEntityId + " does not exist");
                return;
            }

            if (target.OwnerId == info.PlayerId)
            {
                Log("Attack rejected: player " + info.PlayerId + " owns target " + request.TargetEntityId);
                return;
            }

            if (target.ActionState == ActionState.Dying)
            {
                Log("Attack rejected: target " + request.TargetEntityId + " is dying");
                return;
            }

            world.SetAttackTarget(attacker.EntityId, target.EntityId);
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

        static string Describe(PlacementError error)
        {
            switch (error)
            {
                case PlacementError.NotAStructure:
                    return "not a placeable structure type";
                case PlacementError.AlreadyHasHQ:
                    return "already has an HQ";
                case PlacementError.NeedsHQ:
                    return "needs an HQ first";
                case PlacementError.OutOfBounds:
                    return "position out of bounds";
                case PlacementError.Overlaps:
                    return "overlaps an existing entity";
                default:
                    return error.ToString();
            }
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
