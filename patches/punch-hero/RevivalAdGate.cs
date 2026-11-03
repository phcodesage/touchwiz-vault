using System;
using System.Reflection;
using UnityEngine;

// Built against the game's own mscorlib and UnityEngine, never the host .NET runtime.
public class RevivalAdGate : MonoBehaviour {
    private static RevivalAdGate instance;
    private static object target;
    private static MethodBase action;
    private static object[] arguments;
    private static bool busy;
    public static bool IsReplaying;

    public static bool Capture(object receiver, MethodBase method, object[] args) {
        if (IsReplaying) return true;
        if (busy) return false;
        target = receiver;
        action = method;
        arguments = args;
        return true;
    }

    public static void Begin(string description) {
        if (busy || IsReplaying || action == null) return;
        if (instance == null) {
            GameObject host = new GameObject("RevivalAdGate");
            UnityEngine.Object.DontDestroyOnLoad(host);
            instance = (RevivalAdGate)host.AddComponent(typeof(RevivalAdGate));
        }
        busy = true;
        Debug.Log("RevivalAdGate: waiting for completed rewarded ad");
        try {
            using (AndroidJavaClass bridge = new AndroidJavaClass("com.touchwizvault.ads.UnityLevelPlayBridge")) {
                bridge.CallStatic("showRewardedAd", new object[] { description });
            }
        } catch (Exception e) {
            Debug.LogError("RevivalAdGate: could not request ad: " + e);
            instance.OnAdResult("failed");
        }
    }

    public void OnAdResult(string result) {
        if (!busy) return;
        object receiver = target;
        MethodBase method = action;
        object[] args = arguments;
        action = null;
        target = null;
        arguments = null;
        if (result != "completed") {
            busy = false;
            Debug.Log("RevivalAdGate: " + result + "; no reward or progress granted");
            return;
        }
        IsReplaying = true;
        try {
            Debug.Log("RevivalAdGate: completed ad; applying " + method.Name);
            method.Invoke(receiver, args);
        } catch (Exception e) {
            Debug.LogError("RevivalAdGate: reward failed: " + e);
        } finally {
            IsReplaying = false;
            busy = false;
        }
    }
}
