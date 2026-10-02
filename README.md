# Tracker of Time V2

Tracker of Time V2 combines the OoT Randomizer workspace, embedded Mupen64Plus gameplay and live tracker UI in one Windows application.

## Build

1. Open `TrackerOfTime.V2.sln` in Visual Studio.
2. Select `Release`.
3. Build the solution.
4. Set `TrackerOfTime.V2.M8.5.Desktop` as the startup project and start it.

For end-user distribution, publish the Desktop project as `Release | x64`, `win-x64`, self-contained. The publish step stages the required OoTR files, the self-contained GameCore host and the prebuilt x64 input provider next to the application so the result can run outside the source checkout.

The desktop project builds the required GameCore host and native input component automatically.

## Repository layout

- `src/` – application, integration, tracker, randomizer and GameCore source.
- `third_party/OoTR/` – bundled OoT Randomizer source and its upstream license.

Development milestone gates, frozen-baseline copies, smoke projects, verification logs and historical solutions are intentionally not part of the release repository.

## Third-party software

OoT Randomizer is bundled under its upstream license in `third_party/OoTR/LICENSE`. Mupen64Plus runtime components are provisioned by the application from their pinned upstream release. See `THIRD_PARTY_NOTICES.md` for attribution and distribution notes.
