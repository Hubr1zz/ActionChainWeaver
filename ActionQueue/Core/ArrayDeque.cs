using System;

namespace CardGame.ActionQueue
{
    /// <summary>
    /// 基于环形数组的双端队列。扩容之外，AddFirst/AddLast/RemoveFirst 不产生托管分配。
    /// </summary>
    internal sealed class ArrayDeque<T>
    {
        private T[] _items;
        private int _head;
        private int _count;

        public ArrayDeque(int initialCapacity = 8)
        {
            if (initialCapacity < 1)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _items = new T[initialCapacity];
        }

        public int Count => _count;

        public T this[int index]
        {
            get
            {
                if ((uint)index >= (uint)_count)
                    throw new ArgumentOutOfRangeException(nameof(index));

                return _items[PhysicalIndex(index)];
            }
        }

        public void AddFirst(T item)
        {
            EnsureCapacity(_count + 1);
            _head = _head == 0 ? _items.Length - 1 : _head - 1;
            _items[_head] = item;
            _count++;
        }

        public void AddLast(T item)
        {
            EnsureCapacity(_count + 1);
            _items[PhysicalIndex(_count)] = item;
            _count++;
        }

        public T RemoveFirst()
        {
            if (_count == 0)
                throw new InvalidOperationException("The deque is empty.");

            T item = _items[_head];
            _items[_head] = default;
            _count--;
            _head = _count == 0
                ? 0
                : (_head + 1 == _items.Length ? 0 : _head + 1);
            return item;
        }

        public void Clear()
        {
            if (_count == 0)
                return;

            if (_head + _count <= _items.Length)
            {
                Array.Clear(_items, _head, _count);
            }
            else
            {
                int firstPart = _items.Length - _head;
                Array.Clear(_items, _head, firstPart);
                Array.Clear(_items, 0, _count - firstPart);
            }

            _head = 0;
            _count = 0;
        }

        private int PhysicalIndex(int logicalIndex)
        {
            int index = _head + logicalIndex;
            return index >= _items.Length ? index - _items.Length : index;
        }

        private void EnsureCapacity(int required)
        {
            if (required <= _items.Length)
                return;

            int nextCapacity = Math.Max(required, _items.Length * 2);
            var next = new T[nextCapacity];
            for (int i = 0; i < _count; i++)
                next[i] = _items[PhysicalIndex(i)];

            _items = next;
            _head = 0;
        }
    }
}
