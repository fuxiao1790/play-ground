using System;
using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Skills
{
    [CreateAssetMenu(menuName = "PlayGround/Skills/Player Loadout", fileName = "NewPlayerLoadout")]
    public sealed class PlayerLoadout : ScriptableObject
    {
        [SerializeField] private SkillSet[] rootSets = Array.Empty<SkillSet>();
        [SerializeField, SerializeReference] private List<TriggerLink> links = new();
        [SerializeField, Min(1)] private int maxRootSets = 8;

        public SkillSet[] RootSets => rootSets;
        public IReadOnlyList<TriggerLink> Links => links;
        public int MaxRootSets => maxRootSets;
    }
}
