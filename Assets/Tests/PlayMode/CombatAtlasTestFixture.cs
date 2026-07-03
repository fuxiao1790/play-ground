#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;
using UnityEngine.U2D;

namespace PlayGround.Tests.PlayMode
{
    // Loads the real, manually-assembled test SpriteAtlas + its one packed sprite from disk.
    // CombatRenderResourceRegistry.Register(...) validates via Atlas.GetSprite(sprite.name), which
    // only returns non-null once Unity's Sprite Atlas system has actually packed a real project
    // asset — an in-memory Sprite.Create(...) sprite can never satisfy this, so every test fixture
    // that used to hand CombatRoot a synthetic sprite now shares this one real asset instead.
    internal static class CombatAtlasTestFixture
    {
        private const string AtlasPath = "Assets/Tests/TestAssets/CombatAtlasTest.spriteatlasv2";
        private const string SourceTexturePath = "Assets/Tests/TestAssets/CombatAtlasTestSource.png";

        private static SpriteAtlas _atlas;
        private static Sprite _sprite;

        public static SpriteAtlas Atlas => _atlas ??= AssetDatabase.LoadAssetAtPath<SpriteAtlas>(AtlasPath);

        // The original asset-database sprite reference (not SpriteAtlas.GetSprite(...)), matching
        // how a real skill prefab references its sprite: CombatRoot.Register(...) is always called
        // with this kind of reference, whose .texture is redirected to the atlas's packed page by
        // Unity's Sprite Packing system once the atlas is packed.
        public static Sprite Sprite
        {
            get
            {
                if (_sprite != null) return _sprite;
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(SourceTexturePath))
                {
                    if (asset is Sprite sprite)
                    {
                        _sprite = sprite;
                        break;
                    }
                }
                return _sprite;
            }
        }
    }
}
#endif
