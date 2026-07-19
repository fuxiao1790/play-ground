using UnityEngine;

namespace PlayGround.Player
{
    [CreateAssetMenu(menuName = "PlayGround/Player/Sprite Facing Set")]
    public sealed class SpriteFacingSet : ScriptableObject
    {
        [SerializeField] private Sprite upLeft;
        [SerializeField] private Sprite upRight;
        [SerializeField] private Sprite downLeft;
        [SerializeField] private Sprite downRight;

        public bool HasAllSprites => upLeft != null && upRight != null && downLeft != null && downRight != null;

        public Sprite Resolve(bool up, bool right) =>
            up ? (right ? upRight : upLeft) : (right ? downRight : downLeft);
    }
}
