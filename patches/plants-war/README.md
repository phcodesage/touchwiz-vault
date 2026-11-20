# Plants War 1.5.0 offline startup

The original game reaches its local title screen but is blocked by `GvTorchwood`. This Gamevil component downloads/caches a list of packages, checks installed/running apps against it, and shows a non-cancelable "Network Error" when the list is unavailable. It is not a download of level data: the maps, units, stages, graphics, sounds, and native engine are already present in the APK.

The patched build installs as **Plants War Offline**, package `com.gamevil.plantswar.offline`, alongside the untouched original. It targets API 24, has version code 153, and is signed with the local Android debug key. It cannot update the original GAMEVIL-signed package or share its private saves.

Changes:

- Replace `GvActivity.doTorchwood()` and `doJellyBeen()` with clearing `pend` and returning, preserving the normal game resume lifecycle.
- Disable the retired C2DM registration/unregistration service calls.
- Use an explicit intent to start the game's original activity inside the offline package, carrying the original profile bundle. This avoids routing a deep link into the separately installed original game.
- Report billing-unavailable through the existing setup listener, instead of binding an implicit legacy Play Billing service. Purchase requests return through the existing failure callback; no purchase success or currency is fabricated.
- Reuse `punch-hero/legacy_profile.py` to remove obsolete phone/SIM/device/account reads.
- Remove personal-data, shared-storage, and retired push permissions. FileManager already saves in the app's own private directory.

Only `AndroidManifest.xml` and `classes.dex` are modified. Native libraries, level data, and all other game assets remain byte-for-byte identical to the verified source APK. Old non-blocking marketing/social SDK code remains in the archive; this patch does not claim to remove every outbound network request.

## Build and verification

The source APK must have SHA-256 `15e9684e2389890fcf99ea3673be9342f85b98a025255d47fdd2b975fbc5d318`. It was downloaded from APKPure and matches the SHA-256 and certificate digest listed independently by Uptodown for the archived 1.5.0 release.

```sh
python3 build_offline.py --input ~/Downloads/Plants_War_v1.5.0_original.apk --output ~/Downloads/Plants_War_v1.5.0_offline.apk
python3 verify_offline.py ~/Downloads/Plants_War_v1.5.0_original.apk ~/Downloads/Plants_War_v1.5.0_offline.apk
```

Requires Python 3, Android SDK build-tools 36.0.0 at the default macOS path, and the local debug signing key. The profile compatibility helper is shared with the sibling Punch Hero patch directory.

Device verification: installed without the low-target bypass on Samsung SM-G990E / Android 16. Reached the story intro, entered the 3D tutorial, resumed from pause, and observed the character fighting an enemy with health/mana and potion controls visible. The blocking network dialog was absent and no AndroidRuntime crash was reported in the successful process. Full campaign, shop behavior, save restoration after restart, and a launch with all networking disabled have not been exercised.

## Later: rewarded ads for power-ups

No new ad SDK, account configuration, ad UI, or reward hook is added in this build.

Relevant findings:

- `com.gamevil.plantswar.glo.GameActivity` is the activity hosting `GameGLSurfaceView`. The game uses its native `libGame.so` engine, not Unity's managed scripting runtime.
- The native engine contains HP/MP potion resource identifiers (`hp_potion`, `mp_potion`, `use_hp_potion`, `use_mp_potion`), and the tutorial HUD shows red and blue potion inventories.
- `com.ideaBox.Library.GVManager` and `InAppPurchaseManager` bridge Java/native code. Existing purchase-success callbacks are not a dedicated potion grant interface; do not invoke them as a substitute for a completed-ad reward.
- The archive has Tapjoy video callbacks, including `GameActivity.videoComplete()`. A legacy callback or a visible video is not evidence of a modern monetizable integration.

Before adding ads, trace the native potion inventory and save path and implement an explicit, bounded power-up grant. Connect the chosen ad network's supported SDK/adapter to placements actually configured for this app. Pause gameplay during ad display; grant a reward exactly once on the provider's completed rewarded-ad callback; resume on completion, skip, or failure. No timer, tap, request, or load callback should grant progress. Use test devices for validation, and verify app-specific reporting before treating impressions as paid revenue. Do not rewrite bidding configuration responses into waterfall placements.
