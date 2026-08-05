# Resource Spend Gate

Root skill casts keep authored resource configuration on the GameObject but use
ECS as the runtime authority for Current values.

1. `SkillDriver` fires optimistically, resets its cooldown, and submits an
   `ExternalSpawnRequest` with caster proxy, compiled total `ManaCost`, and
   cast token. Each valid link's resolved factor,
   `manaCostIncreased * manaCostMultiplier`, from
   `TriggerLink.ResolveManaCostFactor()` multiplies both the initial active
   skill chain cost and that link's triggered skill cost.
2. `ExternalSpawnGateSystem` runs serially before spawn expansion. It accepts a
   missing `Mana` component, otherwise checks and deducts that caster's `Mana`.
3. Accepted requests become the existing typed projectile or AOE internal event
   in the same simulation update. Interval and impact children already produce
   internal events and bypass this gate; their mana costs were included in the
   initiating active skill's one-time spend.
4. Rejected requests emit `SpawnRejectedEvent`. `SpawnRejectionBridge` resolves
   the target companion during presentation and returns the token to `SkillDriver`.
5. The matching slot refunds its cooldown. Accepted casts need no callback.

`ResourceRegenSystem` runs after combat application. Roots mirror resource
Current from their target proxy for UI, death, and feedback, while Max and regen
rate remain root-authored and are pushed on change.
