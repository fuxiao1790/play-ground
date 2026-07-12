Generate a **[WIDTH]×[HEIGHT] RGBA PNG texture** for a 2D particle or visual effect.

Effect description:

**[DESCRIBE THE EFFECT HERE]**

General requirements:

* Simple, clean design with low visual complexity
* Readable at the final requested resolution
* Avoid tiny details that disappear when viewed in-game
* Center the effect unless another placement is specified
* Keep the visible effect comfortably inside the image boundaries
* Do not crop or cut off any part of the effect
* Use smooth edges and controlled alpha falloff
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
* Transparency must be stored in the PNG alpha channel, not visually simulated

Output requirements:

* Exact dimensions: **[WIDTH]×[HEIGHT] pixels**
* Aspect ratio: **[ASPECT RATIO]**
* File format: **PNG with alpha transparency**
* Designed for use as a game-engine particle or VFX texture
* Generate only the texture asset, with no presentation background or mockup
