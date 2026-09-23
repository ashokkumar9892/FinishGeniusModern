# Color Matching

Written for: whoever runs the colour work at AWFI, and the next developer on this module.

This is the part of *Automated Formulation & Wood Stain Color Prediction* that is software. It is in the app now, under
**Formulation → Color Matching**. The rest of that document — the camera rig, the spectrophotometer, the physical
samples — is equipment and lab work, and no amount of code substitutes for it.

## What the app does today

**Sample Library.** One row per finished sample: wood species and sanding grit, the unfinished board's own colour if it
was measured, the formula and strength, how it was applied (spray/wipe, coats, wet film, flash), the sealer, topcoat and
sheen, and the L\*a\*b\* it measured — with whether that reading came from a spectrophotometer, a photo or was typed in.
This is the training database the requirements call the most important part of the project.

**Match a Colour.** Give it the colour you want and it ranks the recorded formulas by ΔE00, the colour difference an eye
actually judges by. Where a formula has samples at two strengths, it reads the strength off the line between them —
"mix at 7%", never a strength nobody has mixed. Where the board's own colour is known, a sample's measured *shift* is
applied to your board instead of assuming the same wood. Every line says what it stands on: how many samples, how many
on a device, whether the strength was interpolated.

**Stain Preview.** Photograph the bare wood, drag a box over it, pick a formula: the picture comes back as that stain
should leave it. The colour comes from the samples; the grain is the photograph's. It is a guide for a customer, not a
substitute for a sample board.

**Reading colour from a photo.** Put a grey or white card in the shot, mark the wood and the card, and the app corrects
the lighting away and reports L\*a\*b\*. Without a card it still reads, but marks the reading uncalibrated, and
predictions weigh device readings higher. Validated in testing: a board photographed under a strong warm light read
ΔE ≈ 12 off before correction and within about ΔE 1 of truth after it.

## What it is not

- **It is not a trained model.** There is no neural network and nothing is extrapolated. Every answer traces to samples
  someone measured; with no samples for a formula the app says exactly that instead of guessing.
- **A photo is not a measurement.** A spectrophotometer reading is the ground truth the requirements ask for; the
  calibrated photo path exists so useful work can start before one is bought.
- **The preview predicts colour, not finish.** Gloss, grain raise, blotching and how a dye moves in end grain are not
  modelled.

## Getting useful results

The requirements suggest starting small, and that is right: **3–5 woods × 20–30 formulas × several strengths**, applied
the way the shop really applies them. In practice:

1. Record the unfinished board as well as the finished sample. It is one extra reading and it is what lets a formula be
   predicted on a board it was never tried on.
2. Record at least two strengths of any formula you care about. That single step turns "nearest recorded colour" into
   "mix at 7%".
3. Keep the conditions honest — the same formula sprayed and wiped is two different colours, and the app can only tell
   them apart if the rows say which was which.
4. Re-record what you actually got after mixing to a recommendation. That is how the library gets better.

The Sample Library header counts samples, woods, formulas and device readings, so it is visible at a glance how far the
dataset has come.

## Where it lives

| Part | File |
|------|------|
| Colour maths (sRGB/Lab/OKLab, ΔE76, ΔE00) | `backend/FinishGenius.Api/Services/ColorScience.cs` |
| Matching, strength interpolation, confidence | `backend/FinishGenius.Api/Services/ColorMatchService.cs` |
| Photo reading and the preview renderer | `backend/FinishGenius.Api/Services/PhotoColorService.cs` |
| API (`/api/color-matching/...`) | `backend/FinishGenius.Api/Controllers/ColorMatchingController.cs` |
| Screens | `frontend/src/pages/colors/` |
| Table | `fg.ColorSamples` (EF migration) / `dbo.FG_ColorSamples` (`database/FG_ColorSamples.sql`) |

ΔE00 is implemented from Sharma, Wu & Dalal (2005) and checked against their reference data: 13 of the 14 published
pairs match to within 0.0002, the fourteenth being a degenerate pair whose published value rounds to zero.

## The next step, when the data exists

Once a few hundred samples are recorded, the honest next move is a model that learns the *shift* a formula makes as a
function of strength and wood, fitted per colorant rather than per formula — that is what would let the app propose a
formula it has never seen, which is the reverse-engineering the requirements describe. It needs the library first, and
the library is now being collected in a shape that supports it.
