using System.Collections;
using System.Reflection;
using NUnit.Framework;
using PlayGround.Common;
using PlayGround.System.Aoe;
using PlayGround.System.Common;
using PlayGround.System.Projectile;
using Unity.Collections;
using Unity.Entities;
using UnityEngine;
using UnityEngine.TestTools;

namespace PlayGround.Tests.PlayMode
{
    public sealed class CombatRuntimeRootPlayModeTests
    {
        private static int nextTargetId = 9000;

        [UnityTest]
        public IEnumerator SharedTargetSetWritesSameSnapshotToProjectileAndAoeScopesOnce()
        {
            CreateRuntime(out GameObject runtimeObject, out CombatRuntimeRoot runtime);
            CreateProjectileRoot("ProjectileEndpoint", out GameObject projectileObject, out ProjectileRoot projectileRoot);
            CreateAoeRoot("AoeEndpoint", out GameObject aoeObject, out AoeRoot aoeRoot);
            CombatTargetProbe target = CreateTarget("SharedTarget", new Vector2(3f, 4f));

            runtime.BindScope(projectileRoot, "SetA");
            runtime.BindScope(aoeRoot, "SetA");
            runtime.RegisterTarget("SetA", target);

            yield return null;

            CombatTargetElement projectileSnapshot = FirstTarget(ProjectileScopeEntity(projectileRoot));
            CombatTargetElement aoeSnapshot = FirstTarget(AoeScopeEntity(aoeRoot));

            Assert.That(runtime.GetOrCreateTargetSet("SetA").SnapshotBuildCount, Is.EqualTo(1));
            Assert.That(projectileSnapshot.TargetId, Is.EqualTo(target.TargetId));
            Assert.That(aoeSnapshot.TargetId, Is.EqualTo(target.TargetId));
            Assert.That(projectileSnapshot.Position.x, Is.EqualTo(aoeSnapshot.Position.x));
            Assert.That(projectileSnapshot.Position.y, Is.EqualTo(aoeSnapshot.Position.y));

            Cleanup(runtimeObject, projectileObject, aoeObject, target.gameObject);
        }

        [UnityTest]
        public IEnumerator ProjectileScopesUseDifferentAgnosticTargetSetsInSameEcsWorld()
        {
            CreateRuntime(out GameObject runtimeObject, out CombatRuntimeRoot runtime);
            CreateProjectileRoot("ProjectileEndpointA", out GameObject projectileObjectA, out ProjectileRoot projectileRootA);
            CreateProjectileRoot("ProjectileEndpointB", out GameObject projectileObjectB, out ProjectileRoot projectileRootB);
            CombatTargetProbe targetA = CreateTarget("TargetA", Vector2.zero);
            CombatTargetProbe targetB = CreateTarget("TargetB", new Vector2(10f, 0f));

            runtime.BindScope(projectileRootA, "SetA");
            runtime.BindScope(projectileRootB, "SetB");
            runtime.RegisterTarget("SetA", targetA);
            runtime.RegisterTarget("SetB", targetB);

            projectileRootA.Spawn(new ProjectileSpawnCommand(
                Vector2.zero,
                Vector2.right,
                0f,
                1f,
                1f,
                new DamageSnapshot(2f),
                CombatShapeType.Circle));
            projectileRootB.Spawn(new ProjectileSpawnCommand(
                new Vector2(10f, 0f),
                Vector2.right,
                0f,
                1f,
                1f,
                new DamageSnapshot(3f),
                CombatShapeType.Circle));

            yield return null;

            Assert.That(targetA.TotalDamage, Is.EqualTo(2f));
            Assert.That(targetB.TotalDamage, Is.EqualTo(3f));
            Assert.That(ProjectileScopeEntity(projectileRootA), Is.Not.EqualTo(ProjectileScopeEntity(projectileRootB)));

            Cleanup(runtimeObject, projectileObjectA, projectileObjectB, targetA.gameObject, targetB.gameObject);
        }

        private static void CreateRuntime(out GameObject runtimeObject, out CombatRuntimeRoot runtime)
        {
            runtimeObject = new GameObject("CombatRuntimeRoot");
            runtime = runtimeObject.AddComponent<CombatRuntimeRoot>();
        }

        private static void CreateProjectileRoot(string name, out GameObject rootObject, out ProjectileRoot root)
        {
            rootObject = new GameObject(name);
            rootObject.SetActive(false);
            root = rootObject.AddComponent<ProjectileRoot>();
            root.Configure(Sprite.Create(Texture2D.whiteTexture, new Rect(0f, 0f, 1f, 1f), Vector2.one * 0.5f));
            rootObject.SetActive(true);
        }

        private static void CreateAoeRoot(string name, out GameObject rootObject, out AoeRoot root)
        {
            rootObject = new GameObject(name);
            rootObject.SetActive(false);
            root = rootObject.AddComponent<AoeRoot>();
            root.Configure(~0);
            rootObject.SetActive(true);
        }

        private static CombatTargetProbe CreateTarget(string name, Vector2 position)
        {
            GameObject targetObject = new(name);
            targetObject.transform.position = position;
            CombatTargetProbe target = targetObject.AddComponent<CombatTargetProbe>();
            target.Configure(++nextTargetId, targetMask: 1, radius: 0.25f);
            return target;
        }

        private static CombatTargetElement FirstTarget(Entity scope)
        {
            EntityManager entityManager = World.DefaultGameObjectInjectionWorld.EntityManager;
            DynamicBuffer<CombatTargetElement> targets = entityManager.GetBuffer<CombatTargetElement>(scope);
            Assert.That(targets.Length, Is.EqualTo(1));
            return targets[0];
        }

        private static Entity ProjectileScopeEntity(ProjectileRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(ProjectileRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static Entity AoeScopeEntity(AoeRoot root)
        {
            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
            FieldInfo scopeEntityField = typeof(AoeRoot).GetField("scopeEntity", Flags);
            return (Entity)scopeEntityField.GetValue(root);
        }

        private static void Cleanup(params GameObject[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                Object.Destroy(objects[i]);
            }
        }

        private sealed class CombatTargetProbe : MonoBehaviour, IProjectileTarget, IAoeTarget
        {
            private int targetId;
            private int targetMask;
            private float radius;

            public int TargetId => targetId;
            public Vector2 CombatTargetPosition => transform.position;
            public float CombatTargetRadius => radius;
            public Vector2 CombatTargetHalfExtents => Vector2.one * radius;
            public float CombatTargetRotationRadians => 0f;
            public CombatShapeType CombatTargetShapeType => CombatShapeType.Circle;
            public int CombatTargetMask => targetMask;
            public bool IsCombatTargetActive => true;
            public float TotalDamage { get; private set; }

            public void Configure(int id, int targetMask, float radius)
            {
                targetId = id;
                this.targetMask = targetMask;
                this.radius = radius;
            }

            public void ReceiveHit(in CombatHitData hit)
            {
                TotalDamage += hit.Damage.Amount;
            }
        }
    }
}
