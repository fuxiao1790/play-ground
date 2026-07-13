Generate a **[WIDTH]×[HEIGHT] RGBA PNG texture** for a 2D particle or visual effect.

Effect description:

**[DESCRIBE THE EFFECT HERE]**

Art style:

* Intended for use in a 2D pixel game
* Crisp, clean pixel-art appearance
* Strong, readable silhouette
* Limited palette and chunky, game-friendly forms
* Keep it consistent with a low-resolution retro or minimalist pixel-art aesthetic
* No soft, realistic, or high-detail illustration style

General requirements:

* Simple, clean design with low visual complexity
* Readable at the final requested resolution
* Avoid tiny details that disappear when viewed in-game
* Center the effect unless another placement is specified
* Keep the visible effect comfortably inside the image boundaries
* Do not crop or cut off any part of the effect
* Use crisp pixel-art edges and controlled alpha falloff
* Keep the effect readable at the final game resolution
* Use semi-transparent pixels sparingly for performance and readability
* Prefer hard-edged or mostly opaque pixels where possible
* Avoid excessive blur, noise, haze, bloom, or photorealistic detail
* Do not add unrelated decorative elements
* Do not add text, borders, frames, shadows, or watermarks
* Use only the requested colors

Transparency requirements:

* Export as a real RGBA PNG with a genuinely transparent background
* Every pixel outside the visible effect must have alpha = 0
* Do not create a white, gray, black, colored, or gradient background
* Do not render a checkerboard transparency pattern into the image
* Do not add a full-canvas glow, fog, haze, vignette, or ambient lighting
* Do not leave a white or gray matte around translucent pixels
* Limit semi-transparent pixels to only the necessary soft edge regions
* Transparency must be stored in the PNG alpha channel, not visually simulated

Output requirements:

* Exact dimensions: **[WIDTH]×[HEIGHT] pixels**
* Aspect ratio: **[ASPECT RATIO]**
* File format: **PNG with alpha transparency**
* Designed for use as a game-engine particle or VFX texture
* Generate only the texture asset, with no presentation background or mockup
