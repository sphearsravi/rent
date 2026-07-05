# Branding assets

Files in this directory are picked up by `QuestPdfService` to brand the
generated invoice / receipt / tenant statement / executive dashboard PDFs.

## Required

**`logo.png`** — the company logo. Used in the top-left of every PDF.
- Recommended size: 600 × 600 px or larger; square or vertical
  composition (the PDF renderer scales to ~80 px tall while
  preserving aspect ratio)
- PNG with transparent background is best so it sits cleanly on the
  gold header band
- Max ~500 KB so PDFs don't bloat

## Optional

**`logo-light.png`** — alternate variant for dark backgrounds. Not used
yet; reserved for future "light on dark" headers.

## How the renderer uses these

The path is read from `appsettings.json` → `Branding:LogoPath`. If the
file doesn't exist on disk, the renderer silently falls back to a text-only
header with the company name in `Branding:SecondaryHex` colour, so
generation never breaks because of a missing asset.
