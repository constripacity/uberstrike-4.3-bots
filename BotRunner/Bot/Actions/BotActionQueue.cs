using System;
using System.Collections.Generic;

namespace BotRunner.Bot.Actions
{
    public class BotActionQueue
    {
        private readonly Queue<BotAction> _queue = new();
        private readonly int _maxSize;

        public BotActionQueue(int maxSize = 64)
        {
            _maxSize = Math.Max(8, maxSize);
        }

        public int Count => _queue.Count;

        public void Enqueue(BotAction action)
        {
            if (_queue.Count >= _maxSize)
            {
                _queue.Dequeue();
            }

            _queue.Enqueue(action);
        }

        public IReadOnlyList<BotAction> Drain()
        {
            var list = new List<BotAction>(_queue.Count);
            while (_queue.Count > 0)
            {
                list.Add(_queue.Dequeue());
            }

            return list;
        }
    }
}
