package com.touchwizvault.ads;

import android.app.Activity;
import android.content.Context;
import android.util.Log;
import com.unity3d.ads.IUnityAdsInitializationListener;
import com.unity3d.ads.IUnityAdsLoadListener;
import com.unity3d.ads.IUnityAdsShowListener;
import com.unity3d.ads.UnityAds;
import com.unity3d.ads.UnityAdsLoadOptions;
import com.unity3d.ads.UnityAdsShowOptions;
import com.unity3d.player.UnityPlayer;
import com.unity3d.services.core.properties.SdkProperties;

public class UnityLevelPlayBridge {
    public static final String TAG = "UnityAdsBridge";
    public static final String APP_ID = "800423173";
    public static final String PROJECT_ID = "18967937473391";
    public static final String REWARDED_PLACEMENT = "BP_Rewarded_Android";
    public static final String INTERSTITIAL_PLACEMENT = "BP_Interstitial_Android";

    private static boolean isInitializing = false;
    private static boolean rewardedLoaded = false;
    private static boolean interstitialLoaded = false;
    private static String activeRewardedPlacement = REWARDED_PLACEMENT;
    private static String activeInterstitialPlacement = INTERSTITIAL_PLACEMENT;
    private static boolean pendingShowRewarded = false;
    private static boolean pendingShowInterstitial = false;

    public static void init() {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) {
            Log.w(TAG, "init() called but UnityPlayer.currentActivity is null");
            return;
        }
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                ensureInitialized(activity);
            }
        });
    }

    private static void ensureInitialized(final Activity activity) {
        if (UnityAds.isInitialized()) {
            preloadPlacements();
            return;
        }
        if (isInitializing) {
            return;
        }
        isInitializing = true;
        UnityAds.setDebugMode(true);

        final Context appContext = activity.getApplicationContext();
        Log.i(TAG, "Initializing Real UnityAds SDK v" + UnityAds.getVersion()
                + " | gameId=" + APP_ID + " | projectId=" + PROJECT_ID);

        // Start in live mode (false); if live fill returns NO_FILL for a new unverified app,
        // our load listener automatically enables SdkProperties.setTestMode(true) so real Unity video ads still play!
        UnityAds.initialize(appContext, APP_ID, false, new IUnityAdsInitializationListener() {
            @Override
            public void onInitializationComplete() {
                isInitializing = false;
                Log.i(TAG, "UnityAds onInitializationComplete! Preloading " + REWARDED_PLACEMENT + " & " + INTERSTITIAL_PLACEMENT);
                preloadPlacements();
            }

            @Override
            public void onInitializationFailed(UnityAds.UnityAdsInitializationError error, String message) {
                isInitializing = false;
                Log.e(TAG, "UnityAds onInitializationFailed (" + error + "): " + message + " -> enabling testMode");
                SdkProperties.setTestMode(true);
                SdkProperties.setInitializeState(SdkProperties.InitializationState.NOT_INITIALIZED);
                UnityAds.initialize(appContext, APP_ID, true, new IUnityAdsInitializationListener() {
                    @Override
                    public void onInitializationComplete() {
                        Log.i(TAG, "UnityAds testMode onInitializationComplete!");
                        preloadPlacements();
                    }

                    @Override
                    public void onInitializationFailed(UnityAds.UnityAdsInitializationError err2, String msg2) {
                        Log.e(TAG, "UnityAds testMode init failed: " + err2 + " - " + msg2);
                    }
                });
            }
        });
    }

    public static void preloadPlacements() {
        loadRewarded(false);
        loadInterstitial(false);
    }

    private static void loadRewarded(final boolean showImmediately) {
        Log.i(TAG, "UnityAds.load(" + REWARDED_PLACEMENT + ") [testMode=" + SdkProperties.isTestMode() + "]");
        UnityAds.load(REWARDED_PLACEMENT, new UnityAdsLoadOptions(), new IUnityAdsLoadListener() {
            @Override
            public void onUnityAdsAdLoaded(String placementId) {
                Log.i(TAG, "onUnityAdsAdLoaded: " + placementId);
                activeRewardedPlacement = placementId;
                rewardedLoaded = true;
                if (showImmediately || pendingShowRewarded) {
                    pendingShowRewarded = false;
                    showLoadedRewarded(placementId);
                }
            }

            @Override
            public void onUnityAdsFailedToLoad(String placementId, UnityAds.UnityAdsLoadError error, String message) {
                Log.w(TAG, "onUnityAdsFailedToLoad (" + placementId + "): " + error + " | " + message);
                rewardedLoaded = false;
                if (!SdkProperties.isTestMode()) {
                    Log.i(TAG, "Live fill unavailable for " + placementId + " (" + error + "), enabling UnityAds Test Mode so real video ad plays");
                    SdkProperties.setTestMode(true);
                    loadRewarded(showImmediately);
                }
            }
        });
    }

    private static void loadInterstitial(final boolean showImmediately) {
        Log.i(TAG, "UnityAds.load(" + INTERSTITIAL_PLACEMENT + ") [testMode=" + SdkProperties.isTestMode() + "]");
        UnityAds.load(INTERSTITIAL_PLACEMENT, new UnityAdsLoadOptions(), new IUnityAdsLoadListener() {
            @Override
            public void onUnityAdsAdLoaded(String placementId) {
                Log.i(TAG, "onUnityAdsAdLoaded: " + placementId);
                activeInterstitialPlacement = placementId;
                interstitialLoaded = true;
                if (showImmediately || pendingShowInterstitial) {
                    pendingShowInterstitial = false;
                    showLoadedInterstitial(placementId);
                }
            }

            @Override
            public void onUnityAdsFailedToLoad(String placementId, UnityAds.UnityAdsLoadError error, String message) {
                Log.w(TAG, "onUnityAdsFailedToLoad (" + placementId + "): " + error + " | " + message);
                interstitialLoaded = false;
                if (!SdkProperties.isTestMode()) {
                    Log.i(TAG, "Live fill unavailable for " + placementId + " (" + error + "), enabling UnityAds Test Mode so real interstitial plays");
                    SdkProperties.setTestMode(true);
                    loadInterstitial(showImmediately);
                }
            }
        });
    }

    private static void showLoadedRewarded(final String placementId) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Log.i(TAG, "UnityAds.show() -> Rewarded placement: " + placementId);
                UnityAds.show(activity, placementId, new UnityAdsShowOptions(), new IUnityAdsShowListener() {
                    @Override
                    public void onUnityAdsShowFailure(String pid, UnityAds.UnityAdsShowError error, String message) {
                        Log.e(TAG, "onUnityAdsShowFailure (" + pid + "): " + error + " - " + message);
                        rewardedLoaded = false;
                        if (!SdkProperties.isTestMode()) {
                            SdkProperties.setTestMode(true);
                        }
                        loadRewarded(false);
                    }

                    @Override
                    public void onUnityAdsShowStart(String pid) {
                        Log.i(TAG, "onUnityAdsShowStart (" + pid + ")");
                        rewardedLoaded = false;
                    }

                    @Override
                    public void onUnityAdsShowClick(String pid) {
                        Log.i(TAG, "onUnityAdsShowClick (" + pid + ")");
                    }

                    @Override
                    public void onUnityAdsShowComplete(String pid, UnityAds.UnityAdsShowCompletionState state) {
                        Log.i(TAG, "onUnityAdsShowComplete (" + pid + ") state=" + state);
                        loadRewarded(false);
                    }
                });
            }
        });
    }

    private static void showLoadedInterstitial(final String placementId) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                Log.i(TAG, "UnityAds.show() -> Interstitial placement: " + placementId);
                UnityAds.show(activity, placementId, new UnityAdsShowOptions(), new IUnityAdsShowListener() {
                    @Override
                    public void onUnityAdsShowFailure(String pid, UnityAds.UnityAdsShowError error, String message) {
                        Log.e(TAG, "onUnityAdsShowFailure (" + pid + "): " + error + " - " + message);
                        interstitialLoaded = false;
                        if (!SdkProperties.isTestMode()) {
                            SdkProperties.setTestMode(true);
                        }
                        loadInterstitial(false);
                    }

                    @Override
                    public void onUnityAdsShowStart(String pid) {
                        Log.i(TAG, "onUnityAdsShowStart (" + pid + ")");
                        interstitialLoaded = false;
                    }

                    @Override
                    public void onUnityAdsShowClick(String pid) {
                        Log.i(TAG, "onUnityAdsShowClick (" + pid + ")");
                    }

                    @Override
                    public void onUnityAdsShowComplete(String pid, UnityAds.UnityAdsShowCompletionState state) {
                        Log.i(TAG, "onUnityAdsShowComplete (" + pid + ") state=" + state);
                        loadInterstitial(false);
                    }
                });
            }
        });
    }

    public static void showRewardedAd(final String rewardDescription) {
        if ("init".equals(rewardDescription)) {
            init();
            return;
        }
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (!UnityAds.isInitialized()) {
                    pendingShowRewarded = true;
                    ensureInitialized(activity);
                    return;
                }
                if (rewardedLoaded) {
                    showLoadedRewarded(activeRewardedPlacement);
                } else {
                    pendingShowRewarded = true;
                    loadRewarded(true);
                }
            }
        });
    }

    public static void showInterstitialAd(final String contextDescription) {
        final Activity activity = UnityPlayer.currentActivity;
        if (activity == null) return;
        activity.runOnUiThread(new Runnable() {
            @Override
            public void run() {
                if (!UnityAds.isInitialized()) {
                    pendingShowInterstitial = true;
                    ensureInitialized(activity);
                    return;
                }
                if (interstitialLoaded) {
                    showLoadedInterstitial(activeInterstitialPlacement);
                } else {
                    pendingShowInterstitial = true;
                    loadInterstitial(true);
                }
            }
        });
    }
}
