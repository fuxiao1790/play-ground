using System.Collections.Generic;
using UnityEngine;

namespace PlayGround.Mob
{
    public sealed class MobBehaviourSelector
    {
        private readonly Dictionary<string, MobBehaviour> behaviours = new();
        private readonly Dictionary<string, string> triggerBehaviourMap = new();

        public void Configure(IReadOnlyList<MobBehaviour> behaviourList, IReadOnlyList<MobTriggerBehaviourMapping> mappings)
        {
            behaviours.Clear();
            triggerBehaviourMap.Clear();

            for (int i = 0; i < behaviourList.Count; i++)
            {
                MobBehaviour behaviour = behaviourList[i];
                if (behaviour == null || string.IsNullOrWhiteSpace(behaviour.Key))
                {
                    throw new MissingReferenceException($"Mob behaviour slot {i} needs a behaviour with a key.");
                }

                behaviours[behaviour.Key] = behaviour;
            }

            for (int i = 0; i < mappings.Count; i++)
            {
                MobTriggerBehaviourMapping mapping = mappings[i];
                if (string.IsNullOrWhiteSpace(mapping.triggerKey) || string.IsNullOrWhiteSpace(mapping.behaviourKey))
                {
                    throw new MissingReferenceException($"Mob trigger mapping slot {i} needs trigger and behaviour keys.");
                }

                if (!behaviours.ContainsKey(mapping.behaviourKey))
                {
                    throw new MissingReferenceException($"Mob trigger '{mapping.triggerKey}' maps to missing behaviour '{mapping.behaviourKey}'.");
                }

                triggerBehaviourMap[mapping.triggerKey] = mapping.behaviourKey;
            }

            if (!triggerBehaviourMap.ContainsKey(MobRoot.DefaultTriggerKey))
            {
                throw new MissingReferenceException($"Mob trigger map needs '{MobRoot.DefaultTriggerKey}'.");
            }
        }

        public Vector2 Update(float deltaTime, MobRoot mob, MobBlackboard blackboard, MobBehaviourState state)
        {
            MobBehaviour behaviour = SelectBehaviour(blackboard, state);
            blackboard.ActiveBehaviourKey = behaviour != null ? behaviour.Key : "none";
            return behaviour != null ? behaviour.Evaluate(deltaTime, mob, blackboard, state) : Vector2.zero;
        }

        private MobBehaviour SelectBehaviour(MobBlackboard blackboard, MobBehaviourState state)
        {
            if (!triggerBehaviourMap.TryGetValue(blackboard.RequestedTriggerKey, out string behaviourKey)
                || !behaviours.TryGetValue(behaviourKey, out MobBehaviour behaviour)
                || !behaviour.SupportsState(state))
            {
                blackboard.ActiveTriggerKey = "none";
                return null;
            }

            blackboard.ActiveTriggerKey = blackboard.RequestedTriggerKey;
            return behaviour;
        }
    }
}
