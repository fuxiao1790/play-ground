using PlayGround.Attack;
using UnityEngine;

namespace PlayGround.Player
{
    public sealed class PlayerAttackLoadout
    {
        private readonly ProjectileAttack[] projectileAttacks;
        private readonly AoeAttack[] aoeAttacks;
        private readonly int projectileAttackCount;
        private readonly int aoeAttackCount;

        public PlayerAttackLoadout(ProjectileAttack[] projectileAttacks, AoeAttack[] aoeAttacks, int maxAttackCount)
        {
            int availableSlots = Mathf.Max(0, maxAttackCount);
            this.projectileAttacks = projectileAttacks;
            this.aoeAttacks = aoeAttacks;
            projectileAttackCount = Mathf.Min(projectileAttacks.Length, availableSlots);
            aoeAttackCount = Mathf.Min(aoeAttacks.Length, Mathf.Max(0, availableSlots - projectileAttackCount));
        }

        public int AttackCount => projectileAttackCount + aoeAttackCount;

        public void TickHeldFire(bool attackHeld, Vector2 aimDirection, Vector2 aimWorldPosition)
        {
            if (!attackHeld)
            {
                return;
            }

            for (int i = 0; i < projectileAttackCount; i++)
            {
                projectileAttacks[i].TryFire(aimDirection);
            }

            for (int i = 0; i < aoeAttackCount; i++)
            {
                aoeAttacks[i].TryFire(aimWorldPosition);
            }
        }
    }
}
