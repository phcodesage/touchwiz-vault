using System;
using System.Reflection;

// Exercise callback ordering without an Android device or paid ad requests.
class Purchase {
    public int Progress;
    public int Rewards;
    public void Buy(int required) {
        if (!RevivalAdGate.Capture(this, typeof(Purchase).GetMethod("Buy"), new object[] { required })) return;
        if (!RevivalAdGate.IsReplaying) { RevivalAdGate.Begin("test"); return; }
        Progress++;
        if (Progress == required) { Progress = 0; Rewards++; }
    }
}
class Program {
    static int checks;
    static void Check(bool condition, string message) {
        if (!condition) throw new Exception(message);
        checks++;
    }
    static void Main() {
        var purchase = new Purchase();
        var other = new Purchase();
        purchase.Buy(2);
        var gate = (RevivalAdGate)UnityEngine.GameObject.LastComponent;
        Check(purchase.Progress == 0 && purchase.Rewards == 0, "Tap granted progress before completion");
        other.Buy(1);
        gate.OnAdResult("completed");
        Check(purchase.Progress == 1 && other.Rewards == 0, "Overlapping tap replaced the pending purchase");
        gate.OnAdResult("completed");
        Check(purchase.Progress == 1, "Duplicate completion granted progress twice");
        purchase.Buy(2);
        gate.OnAdResult("skipped");
        Check(purchase.Progress == 1 && purchase.Rewards == 0, "Skipped ad granted reward");
        purchase.Buy(2);
        gate.OnAdResult("failed");
        Check(purchase.Progress == 1 && purchase.Rewards == 0, "Failed ad granted reward");
        purchase.Buy(2);
        gate.OnAdResult("completed");
        Check(purchase.Progress == 0 && purchase.Rewards == 1, "Completed ad did not grant earned reward");
        Check(!RevivalAdGate.IsReplaying, "Replay flag remained set");
        other.Buy(1);
        gate.OnAdResult("completed");
        Check(other.Rewards == 1, "Gate did not accept next purchase");
        UnityEngine.AndroidJavaClass.Fail = true;
        other.Buy(1);
        Check(other.Rewards == 1, "JNI failure granted a reward");
        UnityEngine.AndroidJavaClass.Fail = false;
        other.Buy(1);
        gate.OnAdResult("completed");
        Check(other.Rewards == 2, "JNI failure prevented retry");
        Console.WriteLine("PASS: " + checks + " reward callback checks");
    }
}
namespace UnityEngine {
    public class Object { public static void DontDestroyOnLoad(Object o) {} }
    public class MonoBehaviour : Object {}
    public class GameObject : Object {
        public static object LastComponent;
        public GameObject(string name) {}
        public object AddComponent(Type type) { return LastComponent = Activator.CreateInstance(type); }
    }
    public class AndroidJavaClass : IDisposable {
        public static bool Fail;
        public AndroidJavaClass(string name) {}
        public void CallStatic(string name, object[] args) { if (Fail) throw new Exception("Simulated JNI failure"); }
        public void Dispose() {}
    }
    public class Debug { public static void Log(object o) {} public static void LogError(object o) {} }
}
