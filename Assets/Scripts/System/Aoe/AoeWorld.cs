using System.Collections.Generic;
using PlayGround.Common;
using UnityEngine;

namespace PlayGround.System.Aoe
{
    public sealed class AoeWorld
    {
        private readonly List<RuntimeAoe> pendingSpawns = new();
        private readonly List<RuntimeAoe> activeAoes = new();
        private readonly List<AoeTargetSnapshot> targets = new();
        private readonly Dictionary<int, AoeShape> shapesByType = new();
        private readonly Dictionary<int, Dictionary<int, ContactState>> contactsByAoe = new();
        private readonly List<int> staleTargets = new();
        private readonly List<AoeHitEvent> hitEvents = new();
        private readonly List<AoeDespawnedEvent> despawnedEvents = new();

        private int nextAoeId;
        private int stepVersion;

        public int ActiveCount => activeAoes.Count + pendingSpawns.Count;
        public int MaximumAoeCount { get; set; } = 10000;
        public int MaximumTargetCount { get; set; } = 100;

        public void RegisterAoeType(int typeId, AoeShape shape)
        {
            shapesByType[typeId] = shape;
        }

        public int SubmitSpawn(AoeSpawnCommand command)
        {
            if (!shapesByType.ContainsKey(command.TypeId))
            {
                throw new global::System.InvalidOperationException($"Missing AOE collision definition for type id {command.TypeId}.");
            }

            int aoeId = ++nextAoeId;
            pendingSpawns.Add(new RuntimeAoe(aoeId, command));
            return aoeId;
        }

        public void SubmitTargets(IReadOnlyList<AoeTargetSnapshot> snapshots)
        {
            targets.Clear();
            int count = Mathf.Min(snapshots.Count, MaximumTargetCount);
            for (int i = 0; i < count; i++)
            {
                targets.Add(snapshots[i]);
            }
        }

        public void Step(float deltaTime)
        {
            PromotePendingSpawns();
            stepVersion++;

            for (int i = activeAoes.Count - 1; i >= 0; i--)
            {
                RuntimeAoe aoe = activeAoes[i];
                StepAoe(ref aoe, deltaTime);

                if (aoe.Command.LifetimeSeconds <= 0f)
                {
                    RemoveAoe(i, aoe.AoeId);
                    continue;
                }

                aoe.RemainingLifetime -= Mathf.Max(0f, deltaTime);
                if (aoe.RemainingLifetime <= 0f)
                {
                    RemoveAoe(i, aoe.AoeId);
                    continue;
                }

                activeAoes[i] = aoe;
            }
        }

        public void DrainEvents(List<AoeHitEvent> hits, List<AoeDespawnedEvent> despawns)
        {
            hits.AddRange(hitEvents);
            despawns.AddRange(despawnedEvents);
            hitEvents.Clear();
            despawnedEvents.Clear();
        }

        public void Clear()
        {
            pendingSpawns.Clear();
            activeAoes.Clear();
            targets.Clear();
            contactsByAoe.Clear();
            staleTargets.Clear();
            hitEvents.Clear();
            despawnedEvents.Clear();
        }

        private void PromotePendingSpawns()
        {
            if (pendingSpawns.Count == 0)
            {
                return;
            }

            int availableSlots = Mathf.Max(0, MaximumAoeCount - activeAoes.Count);
            int count = Mathf.Min(availableSlots, pendingSpawns.Count);
            for (int i = 0; i < count; i++)
            {
                activeAoes.Add(pendingSpawns[i]);
            }

            pendingSpawns.RemoveRange(0, pendingSpawns.Count);
        }

        private void StepAoe(ref RuntimeAoe aoe, float deltaTime)
        {
            AoeShape shape = shapesByType[aoe.Command.TypeId];
            Dictionary<int, ContactState> contacts = ContactsFor(aoe.AoeId);
            Rect aoeBounds = AoeCollisionMath.Bounds(aoe.Command.Position, shape);

            for (int i = 0; i < targets.Count; i++)
            {
                AoeTargetSnapshot target = targets[i];
                if ((aoe.Command.TargetMask & target.TargetMask) == 0
                    || !aoeBounds.Overlaps(AoeCollisionMath.Bounds(target.Position, target.Shape))
                    || !AoeCollisionMath.Hit(aoe.Command.Position, shape, target.Position, target.Shape))
                {
                    continue;
                }

                ResolveHit(in aoe, target, contacts, deltaTime);
            }

            ReleaseExitedContacts(contacts);
        }

        private void ResolveHit(in RuntimeAoe aoe, AoeTargetSnapshot target, Dictionary<int, ContactState> contacts, float deltaTime)
        {
            bool hasContact = contacts.TryGetValue(target.TargetId, out ContactState contact);
            contact.CooldownRemaining = Mathf.Max(0f, contact.CooldownRemaining - Mathf.Max(0f, deltaTime));
            bool shouldHit = !hasContact || contact.CooldownRemaining <= 0f;
            contact.LastSeenStep = stepVersion;

            if (shouldHit)
            {
                contact.CooldownRemaining = Mathf.Max(0f, aoe.Command.TickIntervalSeconds);
                hitEvents.Add(new AoeHitEvent(
                    aoe.AoeId,
                    aoe.Command.TypeId,
                    target.TargetId,
                    aoe.Command.Position,
                    aoe.Command.Damage));
            }

            contacts[target.TargetId] = contact;
        }

        private Dictionary<int, ContactState> ContactsFor(int aoeId)
        {
            if (!contactsByAoe.TryGetValue(aoeId, out Dictionary<int, ContactState> contacts))
            {
                contacts = new Dictionary<int, ContactState>();
                contactsByAoe.Add(aoeId, contacts);
            }

            return contacts;
        }

        private void ReleaseExitedContacts(Dictionary<int, ContactState> contacts)
        {
            staleTargets.Clear();
            foreach (KeyValuePair<int, ContactState> pair in contacts)
            {
                if (pair.Value.LastSeenStep != stepVersion)
                {
                    staleTargets.Add(pair.Key);
                }
            }

            for (int i = 0; i < staleTargets.Count; i++)
            {
                contacts.Remove(staleTargets[i]);
            }
        }

        private void RemoveAoe(int index, int aoeId)
        {
            activeAoes.RemoveAt(index);
            contactsByAoe.Remove(aoeId);
            despawnedEvents.Add(new AoeDespawnedEvent(aoeId));
        }

        private struct RuntimeAoe
        {
            public RuntimeAoe(int aoeId, AoeSpawnCommand command)
            {
                AoeId = aoeId;
                Command = command;
                RemainingLifetime = command.LifetimeSeconds;
            }

            public int AoeId;
            public AoeSpawnCommand Command;
            public float RemainingLifetime;
        }

        private struct ContactState
        {
            public float CooldownRemaining;
            public int LastSeenStep;
        }
    }
}
