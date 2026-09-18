using System.Collections;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.VFX;

namespace PlayGround.Tests.PlayMode
{
    // Task 001 of .agent/projectile-trail-gpu-strips: proves a compute-resolved strip index,
    // written immediately before SendEvent, reaches that same batch's VFX Initialize, that an
    // invalid resolved index never touches a live strip, and that a later batch's Initialize
    // never rewrites an older batch's still-alive points. Requires the hand-built fixture graph
    // at Assets/Tests/TestAssets/ProjectileTrailStripProbe.vfx (see
    // 001-verify-vfx-strip-bridge.md's editor-plan); ProjectileTrailStripBridgeProbeFixture
    // throws with a clear message if that asset is missing instead of skipping silently.
    public sealed class ProjectileTrailStripBridgeTests
    {
        private const int StripCapacity = 4;
        private const int MaxRequestsPerBatch = 8;

        // Generous relative to how long any of these tests run, so no point dies mid-test and
        // confounds an assertion; matches the real PointLifetimeSeconds contract (index.md
        // decision 3) being a graph-wide value set once, not something these tests tune per case.
        private const float PointLifetimeSeconds = 30f;

        // Starting guess for how many frames after SendEvent the compute-written buffers become
        // visible to this batch's Initialize. Task 001's acceptance requires this be established
        // empirically on target graphics APIs; raise it here (and record the finding in index.md)
        // if a run shows the value still missing after this many frames.
        private const int FramesToWaitForInitialize = 4;

        [UnityTest]
        public IEnumerator ComputeResolvedIndices_ArriveBeforeInitialize()
        {
            CreateProbe(out GameObject go, out ProjectileTrailStripBridgeProbe probe);

            probe.DispatchBatch(new[] { 0, 1, 2 }, batchTag: 1);
            for (int i = 0; i < FramesToWaitForInitialize; i++)
            {
                yield return null;
            }

            uint2[] samples = probe.ReadDebugSamples();
            Assert.That(samples[0], Is.EqualTo(new uint2(0, 1)));
            Assert.That(samples[1], Is.EqualTo(new uint2(1, 1)));
            Assert.That(samples[2], Is.EqualTo(new uint2(2, 1)));
            Assert.That(samples[3], Is.EqualTo(ProjectileTrailStripBridgeProbe.UntouchedSample));

            Cleanup(go, probe);
        }

        [UnityTest]
        public IEnumerator InvalidIndex_DoesNotTouchLiveStrip()
        {
            CreateProbe(out GameObject go, out ProjectileTrailStripBridgeProbe probe);

            probe.DispatchBatch(new[] { 0 }, batchTag: 1);
            for (int i = 0; i < FramesToWaitForInitialize; i++)
            {
                yield return null;
            }

            // Request an out-of-range slot; the compute shader must write the invalid sentinel
            // for it, and stock VFX Graph 17.4 must reject the point before strip reservation, so
            // no debug-sample entry may change as a result of this dispatch.
            probe.DispatchBatch(new[] { StripCapacity + 5 }, batchTag: 2);
            for (int i = 0; i < FramesToWaitForInitialize; i++)
            {
                yield return null;
            }

            uint2[] samples = probe.ReadDebugSamples();
            Assert.That(samples[0], Is.EqualTo(new uint2(0, 1)), "Invalid request must not steal or overwrite slot 0.");
            for (int i = 1; i < StripCapacity; i++)
            {
                Assert.That(
                    samples[i],
                    Is.EqualTo(ProjectileTrailStripBridgeProbe.UntouchedSample),
                    $"Invalid request must not write any live strip slot (slot {i}).");
            }

            Cleanup(go, probe);
        }

        [UnityTest]
        public IEnumerator LaterBatch_DoesNotChangeOldPoints()
        {
            CreateProbe(out GameObject go, out ProjectileTrailStripBridgeProbe probe);

            probe.DispatchBatch(new[] { 0, 1 }, batchTag: 1);
            for (int i = 0; i < FramesToWaitForInitialize; i++)
            {
                yield return null;
            }

            probe.DispatchBatch(new[] { 2 }, batchTag: 2);
            for (int i = 0; i < FramesToWaitForInitialize; i++)
            {
                yield return null;
            }

            uint2[] samples = probe.ReadDebugSamples();
            Assert.That(samples[0], Is.EqualTo(new uint2(0, 1)), "Older batch's point must survive a later batch.");
            Assert.That(samples[1], Is.EqualTo(new uint2(1, 1)), "Older batch's point must survive a later batch.");
            Assert.That(samples[2], Is.EqualTo(new uint2(2, 2)));

            Cleanup(go, probe);
        }

        private static void CreateProbe(out GameObject go, out ProjectileTrailStripBridgeProbe probe)
        {
            go = new GameObject(nameof(ProjectileTrailStripBridgeTests));
            VisualEffect vfx = go.AddComponent<VisualEffect>();
            vfx.visualEffectAsset = ProjectileTrailStripBridgeProbeFixture.GraphAsset;
            probe = new ProjectileTrailStripBridgeProbe(
                ProjectileTrailStripBridgeProbeFixture.ComputeShader,
                vfx,
                StripCapacity,
                MaxRequestsPerBatch,
                PointLifetimeSeconds);
        }

        private static void Cleanup(GameObject go, ProjectileTrailStripBridgeProbe probe)
        {
            probe.Dispose();
            Object.Destroy(go);
        }
    }
}
