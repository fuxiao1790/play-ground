using NUnit.Framework;
using PlayGround.System.Combat.Vfx;
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

            int first = root.Register(asset, VfxDataShape.ImpactCircle);
            int second = root.Register(asset, VfxDataShape.ImpactCircle);

            Assert.That(first, Is.GreaterThan(0));
            Assert.That(second, Is.EqualTo(first));
            Assert.That(root.transform.childCount, Is.EqualTo(1));
        }

        [Test]
        public void RegisterNullAsset_ReturnsZeroAndCreatesNoChild()
        {
            CombatVfxRoot root = CreateRoot();

            int id = root.Register(null, VfxDataShape.ImpactCircle);

            Assert.That(id, Is.Zero);
            Assert.That(root.transform.childCount, Is.Zero);
        }

        [Test]
        public void LineSegmentId_RetainsShapeAndLocalIndex()
        {
            int id = VfxDataShapeTable.EncodeId(VfxDataShape.LineSegment, 7);

            Assert.That(VfxDataShapeTable.DecodeShape(id), Is.EqualTo(VfxDataShape.LineSegment));
            Assert.That(VfxDataShapeTable.DecodeLocalIndex(id), Is.EqualTo(7));
            Assert.That(VfxDataShapeTable.BufferCountFor(VfxDataShape.LineSegment), Is.EqualTo(3));
        }

        [Test]
        public void EnsureBufferCapacity_BeyondInitialCapacity_GrowsWithoutShrinking()
        {
            var res = new CircularVfxResources
            {
                BufferCapacity = AoeVfxResourcesBase.InitialBufferCapacity
            };

            try
            {
                int count = AoeVfxResourcesBase.InitialBufferCapacity + 1;
                res.EnsureBufferCapacity(count);

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
