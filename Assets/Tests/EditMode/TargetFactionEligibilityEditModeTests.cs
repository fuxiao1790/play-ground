using NUnit.Framework;
using PlayGround.System.Combat.Core;
using PlayGround.System.Combat.Targets;

namespace PlayGround.Tests.EditMode
{
    // Pure unit coverage for the shared TargetFaction.CanHit eligibility predicate.
    // No ECS World is needed: TargetFaction is a plain unmanaged struct and CanHit is
    // a static value comparison, so these tests construct it directly.
    public sealed class TargetFactionEligibilityEditModeTests
    {
        [Test]
        public void HostileOnly_RejectsSameFactionAttacker()
        {
            TargetFaction target = TargetFaction.Hostile(CombatFaction.Player);

            Assert.That(TargetFaction.CanHit(CombatFaction.Player, in target), Is.False);
        }

        [Test]
        public void HostileOnly_AcceptsDifferentFactionAttacker()
        {
            TargetFaction target = TargetFaction.Hostile(CombatFaction.Player);

            Assert.That(TargetFaction.CanHit(CombatFaction.Mob, in target), Is.True);
        }

        [Test]
        public void AllowedFactionOnly_AcceptsSelectedFactionEvenWhenEqualToOwnFaction()
        {
            TargetFaction target = TargetFaction.AllowedFrom(CombatFaction.Player, CombatFaction.Player);

            Assert.That(TargetFaction.CanHit(CombatFaction.Player, in target), Is.True);
        }

        [Test]
        public void AllowedFactionOnly_AcceptsSelectedFactionDifferentFromOwnFaction()
        {
            TargetFaction target = TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Player);

            Assert.That(TargetFaction.CanHit(CombatFaction.Player, in target), Is.True);
        }

        [Test]
        public void AllowedFactionOnly_RejectsNonSelectedFactionAttacker()
        {
            TargetFaction target = TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.Mob);

            Assert.That(TargetFaction.CanHit(CombatFaction.Player, in target), Is.False);
        }

        [Test]
        public void HostileOnly_NoneAttackerAlwaysRejected()
        {
            TargetFaction target = TargetFaction.Hostile(CombatFaction.Player);

            Assert.That(TargetFaction.CanHit(CombatFaction.None, in target), Is.False);
        }

        [Test]
        public void AllowedFactionOnly_NoneAttackerAlwaysRejectedEvenWhenSelected()
        {
            // AllowedAttackerFaction defaults to None only via misconfiguration; even then,
            // CombatFaction.None must never be treated as a valid attacker.
            TargetFaction target = TargetFaction.AllowedFrom(CombatFaction.Mob, CombatFaction.None);

            Assert.That(TargetFaction.CanHit(CombatFaction.None, in target), Is.False);
        }

        [Test]
        public void UnknownFilterMode_Rejects()
        {
            var target = new TargetFaction
            {
                Value = CombatFaction.Mob,
                FilterMode = (TargetFactionFilterMode)255,
                AllowedAttackerFaction = CombatFaction.Player
            };

            Assert.That(TargetFaction.CanHit(CombatFaction.Player, in target), Is.False);
        }
    }
}
