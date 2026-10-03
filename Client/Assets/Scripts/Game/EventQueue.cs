using System.Collections.Generic;
using Multiplayer.Protocol;

namespace Multiplayer.Game
{
    // Holds events until the render clock reaches their tick, so they play alongside the drawn
    // state they belong to instead of on arrival, about two ticks ahead of it.
    public sealed class EventQueue
    {
        readonly List<GameEventMessage> pending = new List<GameEventMessage>();
        bool observed;
        uint firstObservedTick;
        GameEventMessage lastReleased;
        int releasedCount;
        int droppedCount;

        public int Count
        {
            get { return pending.Count; }
        }

        public int ReleasedCount
        {
            get { return releasedCount; }
        }

        public int DroppedCount
        {
            get { return droppedCount; }
        }

        public GameEventMessage LastReleased
        {
            get { return lastReleased; }
        }

        // The clock counts every tick older than the first snapshot as reached. Without this
        // floor, events from before this client was watching would all play at once on joining.
        public void ObserveSnapshot(uint tick)
        {
            if (observed)
                return;

            observed = true;
            firstObservedTick = tick;

            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Tick < tick)
                {
                    pending.RemoveAt(i);
                    droppedCount++;
                }
            }
        }

        public void Enqueue(GameEventMessage gameEvent)
        {
            if (observed && gameEvent.Tick < firstObservedTick)
            {
                droppedCount++;
                return;
            }

            int index = pending.Count;
            while (index > 0 && pending[index - 1].Tick > gameEvent.Tick)
                index--;

            pending.Insert(index, gameEvent);
        }

        public void Release(RenderClock clock, List<GameEventMessage> released)
        {
            released.Clear();

            int count = 0;
            while (count < pending.Count && clock.HasReached(pending[count].Tick))
            {
                released.Add(pending[count]);
                count++;
            }

            if (count == 0)
                return;

            lastReleased = released[count - 1];
            releasedCount += count;
            pending.RemoveRange(0, count);
        }

        public void Clear()
        {
            pending.Clear();
            observed = false;
            firstObservedTick = 0;
            lastReleased = default;
            releasedCount = 0;
            droppedCount = 0;
        }
    }
}
