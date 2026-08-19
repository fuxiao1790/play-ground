using NUnit.Framework;
using PlayGround.System.Combat.Audio;

namespace PlayGround.Tests.EditMode
{
    public sealed class MinPriorityQueueEditModeTests
    {
        [Test]
        public void TryPeekAndDequeue_ReturnLowestPriorityWithoutRemovingOnPeek()
        {
            var queue = new MinPriorityQueue<string>();
            queue.Enqueue("late", 3f);
            queue.Enqueue("first", 1f);
            queue.Enqueue("middle", 2f);

            Assert.That(queue.TryPeek(out string peeked, out float peekPriority), Is.True);
            Assert.That(peeked, Is.EqualTo("first"));
            Assert.That(peekPriority, Is.EqualTo(1f));
            Assert.That(queue.Count, Is.EqualTo(3));

            Assert.That(queue.TryDequeue(out string first, out _), Is.True);
            Assert.That(queue.TryDequeue(out string middle, out _), Is.True);
            Assert.That(queue.TryDequeue(out string late, out _), Is.True);
            Assert.That(first, Is.EqualTo("first"));
            Assert.That(middle, Is.EqualTo("middle"));
            Assert.That(late, Is.EqualTo("late"));
        }

        [Test]
        public void EqualPriorities_DequeueInInsertionOrder()
        {
            var queue = new MinPriorityQueue<int>();
            queue.Enqueue(10, 1f);
            queue.Enqueue(20, 1f);
            queue.Enqueue(30, 1f);

            queue.TryDequeue(out int first, out _);
            queue.TryDequeue(out int second, out _);
            queue.TryDequeue(out int third, out _);

            Assert.That(new[] { first, second, third }, Is.EqualTo(new[] { 10, 20, 30 }));
        }

        [Test]
        public void Clear_EmptiesQueueAndAllowsReuse()
        {
            var queue = new MinPriorityQueue<int>();
            queue.Enqueue(10, 10f);
            queue.Clear();

            Assert.That(queue.TryPeek(out _, out _), Is.False);
            Assert.That(queue.Count, Is.Zero);

            queue.Enqueue(20, 2f);
            Assert.That(queue.TryDequeue(out int item, out float priority), Is.True);
            Assert.That(item, Is.EqualTo(20));
            Assert.That(priority, Is.EqualTo(2f));
        }
    }
}
