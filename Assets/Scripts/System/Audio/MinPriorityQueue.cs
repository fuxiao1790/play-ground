using System.Collections.Generic;

namespace PlayGround.System.Combat.Audio
{
    // Reused binary min-heap. Equal priorities dequeue in insertion order.
    internal sealed class MinPriorityQueue<T>
    {
        private readonly List<Entry> heap = new();
        private long nextSequence;

        public int Count => heap.Count;

        public void Enqueue(T item, float priority)
        {
            var entry = new Entry(item, priority, nextSequence++);
            heap.Add(entry);
            SiftUp(heap.Count - 1);
        }

        public bool TryPeek(out T item, out float priority)
        {
            if (heap.Count == 0)
            {
                item = default;
                priority = default;
                return false;
            }

            Entry root = heap[0];
            item = root.Item;
            priority = root.Priority;
            return true;
        }

        public bool TryDequeue(out T item, out float priority)
        {
            if (!TryPeek(out item, out priority))
            {
                return false;
            }

            int lastIndex = heap.Count - 1;
            Entry last = heap[lastIndex];
            heap.RemoveAt(lastIndex);
            if (lastIndex > 0)
            {
                heap[0] = last;
                SiftDown(0);
            }

            return true;
        }

        public void Clear()
        {
            heap.Clear();
            nextSequence = 0;
        }

        private void SiftUp(int index)
        {
            while (index > 0)
            {
                int parent = (index - 1) / 2;
                if (!ComesBefore(heap[index], heap[parent]))
                {
                    return;
                }

                Entry swap = heap[parent];
                heap[parent] = heap[index];
                heap[index] = swap;
                index = parent;
            }
        }

        private void SiftDown(int index)
        {
            while (true)
            {
                int left = index * 2 + 1;
                if (left >= heap.Count)
                {
                    return;
                }

                int right = left + 1;
                int first = right < heap.Count && ComesBefore(heap[right], heap[left])
                    ? right
                    : left;
                if (!ComesBefore(heap[first], heap[index]))
                {
                    return;
                }

                Entry swap = heap[first];
                heap[first] = heap[index];
                heap[index] = swap;
                index = first;
            }
        }

        private static bool ComesBefore(in Entry left, in Entry right) =>
            left.Priority < right.Priority
            || (left.Priority == right.Priority && left.Sequence < right.Sequence);

        private readonly struct Entry
        {
            public Entry(T item, float priority, long sequence)
            {
                Item = item;
                Priority = priority;
                Sequence = sequence;
            }

            public T Item { get; }
            public float Priority { get; }
            public long Sequence { get; }
        }
    }
}
