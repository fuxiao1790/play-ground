---
task: 004-removed
title: No shader changes needed (disabled entities culled via scale=0)
---

## Summary

No shader changes required. Disabled entities are written to the GPU buffer with a degenerate matrix (scale=0), which results in a 0×0 quad that the GPU naturally culls during rasterization. The shader works unchanged.

## Why This Works

- A matrix with scale columns zeroed (m00, m01, m10, m11, m20, m21 = 0) transforms any vertex to local position ≈ (0, 0)
- The unit quad's vertices become degenerate points at/near the origin
- The GPU rasterizer culls the resulting 0×0 triangle (no pixels rendered)
- No fragment shader execution; zero GPU cost

## Acceptance Criteria

- [ ] Shader remains unchanged
- [ ] Disabled entities don't appear on screen (culled by GPU rasterizer, not shader logic)
- [ ] No performance regression

## Testing

- [ ] Render a scene with mixed enabled/disabled entities
- [ ] Verify disabled entities don't render (expected result: gone)
- [ ] Verify GPU rasterizer naturally culls zero-scale quads (no special shader handling needed)
