using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Tests.PlayMode
{
    // Disposable GPU-scheduling proof fixture for .agent/projectile-trail-gpu-strips task 001.
    // Not production code: proves whether a compute shader write to a GraphicsBuffer, dispatched
    // immediately before SendEvent, is visible to that same batch's VFX Initialize -- and that an
    // invalid resolved index never touches a live strip. The real key->slot allocator is task 005.
    //
    // The fixture graph's exposed properties mirror the real ProjectileTrail contract exactly
    // (index.md architecture decisions 3 and 5, task 002's planned VfxDataShapeTable entries):
    // ResolvedStripIndicesPropertyName, TrailPositionsPropertyName, TrailWidthsPropertyName
    // (all GraphicsBuffer, sampled only in Initialize Particle Strip by spawnIndex),
    // PointLifetimePropertyName (plain float, set once, not per batch), SpawnCount, OnSpawn.
    // ResolvedStripIndices feeds the context's stripIndex input; TrailPositions/TrailWidths are
    // copied into the persistent "position"/"width" particle attributes, same as production would.
    // This proof has no real trail geometry, so TrailPositions instead carries the batch tag (see
    // the compute shader) purely so results are distinguishable per dispatch.
    //
    // DebugStripSamplesPropertyName is the one property with **no production equivalent** -- Unity
    // exposes no public API to read a VFX graph's internal particle-attribute state back to C#, so
    // this buffer exists purely so the PlayMode tests can observe what Initialize actually did. A
    // Custom HLSL block, placed last in Initialize (after strip reservation), writes
    // DebugStripSamples[stripIndex] = uint2(stripIndex, (uint)position.x). It is never cleared by
    // the graph, so an untouched slot keeps whatever a previous batch (or ClearDebugSamples) wrote.
    public sealed class ProjectileTrailStripBridgeProbe : global::System.IDisposable
    {
        public const string ResolvedStripIndicesPropertyName = "ResolvedStripIndices";
        public const string TrailPositionsPropertyName = "TrailPositions";
        public const string TrailWidthsPropertyName = "TrailWidths";
        public const string PointLifetimePropertyName = "PointLifetime";
        public const string DebugStripSamplesPropertyName = "DebugStripSamples";
        public const string SpawnCountPropertyName = "SpawnCount";
        public const string SpawnEventName = "OnSpawn";

        public static readonly uint2 UntouchedSample = new(uint.MaxValue, uint.MaxValue);

        private readonly ComputeShader computeShader;
        private readonly int kernel;
        private readonly VisualEffect target;
        private readonly int stripCapacity;
        private readonly int maxRequestsPerBatch;

        private GraphicsBuffer requestedSlotsBuffer;
        private GraphicsBuffer batchTagsBuffer;
        private GraphicsBuffer resolvedStripIndicesBuffer;
        private GraphicsBuffer trailPositionsBuffer;
        private GraphicsBuffer trailWidthsBuffer;
        private GraphicsBuffer debugStripSamplesBuffer;

        public ProjectileTrailStripBridgeProbe(
            ComputeShader computeShader,
            VisualEffect target,
            int stripCapacity,
            int maxRequestsPerBatch,
            float pointLifetimeSeconds)
        {
            this.computeShader = computeShader;
            this.target = target;
            this.stripCapacity = stripCapacity;
            this.maxRequestsPerBatch = maxRequestsPerBatch;
            kernel = computeShader.FindKernel("ResolveIndices");

            requestedSlotsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, maxRequestsPerBatch, sizeof(uint));
            batchTagsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, maxRequestsPerBatch, sizeof(uint));
            resolvedStripIndicesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, maxRequestsPerBatch, sizeof(uint));
            trailPositionsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, maxRequestsPerBatch, sizeof(float) * 2);
            trailWidthsBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, maxRequestsPerBatch, sizeof(float));
            debugStripSamplesBuffer = new GraphicsBuffer(
                GraphicsBuffer.Target.Structured, stripCapacity, sizeof(uint) * 2);

            // Set once, like the real definition-driven PointLifetimeSeconds (index.md decision
            // 3), not per batch -- generous, so no point dies mid-test and confounds an assertion.
            target.SetFloat(PointLifetimePropertyName, pointLifetimeSeconds);

            ClearDebugSamples();
        }

        public void ClearDebugSamples()
        {
            var clear = new NativeArray<uint2>(stripCapacity, Allocator.Temp);
            for (int i = 0; i < stripCapacity; i++)
            {
                clear[i] = UntouchedSample;
            }

            debugStripSamplesBuffer.SetData(clear);
            clear.Dispose();
            target.SetGraphicsBuffer(DebugStripSamplesPropertyName, debugStripSamplesBuffer);
        }

        // requestedSlots[i] is the strip slot that request i asks to initialize into; a value
        // >= stripCapacity is deliberately out of range so the compute shader writes the invalid
        // sentinel for it (mirrors index.md architecture decision 5's uint.MaxValue contract).
        // batchTag has no production meaning -- it rides along in TrailPositions purely so the
        // debug read-back can tell which dispatch a surviving point's data came from.
        public void DispatchBatch(int[] requestedSlots, uint batchTag)
        {
            int count = requestedSlots.Length;
            if (count > maxRequestsPerBatch)
            {
                throw new global::System.ArgumentException(
                    $"Batch of {count} exceeds probe capacity {maxRequestsPerBatch}.");
            }

            var slots = new NativeArray<uint>(count, Allocator.Temp);
            var tags = new NativeArray<uint>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                slots[i] = (uint)requestedSlots[i];
                tags[i] = batchTag;
            }

            requestedSlotsBuffer.SetData(slots, 0, 0, count);
            batchTagsBuffer.SetData(tags, 0, 0, count);
            slots.Dispose();
            tags.Dispose();

            computeShader.SetBuffer(kernel, "_RequestedSlots", requestedSlotsBuffer);
            computeShader.SetBuffer(kernel, "_BatchTags", batchTagsBuffer);
            computeShader.SetBuffer(kernel, "_ResolvedStripIndices", resolvedStripIndicesBuffer);
            computeShader.SetBuffer(kernel, "_TrailPositions", trailPositionsBuffer);
            computeShader.SetBuffer(kernel, "_TrailWidths", trailWidthsBuffer);
            computeShader.SetInt("_StripCapacity", stripCapacity);
            computeShader.SetInt("_RequestCount", count);
            int groups = (count + 63) / 64;
            computeShader.Dispatch(kernel, Mathf.Max(1, groups), 1, 1);

            target.SetGraphicsBuffer(ResolvedStripIndicesPropertyName, resolvedStripIndicesBuffer);
            target.SetGraphicsBuffer(TrailPositionsPropertyName, trailPositionsBuffer);
            target.SetGraphicsBuffer(TrailWidthsPropertyName, trailWidthsBuffer);
            target.SetInt(SpawnCountPropertyName, count);
            target.SendEvent(SpawnEventName);
        }

        // GraphicsBuffer.GetData only has Array overloads (no NativeArray readback overload in
        // this Unity version), unlike its SetData; a managed array is the correct fit here.
        public uint2[] ReadDebugSamples()
        {
            var result = new uint2[stripCapacity];
            debugStripSamplesBuffer.GetData(result);
            return result;
        }

        public void Dispose()
        {
            requestedSlotsBuffer?.Release();
            requestedSlotsBuffer = null;
            batchTagsBuffer?.Release();
            batchTagsBuffer = null;
            resolvedStripIndicesBuffer?.Release();
            resolvedStripIndicesBuffer = null;
            trailPositionsBuffer?.Release();
            trailPositionsBuffer = null;
            trailWidthsBuffer?.Release();
            trailWidthsBuffer = null;
            debugStripSamplesBuffer?.Release();
            debugStripSamplesBuffer = null;
        }
    }
}
