# Punch Hero revived ads

The previous patch imported `System.String`, `System.Int32`, and `System.Exception` from the patcher's .NET 10 runtime. On the phone, the old Unity Mono runtime failed to resolve `System.Private.CoreLib, Version=10.0.0.0` in the shop's price updates and purchase handlers, even though the Android SDK had loaded ads.

`Program.cs` now imports those members from the game's own `mscorlib`. It also checks that host runtime assembly references have not leaked into the patched DLL and expands short IL branches before writing the result.

`RevivalAdGate.cs` is compiled with `/nostdlib` against the original game assemblies. A shop action is captured with its exact method and arguments. The initial call can check the wallet and show existing progress, but cannot count an ad or grant an ad reward. The bridge sends `completed`, `skipped`, or `failed` to the persistent gate GameObject. Only completion replays the action with permission to advance progress or grant the earned item. The gate rejects overlapping purchases and ignores duplicate results. Capsule bonuses and boosters use the same completion requirement. Pack grants no longer use a 3.1-second timer.

The Java bridge initializes in live mode, serializes SDK operations onto the Android main thread, preloads ads, and reports loading/showing failures without granting progress. It does not switch live failures to test ads. Interstitials are displayed only at the transition that requested them, with no deferred popup after loading.

## Build

Requires the existing revival workspace: original managed assembly backup, game runtime DLLs, iltool project with Mono.Cecil and ICSharpCode.Decompiler, original Android classes.dex, and extracted Unity Ads SDK including its `libs` directory. The input APK must already contain the revival's native crash fixes and SDK manifest entries. Existing SDK response patches are retained from `unity_sdk/classes.jar`.

```sh
python3 build_ads.py --workspace /path/to/ph_inspect --input /path/to/existing-revival.apk --output /path/to/Punch_Hero_v1.3.8_ads_fixed.apk
```

The default signing key is the Android debug key used by the existing revival APK. `--keystore` overrides the path; this script expects the standard debug-key password. The script preserves the input ZIP entry metadata for unchanged assets, replaces the managed/Java patches, aligns the APK, and verifies its signature. It does not upload or publish a release.

## Tests

```sh
dotnet run --project tests/GateTests.csproj
```

Tests cover no credit on tap, overlapping taps, duplicate callbacks, skipped ads, failed ads, completion reaching the required watch count, next purchases, and JNI failure/retry. These tests use Unity stubs; actual Unity/Android compatibility requires device testing.

## Device verification (2026-11-04)

Installed as an update on Samsung SM-G990E / Android 16; existing currency and player level remained intact. Both live placements initialized and loaded. A gold-pack tap opened a rewarded video. After the SDK reported `onUnityAdsShowComplete: COMPLETED`, the Unity gate replayed `Purchase`, and the game's popup showed `GOLD 5,000 — AD PROGRESS: 1/3 ADS` (previously 0/3). No missing-runtime or reward callback exceptions appeared. A failed initialization in an earlier build also returned through the failure callback with no progress granted. All ten isolated reward callback checks passed; APK signatures and ZIP integrity verified. Full pack payout and other shop categories were not individually exercised on the phone.

## Normal installer candidate (not yet device verified)

The verified v1.0.1 APK still targets API 14 and was installed using ADB's low-target override. It therefore does not pass the normal Android 14–16 package installer. The live site now states this requirement explicitly.

`make_installable.py` prepares a separate candidate with target API 24 and version code 1382, signed with the existing revival key. A manifest-only change is insufficient: Unity 3's native engine contains text relocations, rejected for targets >=23. `legacy_unity.py` adds a DT_INIT trampoline in unused file padding. The executable segment is writable while the loader applies its original relocations; the trampoline flushes the ARM instruction cache and restores read/execute permissions before the engine's original constructors run. This supports targets 24 and 25 only; it does not modernize Unity's native implementation or add 64-bit support.

```sh
python3 make_installable.py --input /path/to/Punch_Hero_v1.3.8_ads_fixed.apk --output /path/to/Punch_Hero_v1.3.8_installable.apk
# Optional emulator check: requires the Python unicorn package.
python3 tests/test_native.py /path/to/Punch_Hero_v1.3.8_ads_fixed.apk /path/to/Punch_Hero_v1.3.8_installable.apk
```

Signatures and manifest fields verified. ARM emulation passed at three load addresses, checking cache flush, RX restoration, register/stack preservation, and unchanged game assets/relocation records. The physical phone disconnected before testing this candidate. Do not treat these checks as evidence of normal installation, game startup, permissions compatibility, or ad playback on a device. The candidate has not replaced the verified release.
