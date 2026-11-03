package com.touchwizvault.ads;

import android.app.Activity;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.widget.Toast;
import com.unity3d.ads.*;
import com.unity3d.player.UnityPlayer;

public class UnityLevelPlayBridge {
    public static final String TAG = "UnityAdsBridge";
    public static final String APP_ID = "800423173";
    public static final String PROJECT_ID = "18967937473391";
    public static final String REWARDED_PLACEMENT = "BP_Rewarded_Android";
    public static final String INTERSTITIAL_PLACEMENT = "BP_Interstitial_Android";
    private static final Handler main = new Handler(Looper.getMainLooper());
    private static boolean initializing, rewardedLoaded, interstitialLoaded;
    private static boolean rewardedLoading, interstitialLoading, pendingRewarded;
    private static boolean showingRewarded, showingInterstitial;
    private static final Runnable loadTimeout = new Runnable() {
        public void run() {
            if (pendingRewarded && !showingRewarded) finishRewarded("failed", "Ad loading timed out. Please try again.");
        }
    };

    private static Activity activity() { return UnityPlayer.currentActivity; }
    private static void toast(String message) {
        Activity a = activity();
        if (a != null) Toast.makeText(a, message, Toast.LENGTH_SHORT).show();
    }
    private static void finishRewarded(String result, String message) {
        main.removeCallbacks(loadTimeout);
        if (!pendingRewarded) return;
        pendingRewarded = false;
        showingRewarded = false;
        Log.i(TAG, "Reward result: " + result);
        UnityPlayer.UnitySendMessage("RevivalAdGate", "OnAdResult", result);
        if (message != null) toast(message);
    }
    public static void init() { main.post(new Runnable() { public void run() { ensureInitialized(); } }); }
    private static void ensureInitialized() {
        if (activity() == null) {
            finishRewarded("failed", null);
            return;
        }
        if (UnityAds.isInitialized()) { preloadPlacements(); return; }
        if (initializing) return;
        initializing = true;
        UnityAds.setDebugMode(true);
        Log.i(TAG, "Initializing Unity Ads " + UnityAds.getVersion() + " gameId=" + APP_ID + " testMode=false");
        UnityAds.initialize(activity().getApplicationContext(), APP_ID, false, new IUnityAdsInitializationListener() {
            public void onInitializationComplete() { main.post(new Runnable() { public void run() {
                initializing = false;
                Log.i(TAG, "onInitializationComplete");
                preloadPlacements();
            } }); }
            public void onInitializationFailed(final UnityAds.UnityAdsInitializationError error, final String message) {
                main.post(new Runnable() { public void run() {
                    initializing = false;
                    Log.e(TAG, "Initialization failed: " + error + " " + message);
                    finishRewarded("failed", "Ads are unavailable. Please try again later.");
                } });
            }
        });
    }
    public static void preloadPlacements() { loadRewarded(); loadInterstitial(); }
    private static void loadRewarded() {
        if (rewardedLoaded) { if (pendingRewarded) showLoadedRewarded(); return; }
        if (rewardedLoading) return;
        rewardedLoading = true;
        UnityAds.load(REWARDED_PLACEMENT, new UnityAdsLoadOptions(), new IUnityAdsLoadListener() {
            public void onUnityAdsAdLoaded(String id) { main.post(new Runnable() { public void run() {
                rewardedLoading = false; rewardedLoaded = true;
                Log.i(TAG, "onUnityAdsAdLoaded: " + REWARDED_PLACEMENT);
                if (pendingRewarded) showLoadedRewarded();
            } }); }
            public void onUnityAdsFailedToLoad(String id, final UnityAds.UnityAdsLoadError error, final String message) {
                main.post(new Runnable() { public void run() {
                    rewardedLoading = false; rewardedLoaded = false;
                    Log.w(TAG, "Rewarded load failed: " + error + " " + message);
                    finishRewarded("failed", "No ad available. Please try again later.");
                } });
            }
        });
    }
    private static void loadInterstitial() {
        if (interstitialLoaded || interstitialLoading) return;
        interstitialLoading = true;
        UnityAds.load(INTERSTITIAL_PLACEMENT, new UnityAdsLoadOptions(), new IUnityAdsLoadListener() {
            public void onUnityAdsAdLoaded(String id) { main.post(new Runnable() { public void run() {
                interstitialLoading = false; interstitialLoaded = true;
                Log.i(TAG, "onUnityAdsAdLoaded: " + INTERSTITIAL_PLACEMENT);
            } }); }
            public void onUnityAdsFailedToLoad(String id, final UnityAds.UnityAdsLoadError error, final String message) {
                main.post(new Runnable() { public void run() {
                    interstitialLoading = false; interstitialLoaded = false;
                    Log.w(TAG, "Interstitial load failed: " + error + " " + message);
                } });
            }
        });
    }
    public static void showRewardedAd(final String description) {
        if ("init".equals(description)) { init(); return; }
        main.post(new Runnable() { public void run() {
            if (pendingRewarded) return;
            pendingRewarded = true;
            main.postDelayed(loadTimeout, 60000);
            if (activity() == null || showingInterstitial) {
                finishRewarded("failed", "Please try again."); return;
            }
            if (!UnityAds.isInitialized()) { toast("Loading ad..."); ensureInitialized(); }
            else if (rewardedLoaded) showLoadedRewarded();
            else { toast("Loading ad..."); loadRewarded(); }
        } });
    }
    private static void showLoadedRewarded() {
        if (showingRewarded || !pendingRewarded) return;
        if (activity() == null) { finishRewarded("failed", null); return; }
        showingRewarded = true;
        rewardedLoaded = false;
        main.removeCallbacks(loadTimeout);
        Log.i(TAG, "UnityAds.show -> " + REWARDED_PLACEMENT);
        UnityAds.show(activity(), REWARDED_PLACEMENT, new UnityAdsShowOptions(), new IUnityAdsShowListener() {
            public void onUnityAdsShowFailure(String id, final UnityAds.UnityAdsShowError error, final String message) {
                main.post(new Runnable() { public void run() {
                    Log.e(TAG, "Rewarded show failed: " + error + " " + message);
                    finishRewarded("failed", "Ad could not play. Please try again."); loadRewarded();
                } });
            }
            public void onUnityAdsShowStart(String id) { Log.i(TAG, "onUnityAdsShowStart: " + id); }
            public void onUnityAdsShowClick(String id) { Log.i(TAG, "onUnityAdsShowClick: " + id); }
            public void onUnityAdsShowComplete(String id, final UnityAds.UnityAdsShowCompletionState state) {
                main.post(new Runnable() { public void run() {
                    Log.i(TAG, "onUnityAdsShowComplete: " + state);
                    finishRewarded(state == UnityAds.UnityAdsShowCompletionState.COMPLETED ? "completed" : "skipped",
                        state == UnityAds.UnityAdsShowCompletionState.COMPLETED ? null : "Ad skipped. No reward granted.");
                    loadRewarded();
                } });
            }
        });
    }
    public static void showInterstitialAd(final String description) {
        main.post(new Runnable() { public void run() {
            // An interstitial is shown only at this requested game transition, never after a delayed load.
            if (activity() == null || pendingRewarded || showingInterstitial) return;
            if (!UnityAds.isInitialized()) { ensureInitialized(); return; }
            if (!interstitialLoaded) { loadInterstitial(); return; }
            interstitialLoaded = false; showingInterstitial = true;
            Log.i(TAG, "UnityAds.show -> " + INTERSTITIAL_PLACEMENT);
            UnityAds.show(activity(), INTERSTITIAL_PLACEMENT, new UnityAdsShowOptions(), new IUnityAdsShowListener() {
                public void onUnityAdsShowFailure(String id, UnityAds.UnityAdsShowError error, String message) {
                    main.post(new Runnable() { public void run() { showingInterstitial = false; loadInterstitial(); } });
                    Log.e(TAG, "Interstitial show failed: " + error + " " + message);
                }
                public void onUnityAdsShowStart(String id) { Log.i(TAG, "onUnityAdsShowStart: " + id); }
                public void onUnityAdsShowClick(String id) {}
                public void onUnityAdsShowComplete(String id, UnityAds.UnityAdsShowCompletionState state) {
                    main.post(new Runnable() { public void run() { showingInterstitial = false; loadInterstitial(); } });
                }
            });
        } });
    }
}
