# Level0 launcher artwork

The existing game artwork `03_wire_full_bleed.png` was reused unchanged. It was created on 2026-08-19 following the request to regenerate the Level0 wire creature icon without white borders.

- Original size: 1024 × 1024, RGB.
- SHA256: b34b624c198cad46ae68d901a2501ab5a1b8a24895ab3a4f88341fbd55e6ddf6.
- Legacy and round icons use this artwork.
- Adaptive icons use the full-bleed artwork as background, with an empty transparent foreground. The launcher applies its own mask.
- `Level0LauncherIconSetup.Apply` reapplies all 18 Android slots during every build.
