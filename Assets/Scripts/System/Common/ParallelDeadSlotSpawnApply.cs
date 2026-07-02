using Unity.Collections;
using Unity.Jobs.LowLevel.Unsafe;
using Unity.Mathematics;

namespace PlayGround.System.Common
{
    internal struct ParallelSpawnWorkerRange
    {
        public int ChunkStart;
        public int ChunkCount;
    }

    internal static class ParallelDeadSlotSpawnApply
    {
        public static int WorkerCountFor(int chunkCount)
        {
            if (chunkCount <= 0)
            {
                return 0;
            }

            int targetWorkers = JobsUtility.JobWorkerCount;
            if (targetWorkers <= 0)
            {
                targetWorkers = 1;
            }

            return math.clamp(targetWorkers, 1, chunkCount);
        }

        public static NativeArray<ParallelSpawnWorkerRange> BuildWorkerRanges(
            int chunkCount,
            int workerCount,
            Allocator allocator)
        {
            var ranges = new NativeArray<ParallelSpawnWorkerRange>(
                workerCount,
                allocator,
                NativeArrayOptions.UninitializedMemory);

            int baseCount = chunkCount / workerCount;
            int remainder = chunkCount % workerCount;
            int chunkStart = 0;

            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int chunkCountForWorker = baseCount + (workerIndex < remainder ? 1 : 0);
                ranges[workerIndex] = new ParallelSpawnWorkerRange
                {
                    ChunkStart = chunkStart,
                    ChunkCount = chunkCountForWorker
                };
                chunkStart += chunkCountForWorker;
            }

            return ranges;
        }

        // Drains a self-sizing command NativeStream (one lane per producer event) into
        // a flat array the reuse job can index randomly. Returns default when empty so
        // callers can treat "no commands" uniformly. The stream sizes itself on write,
        // so producers never pre-count commands to reserve list capacity.
        public static NativeArray<T> DrainToArray<T>(NativeStream stream, Allocator allocator)
            where T : unmanaged
        {
            int total = stream.Count();
            if (total == 0)
            {
                return default;
            }

            var array = new NativeArray<T>(total, allocator, NativeArrayOptions.UninitializedMemory);
            NativeStream.Reader reader = stream.AsReader();
            int idx = 0;
            int laneCount = reader.ForEachCount;
            for (int lane = 0; lane < laneCount; lane++)
            {
                int n = reader.BeginForEachIndex(lane);
                for (int k = 0; k < n; k++)
                {
                    array[idx++] = reader.Read<T>();
                }
                reader.EndForEachIndex();
            }

            return array;
        }

        public static NativeStream BuildCommandIndexStream(
            int commandCount,
            int workerCount,
            Allocator allocator)
        {
            var stream = new NativeStream(workerCount, allocator);
            NativeStream.Writer writer = stream.AsWriter();

            int baseCount = commandCount / workerCount;
            int remainder = commandCount % workerCount;
            int commandStart = 0;

            for (int workerIndex = 0; workerIndex < workerCount; workerIndex++)
            {
                int laneCount = baseCount + (workerIndex < remainder ? 1 : 0);
                writer.BeginForEachIndex(workerIndex);
                for (int i = 0; i < laneCount; i++)
                {
                    writer.Write(commandStart + i);
                }
                writer.EndForEachIndex();
                commandStart += laneCount;
            }

            return stream;
        }
    }
}
