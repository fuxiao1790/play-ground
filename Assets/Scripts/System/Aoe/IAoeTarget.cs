using PlayGround.Common;
using PlayGround.System.Common;

namespace PlayGround.System.Aoe
{
    public interface IAoeTarget : ICombatTarget
    {
        void ReceiveAoeHit(DamageSnapshot damage);
    }
}
