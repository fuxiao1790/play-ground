using System;
using UnityEngine;

namespace PlayGround.Common.Stats
{
    public sealed class Resource
    {
        public Resource(float max, float regenPerSecond)
        {
            Max = Mathf.Max(0f, max);
            RegenPerSecond = Mathf.Max(0f, regenPerSecond);
            Current = Max;
        }

        public float Max { get; private set; }
        public float Current { get; private set; }
        public float RegenPerSecond { get; private set; }
        public bool IsDepleted => Current <= 0f;

        public event Action Depleted;
        public event Action Changed;

        public void SetMax(float max)
        {
            UpdateAuthoring(max, RegenPerSecond);
        }

        public bool UpdateAuthoring(float max, float regenPerSecond)
        {
            max = Mathf.Max(0f, max);
            regenPerSecond = Mathf.Max(0f, regenPerSecond);
            float previousCurrent = Current;
            bool changed = !Mathf.Approximately(Max, max)
                || !Mathf.Approximately(RegenPerSecond, regenPerSecond);

            Max = max;
            RegenPerSecond = regenPerSecond;
            Current = Mathf.Clamp(Current, 0f, Max);
            changed |= !Mathf.Approximately(previousCurrent, Current);
            NotifyChanged(previousCurrent, changed);
            return changed;
        }

        public void Reset(float max, float regenPerSecond)
        {
            float previousCurrent = Current;
            Max = Mathf.Max(0f, max);
            RegenPerSecond = Mathf.Max(0f, regenPerSecond);
            Current = Max;
            NotifyChanged(previousCurrent, true);
        }

        public void MirrorCurrent(float current)
        {
            float previousCurrent = Current;
            Current = Mathf.Clamp(current, 0f, Max);
            NotifyChanged(previousCurrent, !Mathf.Approximately(previousCurrent, Current));
        }

        private void NotifyChanged(float previousCurrent, bool changed)
        {
            if (!changed)
            {
                return;
            }

            Changed?.Invoke();
            if (previousCurrent > 0f && Current <= 0f)
            {
                Depleted?.Invoke();
            }
        }
    }
}
