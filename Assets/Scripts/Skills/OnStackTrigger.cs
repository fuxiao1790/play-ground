using PlayGround.Mob;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Triggers/On Stack", fileName = "NewOnStackTrigger")]
    public sealed class OnStackTrigger : TriggerLink
    {
        public MobDebuffStatus debuffStatus = MobDebuffStatus.Volatile;
        [Min(1)] public int stacksPerHit = 1;
        [Min(1)] public int stackThreshold = 3;
    }
}
