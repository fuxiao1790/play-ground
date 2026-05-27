using System;
using System.Threading.Tasks;

public delegate void ProjectileRangeAction(int startIndex, int endIndex);

/// <summary>
/// Runs projectile systems over contiguous row ranges.
/// </summary>
public interface IProjectileWorkScheduler
{
	void ForEachRange(int count, int minChunkSize, ProjectileRangeAction action);
}

public sealed class SerialProjectileWorkScheduler : IProjectileWorkScheduler
{
	public void ForEachRange(int count, int minChunkSize, ProjectileRangeAction action)
	{
		if (count <= 0)
		{
			return;
		}

		action(0, count);
	}
}

public sealed class TaskProjectileWorkScheduler : IProjectileWorkScheduler
{
	public void ForEachRange(int count, int minChunkSize, ProjectileRangeAction action)
	{
		if (count <= 0)
		{
			return;
		}

		int chunkSize = ChunkSize(count, minChunkSize);
		int chunkCount = ChunkCount(count, minChunkSize);
		if (chunkCount <= 1)
		{
			action(0, count);
			return;
		}

		Parallel.For(0, chunkCount, chunkIndex =>
		{
			int startIndex = chunkIndex * chunkSize;
			int endIndex = Math.Min(count, startIndex + chunkSize);
			action(startIndex, endIndex);
		});
	}

	public static int ChunkSize(int count, int minChunkSize)
	{
		int workerCount = Math.Max(1, Environment.ProcessorCount);
		return Math.Max(Math.Max(1, minChunkSize), (count + workerCount - 1) / workerCount);
	}

	public static int ChunkCount(int count, int minChunkSize)
	{
		if (count <= 0)
		{
			return 0;
		}

		int chunkSize = ChunkSize(count, minChunkSize);
		return (count + chunkSize - 1) / chunkSize;
	}

	public static int WorkerCountFor(int chunkCount)
	{
		return Math.Min(Math.Max(0, chunkCount), Math.Max(1, Environment.ProcessorCount));
	}
}
