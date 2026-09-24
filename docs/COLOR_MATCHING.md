# Color Matching

Written for: whoever runs the colour work at AWFI, and the next developer on this module.

This is the part of *Automated Formulation & Wood Stain Color Prediction* that is software. It is in the app now, under
**Formulation → Color Matching**. The rest of that document — the camera rig, the spectrophotometer, the physical
samples — is equipment and lab work, and no amount of code substitutes for it.

## What the app does today

**Sample Library.** One row per finished sample: wood species, sanding grit, grain direction, porosity, growth rings,
existing finish and moisture content; the unfinished board's own colour if it was measured; the formula and strength;
how it was applied (spray/wipe, coats, wet film, flash, gun, pressure, drying conditions); the sealer, topcoat and
sheen; and the L\*a\*b\* it measured — with whether that reading came from a spectrophotometer, a photo or was typed in.
This is the training database the requirements call the most important part of the project.

**The recipe travels with the sample.** When a sample is linked to one of the group's formulas, the ingredients are
copied onto it — each colorant with its grams and its share of the batch, which is the per-colorant table on page 6 of
the requirements. A formula gets edited over the years, so a sample that only pointed at one would slowly start lying
about what was on the wood. Those percentages are shown against each recommendation, and they are what a formulation
model would eventually have to learn from.

**Match a Colour.** Give it the colour you want and it ranks the recorded formulas by ΔE00, the colour difference an eye
actually judges by. Where a formula has samples at two strengths, it reads the strength off the line between them —
"mix at 7%", never a strength nobody has mixed. Where the board's own colour is known, a sample's measured *shift* is
applied to your board instead of assuming the same wood. Every line says what it stands on: how many samples, how many
on a device, whether the strength was interpolated.

**Stain Preview.** Photograph the bare wood, drag a box over it, pick a formula: the picture comes back as that stain
should leave it. The colour comes from the samples; the grain is the photograph's. It is a guide for a customer, not a
substitute for a sample board.

**Reading colour from a photo.** Put a **24-patch ColorChecker** in the shot (the chart page 2 of the requirements
photographs), mark the wood and the chart, and the app fits a correction across all 24 patches — which undoes a colour
cast, a wrong white balance and channel crosstalk together, not merely a brightness error. A single grey or white card
is still accepted where that is all there is. Whichever is used, the app reports how far out the photo was and how far
out it remains, and a reading with nothing to calibrate against is marked uncalibrated.

Validated on a synthetic chart photographed under a heavy cast: the wood read a\* −3.3 / b\* 21.8 uncorrected against a
truth of a\* 11.7 / b\* 30.2, and a\* 10.9 / b\* 29.9 after correcting — about ΔE 1. A grey card alone, under a warm
light, went from ΔE 12 out to about ΔE 1.

**Finding the areas.** "Find the areas" marks the board, and the chart when it can read one. The chart suggestion
checks itself: it fits the 24 patches and only offers the box when the fit is good, so a mis-detected chart is never
handed over as a confident reading. Both boxes can be dragged, and often the chart has to be.

## What it is not

- **It is not a trained model.** There is no neural network and nothing is extrapolated. Every answer traces to samples
  someone measured; with no samples for a formula the app says exactly that instead of guessing.
- **It cannot invent a formula.** It recommends a formula that has been recorded, and a strength between two that were
  measured. Composing a new recipe for a colour nobody has mixed — pages 8-9 of the requirements — needs the sample
  library first, and then a fit per colorant rather than per formula.
- **There is no camera integration.** You upload a photograph from any camera; the app does not drive a camera, enforce
  the capture conditions of section 1, or read RAW files.
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
| ColorChecker reference values and the fit | `backend/FinishGenius.Api/Services/ColorChecker.cs` |
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
