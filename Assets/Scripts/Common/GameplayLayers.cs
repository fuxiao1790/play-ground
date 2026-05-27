using UnityEngine;

namespace PlayGround.Common
{
    public static class GameplayLayers
    {
        public const string PlayerBody = "PlayerBody";
        public const string PlayerHurtbox = "PlayerHurtbox";
        public const string PlayerProjectile = "PlayerProjectile";
        public const string PlayerAoe = "PlayerAoe";
        public const string MobBody = "MobBody";
        public const string MobHurtbox = "MobHurtbox";
        public const string MobProjectile = "MobProjectile";
        public const string MobAoe = "MobAoe";
        public const string Environment = "Environment";

        public static int RequiredLayer(string layerName)
        {
            int layer = LayerMask.NameToLayer(layerName);
            if (layer < 0)
            {
                throw new MissingReferenceException($"Unity layer '{layerName}' is required.");
            }

            return layer;
        }

        public static int RequiredMask(string layerName)
        {
            return 1 << RequiredLayer(layerName);
        }
    }
}
