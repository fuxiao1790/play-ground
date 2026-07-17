using NUnit.Framework;
using PlayGround.System.Combat.Vfx;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.VFX;

namespace PlayGround.Tests.EditMode
{
    public sealed class CombatVfxRootRegistrationTests
    {
        private GameObject rootObject;

        [TearDown]
        public void TearDown()
        {
            if (rootObject != null)
            {
                Object.DestroyImmediate(rootObject);
            }
        }

        [Test]
        public void RegisterSameAsset_ReturnsSameIdAndCreatesOneChild()
        {
            VisualEffectAsset asset = LoadVfxAsset();
            CombatVfxRoot root = CreateRoot();

            int first = root.Register(asset, requireAreaSizeContract: true);
            int second = root.Register(asset, requireAreaSizeContract: true);

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(root.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterNullAsset_ReturnsZeroAndCreatesNoChild()
        {
            CombatVfxRoot root = CreateRoot();

            int id = root.Register(null, requireAreaSizeContract: true);

            Assert.That(id, Is.Zero);
            Assert.That(root.transform.childCount, Is.Zero);
        }

        [Test]
        public void StagingBeyondInitialCapacity_GrowsBuffersWithoutDroppingRequests()
        {
            var res = new AoeVfxTypeResources
            {
                BufferCapacity = AoeVfxTypeResources.InitialBufferCapacity,
                Staging = new Unity.Collections.NativeList<float2>(
                    AoeVfxTypeResources.InitialBufferCapacity,
                    Unity.Collections.Allocator.Persistent),
                AreaSizeStaging = new Unity.Collections.NativeList<float>(
                    AoeVfxTypeResources.InitialBufferCapacity,
                    Unity.Collections.Allocator.Persistent)
            };

            try
            {
                int count = AoeVfxTypeResources.InitialBufferCapacity + 1;
                for (int i = 0; i < count; i++)
                {
                    Assert.That(res.TryStage(new float2(i, i), 1f), Is.True);
                }

                res.EnsureBufferCapacity(count);

                Assert.That(res.Staging.Length, Is.EqualTo(count));
                Assert.That(res.AreaSizeStaging.Length, Is.EqualTo(count));
                Assert.That(res.BufferCapacity, Is.GreaterThanOrEqualTo(count));
            }
            finally
            {
                res.Dispose();
            }
        }

        private CombatVfxRoot CreateRoot()
        {
            rootObject = new GameObject("CombatVfxRootTest");
            return rootObject.AddComponent<CombatVfxRoot>();
        }

        private static VisualEffectAsset LoadVfxAsset()
        {
            VisualEffectAsset asset =
                AssetDatabase.LoadAssetAtPath<VisualEffectAsset>("Assets/Vfx/Vortex.vfx");
            Assert.That(asset, Is.Not.Null);
            return asset;
        }
    }
}
