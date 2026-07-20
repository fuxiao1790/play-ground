using UnityEngine;

namespace PlayGround.Common
{
    public abstract class PersistentScriptableObject : ScriptableObject
    {
        [SerializeField, HideInInspector] private string assetGuid;

        public string AssetGuid
        {
            get
            {
#if UNITY_EDITOR
                if (string.IsNullOrEmpty(assetGuid))
                {
                    return UnityEditor.AssetDatabase.AssetPathToGUID(
                        UnityEditor.AssetDatabase.GetAssetPath(this));
                }
#endif
                return assetGuid;
            }
        }

#if UNITY_EDITOR
        protected virtual void OnValidate()
        {
            BakeAssetGuid();
        }

        public bool BakeAssetGuid()
        {
            string path = UnityEditor.AssetDatabase.GetAssetPath(this);
            string guid = UnityEditor.AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrEmpty(guid) || assetGuid == guid)
            {
                return false;
            }

            assetGuid = guid;
            UnityEditor.EditorUtility.SetDirty(this);
            return true;
        }
#endif
    }
}
