using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Skill Loadout", fileName = "NewSkillLoadout")]
    public sealed class SkillLoadout : ScriptableObject
    {
        [SerializeField, SerializeReference] private List<LoadoutSlot> slots = new();
        [SerializeField, Min(1)] private int maxRootSets = 8;

        public IReadOnlyList<LoadoutSlot> Slots => slots;
        public int MaxRootSets => maxRootSets;
    }
}
