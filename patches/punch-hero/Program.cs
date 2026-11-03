using System;
using System.IO;
using System.Linq;
using Mono.Cecil;
using Mono.Cecil.Cil;
using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.TypeSystem;

class Program {
    static void EmitGetMember(ILProcessor il, TypeDefinition t, string name) {
        for (var cur = t; cur != null; cur = cur.BaseType?.Resolve()) {
            var p = cur.Properties.FirstOrDefault(x => x.Name == name);
            if (p != null && p.GetMethod != null) {
                il.Append(il.Create(OpCodes.Callvirt, p.GetMethod));
                return;
            }
            var f = cur.Fields.FirstOrDefault(x => x.Name == name || x.Name == "_" + name);
            if (f != null) {
                il.Append(il.Create(OpCodes.Ldfld, f));
                return;
            }
        }
        throw new Exception($"Member {name} not found on {t.FullName}");
    }

    static void Main(string[] args) {
        string managedDir = args.Length > 0 ? args[0] : throw new ArgumentException("Pass the extracted game Managed directory");
        string dllPath = Path.Combine(managedDir, "Assembly-CSharp.dll");
        string backupPath = Path.Combine(managedDir, "Assembly-CSharp.dll.bak");
        if (!File.Exists(backupPath)) {
            File.Copy(dllPath, backupPath);
        } else {
            File.Copy(backupPath, dllPath, true);
        }

        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(managedDir);
        var readerParams = new ReaderParameters { AssemblyResolver = resolver, ReadWrite = false };
        var asm = AssemblyDefinition.ReadAssembly(dllPath, readerParams);
        var mod = asm.MainModule;

        var stringType = mod.TypeSystem.String;
        var voidType = mod.TypeSystem.Void;
        var intType = mod.TypeSystem.Int32;
        var boolType = mod.TypeSystem.Boolean;
        var objectType = mod.TypeSystem.Object;

        // UnityEngine AndroidJavaClass & AndroidJavaObject for Unity Ads / LevelPlay bridge
        var unityAsm = resolver.Resolve(new AssemblyNameReference("UnityEngine", new Version(0, 0, 0, 0)));
        var androidJavaClassDef = unityAsm.MainModule.GetType("UnityEngine.AndroidJavaClass");
        var androidJavaObjDef = unityAsm.MainModule.GetType("UnityEngine.AndroidJavaObject");
        var ajcCtor = mod.ImportReference(androidJavaClassDef.Methods.First(m => m.IsConstructor && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.FullName == "System.String"));
        var ajoCallStatic = mod.ImportReference(androidJavaObjDef.Methods.First(m => m.Name == "CallStatic" && !m.HasGenericParameters && m.Parameters.Count == 2));

        // Core types in Assembly-CSharp
        var gameEventHandlerType = mod.GetType("GameEventHandler");
        var shopItemInfoType = mod.GetType("ShopItemInfo");
        var setItemInfoType = mod.GetType("SetItemInfo");
        var skillAttrType = mod.GetType("SkillAttribute");
        var uiShopSkillDescType = mod.GetType("UIShopSkillDescUpdater");
        var uiBuyCoinBtnType = mod.GetType("UIBuyCoinButton");
        var purchaseMgrType = mod.GetType("PurchaseManager");
        var purchaseItemDataType = mod.GetType("PurchaseItemData");
        var purchaseGameMoneyType = mod.GetType("PurchaseGameMoney");
        var guiEventHandlerType = mod.GetType("GUIEventHandler");
        var uiMessageBoxType = mod.GetType("UIMessageBox");
        var uiShopItemPanelType = mod.GetType("UIShopItemPanel");
        var uiShopItemType = mod.GetType("UIShopItem");
        var gameManagerType = mod.GetType("GameManager");
        var saveDataType = mod.GetType("SaveData");
        var statUpgradeInfoType = mod.GetType("StatUpgradeInfo");
        var shopDataType = mod.GetType("ShopData");
        var athleteType = mod.GetType("Athlete");
        var playerPrefsXType = mod.GetType("PlayerPrefsX");

        // Keep PurchaseItemData.eventHandler untouched (protected) so Unity's C++ binary serializer never shifts offsets!
        var pidEventHandlerField = purchaseItemDataType.Fields.First(f => f.Name == "eventHandler");

        // Unity MonoBehaviour methods & String/Int32 helpers
        var monoBehaviourType = guiEventHandlerType.BaseType.Resolve();
        var invokeMethod = mod.ImportReference(monoBehaviourType.Methods.First(m => m.Name == "Invoke" && m.Parameters.Count == 2));
        var cancelInvokeMethod = mod.ImportReference(monoBehaviourType.Methods.First(m => m.Name == "CancelInvoke" && m.Parameters.Count == 1));
        // Import runtime members from the game's .NET 2.0 profile, not the patcher's .NET 10.
        var core = resolver.Resolve((AssemblyNameReference)mod.TypeSystem.CoreLibrary);
        var legacyString = core.MainModule.GetType("System.String");
        MethodReference Concat(int count) => mod.ImportReference(legacyString.Methods.First(m => m.Name == "Concat" && m.Parameters.Count == count && m.Parameters.All(p => p.ParameterType.FullName == "System.String")));
        var strConcat2 = Concat(2);
        var strConcat3 = Concat(3);
        var strConcat4 = Concat(4);
        var intToString = mod.ImportReference(core.MainModule.GetType("System.Int32").Methods.First(m => m.Name == "ToString" && m.Parameters.Count == 0));

        var gateAsm = AssemblyDefinition.ReadAssembly(Path.Combine(managedDir, "RevivalAdGate.dll"), readerParams);
        var gate = gateAsm.MainModule.GetType("RevivalAdGate");
        var gateReplay = mod.ImportReference(gate.Fields.First(f => f.Name == "IsReplaying"));
        var gateBegin = mod.ImportReference(gate.Methods.First(m => m.Name == "Begin"));
        var gateCapture = mod.ImportReference(gate.Methods.First(m => m.Name == "Capture"));

        var gmInstanceField = gameManagerType.Fields.First(f => f.Name == "instance");
        var guiInstanceField = guiEventHandlerType.Fields.First(f => f.Name == "instance");
        var gmPlayerGetter = gameManagerType.Properties.First(p => p.Name == "player").GetMethod;
        var gmSkillMgrGetter = gameManagerType.Properties.First(p => p.Name == "skillManager").GetMethod;
        var gmAchMgrGetter = gameManagerType.Properties.First(p => p.Name == "achievementManager").GetMethod;
        var gmEventHandlerField = gameManagerType.Fields.First(f => f.Name == "eventHandler");
        var ppxInstanceGetter = playerPrefsXType.Properties.First(p => p.Name == "instance").GetMethod;
        var ppxSave = playerPrefsXType.Methods.First(m => m.Name == "Save");

        var getEventCount = saveDataType.Methods.First(m => m.Name == "GetEventCount");
        var setEventCount = saveDataType.Methods.First(m => m.Name == "SetEventCount");
        var getMoney = saveDataType.Methods.First(m => m.Name == "GetMoney" && m.Parameters.Count == 1);
        var setMoney = saveDataType.Methods.First(m => m.Name == "SetMoney" && m.Parameters.Count == 2);
        var getCoin = saveDataType.Methods.First(m => m.Name == "GetCoin");
        var setCoin = saveDataType.Methods.First(m => m.Name == "SetCoin");
        var getCoco = saveDataType.Methods.First(m => m.Name == "GetCoco");
        var setCoco = saveDataType.Methods.First(m => m.Name == "SetCoco");

        // 0. Add Cost-Aligned Multi-Ad Helper Methods to GUIEventHandler:
        //    - int CalcRequiredAds(bool isCoco, int amount)
        //    - int CalcRequiredAdsForUpgrade(int upgradeCount)
        //    - int CalcRequiredAdsForPack(PurchaseItemData item)
        //    - string FormatAdLabel(string key, int reqAds)
        //    - bool RecordAdWatchAndCheckReady(string key, int reqAds)
        //    - string GetSkillPriceString(SkillAttribute skill, int skillLevel)
        //    - string GetPackPriceString(PurchaseItemData item)

        var mCalcRequiredAds = new MethodDefinition("CalcRequiredAds", MethodAttributes.Public | MethodAttributes.Static, intType);
        mCalcRequiredAds.Parameters.Add(new ParameterDefinition("isCoco", ParameterAttributes.None, boolType));
        mCalcRequiredAds.Parameters.Add(new ParameterDefinition("amount", ParameterAttributes.None, intType));
        {
            var il = mCalcRequiredAds.Body.GetILProcessor();
            var lblGold = il.Create(OpCodes.Nop);
            var lblStar60 = il.Create(OpCodes.Nop);
            var lblStar120 = il.Create(OpCodes.Nop);
            var lblStar250 = il.Create(OpCodes.Nop);
            var lblStar500 = il.Create(OpCodes.Nop);
            var lblStar1500 = il.Create(OpCodes.Nop);
            var lblStar5000 = il.Create(OpCodes.Nop);
            var lblGold4000 = il.Create(OpCodes.Nop);
            var lblGold8000 = il.Create(OpCodes.Nop);
            var lblGold12000 = il.Create(OpCodes.Nop);
            var lblGold20000 = il.Create(OpCodes.Nop);
            var lblGold50000 = il.Create(OpCodes.Nop);

            // if (!isCoco) goto lblGold;
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brfalse_S, lblGold));

            // Star Cash (isCoco == true) scaling:
            // <= 25 -> 1 AD
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)25));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar60));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // <= 60 -> 2 ADS
            il.Append(lblStar60);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)60));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar120));
            il.Append(il.Create(OpCodes.Ldc_I4_2));
            il.Append(il.Create(OpCodes.Ret));

            // <= 120 -> 3 ADS
            il.Append(lblStar120);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)120));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar250));
            il.Append(il.Create(OpCodes.Ldc_I4_3));
            il.Append(il.Create(OpCodes.Ret));

            // <= 250 -> 5 ADS (200 Stars pack = 5 ADS)
            il.Append(lblStar250);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 250));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar500));
            il.Append(il.Create(OpCodes.Ldc_I4_5));
            il.Append(il.Create(OpCodes.Ret));

            // <= 500 -> 8 ADS (440 Stars pack = 8 ADS)
            il.Append(lblStar500);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 500));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar1500));
            il.Append(il.Create(OpCodes.Ldc_I4_8));
            il.Append(il.Create(OpCodes.Ret));

            // <= 1500 -> 15 ADS (1,200 Stars pack = 15 ADS)
            il.Append(lblStar1500);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 1500));
            il.Append(il.Create(OpCodes.Bgt_S, lblStar5000));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)15));
            il.Append(il.Create(OpCodes.Ret));

            // <= 5000 -> 25 ADS (2,600 Stars pack = 25 ADS)
            var lblStarMax = il.Create(OpCodes.Ldc_I4_S, (sbyte)40);
            il.Append(lblStar5000);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 5000));
            il.Append(il.Create(OpCodes.Bgt_S, lblStarMax));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)25));
            il.Append(il.Create(OpCodes.Ret));

            // > 5000 -> 40 ADS (7,000 / 15,000 Stars pack = 40 ADS)
            il.Append(lblStarMax);
            il.Append(il.Create(OpCodes.Ret));

            // Gold Coins (isCoco == false) scaling:
            // <= 1500 -> 1 AD
            il.Append(lblGold);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 1500));
            il.Append(il.Create(OpCodes.Bgt_S, lblGold4000));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // <= 4000 -> 2 ADS (3,500 Gold pack = 2 ADS)
            il.Append(lblGold4000);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 4000));
            il.Append(il.Create(OpCodes.Bgt_S, lblGold8000));
            il.Append(il.Create(OpCodes.Ldc_I4_2));
            il.Append(il.Create(OpCodes.Ret));

            // <= 8000 -> 3 ADS
            il.Append(lblGold8000);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 8000));
            il.Append(il.Create(OpCodes.Bgt_S, lblGold12000));
            il.Append(il.Create(OpCodes.Ldc_I4_3));
            il.Append(il.Create(OpCodes.Ret));

            // <= 12000 -> 4 ADS (10,000 Gold = 4 ADS)
            il.Append(lblGold12000);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 12000));
            il.Append(il.Create(OpCodes.Bgt_S, lblGold20000));
            il.Append(il.Create(OpCodes.Ldc_I4_4));
            il.Append(il.Create(OpCodes.Ret));

            // <= 20000 -> 5 ADS (15,000 Gold item / skill / pack = 5 ADS!)
            il.Append(lblGold20000);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 20000));
            il.Append(il.Create(OpCodes.Bgt_S, lblGold50000));
            il.Append(il.Create(OpCodes.Ldc_I4_5));
            il.Append(il.Create(OpCodes.Ret));

            // <= 50000 -> 10 ADS (40,000 Gold pack = 10 ADS)
            il.Append(lblGold50000);
            var lblGoldMax = il.Create(OpCodes.Ldc_I4_S, (sbyte)20);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4, 50000));
            il.Append(il.Create(OpCodes.Bgt_S, lblGoldMax));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)10));
            il.Append(il.Create(OpCodes.Ret));

            // > 50000 -> 20 ADS (100,000+ Gold pack = 20 ADS)
            il.Append(lblGoldMax);
            il.Append(il.Create(OpCodes.Ret));

            guiEventHandlerType.Methods.Add(mCalcRequiredAds);
        }

        var mCalcUpgradeAds = new MethodDefinition("CalcRequiredAdsForUpgrade", MethodAttributes.Public | MethodAttributes.Static, intType);
        mCalcUpgradeAds.Parameters.Add(new ParameterDefinition("upgradeCount", ParameterAttributes.None, intType));
        {
            var il = mCalcUpgradeAds.Body.GetILProcessor();
            var lbl6 = il.Create(OpCodes.Nop);
            var lbl9 = il.Create(OpCodes.Nop);
            var lbl10 = il.Create(OpCodes.Ldc_I4_5);

            // < 3 (+1..+3) -> 1 AD
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldc_I4_3));
            il.Append(il.Create(OpCodes.Bge_S, lbl6));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // < 6 (+4..+6) -> 2 ADS
            il.Append(lbl6);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldc_I4_6));
            il.Append(il.Create(OpCodes.Bge_S, lbl9));
            il.Append(il.Create(OpCodes.Ldc_I4_2));
            il.Append(il.Create(OpCodes.Ret));

            // < 9 (+7..+9) -> 3 ADS
            il.Append(lbl9);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)9));
            il.Append(il.Create(OpCodes.Bge_S, lbl10));
            il.Append(il.Create(OpCodes.Ldc_I4_3));
            il.Append(il.Create(OpCodes.Ret));

            // >= 9 (+10) -> 5 ADS
            il.Append(lbl10);
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mCalcUpgradeAds);
        }

        var mCalcPackAds = new MethodDefinition("CalcRequiredAdsForPack", MethodAttributes.Public | MethodAttributes.Static, intType);
        mCalcPackAds.Parameters.Add(new ParameterDefinition("item", ParameterAttributes.None, purchaseItemDataType));
        {
            mCalcPackAds.Body.InitLocals = true;
            var vPgm = new VariableDefinition(purchaseGameMoneyType);
            mCalcPackAds.Body.Variables.Add(vPgm);
            var getIsCoco = purchaseGameMoneyType.Properties.First(p => p.Name == "isCoCo").GetMethod;
            var getPgmMoney = purchaseGameMoneyType.Properties.First(p => p.Name == "money").GetMethod;

            var il = mCalcPackAds.Body.GetILProcessor();
            var lblFallback = il.Create(OpCodes.Ldc_I4_5);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brfalse_S, lblFallback));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, pidEventHandlerField));
            il.Append(il.Create(OpCodes.Isinst, purchaseGameMoneyType));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Brfalse_S, lblFallback));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Callvirt, getIsCoco));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Callvirt, getPgmMoney));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(lblFallback);
            il.Append(il.Create(OpCodes.Ret));
            purchaseItemDataType.Methods.Add(mCalcPackAds);
        }

        var mFormatAdLabel = new MethodDefinition("FormatAdLabel", MethodAttributes.Public | MethodAttributes.Static, stringType);
        mFormatAdLabel.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None, stringType));
        mFormatAdLabel.Parameters.Add(new ParameterDefinition("reqAds", ParameterAttributes.None, intType));
        {
            mFormatAdLabel.Body.InitLocals = true;
            var vWatched = new VariableDefinition(intType);
            mFormatAdLabel.Body.Variables.Add(vWatched);
            var il = mFormatAdLabel.Body.GetILProcessor();
            var lblMulti = il.Create(OpCodes.Nop);

            // if (reqAds <= 1) return "1 AD";
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Bgt_S, lblMulti));
            il.Append(il.Create(OpCodes.Ldstr, "1 AD"));
            il.Append(il.Create(OpCodes.Ret));

            // int watched = SaveData.GetEventCount(key);
            // return watched.ToString() + "/" + reqAds.ToString() + " ADS";
            il.Append(lblMulti);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, getEventCount));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloca_S, vWatched));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, "/"));
            il.Append(il.Create(OpCodes.Ldarga_S, mFormatAdLabel.Parameters[1]));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, " ADS"));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mFormatAdLabel);
        }

        var mRecordAdWatch = new MethodDefinition("RecordAdWatchAndCheckReady", MethodAttributes.Public | MethodAttributes.Static, boolType);
        mRecordAdWatch.Parameters.Add(new ParameterDefinition("key", ParameterAttributes.None, stringType));
        mRecordAdWatch.Parameters.Add(new ParameterDefinition("reqAds", ParameterAttributes.None, intType));
        {
            mRecordAdWatch.Body.InitLocals = true;
            var vWatched = new VariableDefinition(intType);
            mRecordAdWatch.Body.Variables.Add(vWatched);
            var il = mRecordAdWatch.Body.GetILProcessor();
            var lblNotReady = il.Create(OpCodes.Nop);

            var hasCompletedAd = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldsfld, gateReplay));
            il.Append(il.Create(OpCodes.Brtrue, hasCompletedAd));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(hasCompletedAd);

            // int watched = SaveData.GetEventCount(key) + 1;
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, getEventCount));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Add));
            il.Append(il.Create(OpCodes.Stloc_0));

            // if (watched < reqAds) goto lblNotReady;
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Blt_S, lblNotReady));

            // Ready! Reset counter to 0, save, return true
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Call, setEventCount));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // Not ready yet: save watched count, return false
            il.Append(lblNotReady);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, setEventCount));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mRecordAdWatch);
        }

        var mGetSkillPriceStr = new MethodDefinition("GetSkillPriceString", MethodAttributes.Public | MethodAttributes.Static, stringType);
        mGetSkillPriceStr.Parameters.Add(new ParameterDefinition("skill", ParameterAttributes.None, skillAttrType));
        mGetSkillPriceStr.Parameters.Add(new ParameterDefinition("skillLevel", ParameterAttributes.None, intType));
        {
            mGetSkillPriceStr.Body.InitLocals = true;
            var vCost = new VariableDefinition(intType);
            var vReqAds = new VariableDefinition(intType);
            var vId = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            mGetSkillPriceStr.Body.Variables.Add(vCost);
            mGetSkillPriceStr.Body.Variables.Add(vReqAds);
            mGetSkillPriceStr.Body.Variables.Add(vId);
            mGetSkillPriceStr.Body.Variables.Add(vKey);

            var getSkillCost = skillAttrType.Methods.First(m => m.Name == "GetCost");

            var il = mGetSkillPriceStr.Body.GetILProcessor();
            // int cost = skill.GetCost(skillLevel);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getSkillCost));
            il.Append(il.Create(OpCodes.Stloc_0));

            // int reqAds = CalcRequiredAds(skill.coco, cost);
            il.Append(il.Create(OpCodes.Ldarg_0));
            EmitGetMember(il, skillAttrType, "coco");
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Stloc_1));

            // int id = skill.id;
            il.Append(il.Create(OpCodes.Ldarg_0));
            EmitGetMember(il, skillAttrType, "id");
            il.Append(il.Create(OpCodes.Stloc_2));

            // string key = "AD_SKILL_" + id.ToString() + "_" + skillLevel.ToString();
            il.Append(il.Create(OpCodes.Ldstr, "AD_SKILL_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, "_"));
            il.Append(il.Create(OpCodes.Ldarga_S, mGetSkillPriceStr.Parameters[1]));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Stloc_3));

            // return cost.ToString() + " (" + FormatAdLabel(key, reqAds) + ")";
            il.Append(il.Create(OpCodes.Ldloca_S, vCost));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, " ("));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Ldstr, ")"));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mGetSkillPriceStr);
        }

        var mGetPackPriceStr = new MethodDefinition("GetPackPriceString", MethodAttributes.Public | MethodAttributes.Static, stringType);
        mGetPackPriceStr.Parameters.Add(new ParameterDefinition("item", ParameterAttributes.None, purchaseItemDataType));
        {
            var getPidName = purchaseItemDataType.Properties.First(p => p.Name == "name").GetMethod;
            var il = mGetPackPriceStr.Body.GetILProcessor();
            il.Append(il.Create(OpCodes.Ldstr, "AD_PACK_"));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getPidName));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, mCalcPackAds));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mGetPackPriceStr);
        }

        // 1. Patch GameEventHandler.GetItemCoinString(ShopItemInfo sii)
        //    - Not purchased: returns sii.coin + " (" + FormatAdLabel("AD_ITEM_" + sii.itemID, CalcRequiredAds(sii.cash, sii.coin)) + ")"
        //    - Purchased & Upgradable: returns "UPG (" + FormatAdLabel("AD_UPG_" + sii.itemID + "_" + sii.UpgradeCount, CalcRequiredAdsForUpgrade(sii.UpgradeCount)) + ")"
        //    - Max Upgrade: returns "MAX UPGRADE"
        {
            var m = gameEventHandlerType.Methods.First(x => x.Name == "GetItemCoinString");
            m.Body.Instructions.Clear();
            m.Body.Variables.Clear();
            m.Body.ExceptionHandlers.Clear();
            m.Body.InitLocals = true;
            var vCoin = new VariableDefinition(intType);
            var vItemId = new VariableDefinition(intType);
            var vUpgCnt = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            m.Body.Variables.Add(vCoin);
            m.Body.Variables.Add(vItemId);
            m.Body.Variables.Add(vUpgCnt);
            m.Body.Variables.Add(vKey);

            var getPurchased = shopItemInfoType.Properties.First(p => p.Name == "purchased").GetMethod;
            var getIsUpgradable = shopItemInfoType.Properties.First(p => p.Name == "isUpgradable").GetMethod;
            var getCash = shopItemInfoType.Properties.First(p => p.Name == "cash").GetMethod;
            var getItemCoin = shopItemInfoType.Properties.First(p => p.Name == "coin").GetMethod;
            var getItemId = shopItemInfoType.Properties.First(p => p.Name == "itemID").GetMethod;
            var getUpgradeCount = shopItemInfoType.Properties.First(p => p.Name == "UpgradeCount").GetMethod;
            var getItemType = shopItemInfoType.Properties.First(p => p.Name == "itemType").GetMethod;

            var il = m.Body.GetILProcessor();
            var lblNull = il.Create(OpCodes.Ldstr, "");
            var lblNotPurchased = il.Create(OpCodes.Nop);
            var lblMaxUpgrade = il.Create(OpCodes.Ldstr, "MAX UPGRADE");

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Brfalse, lblNull));

            // Save itemID into vItemId
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemId));
            il.Append(il.Create(OpCodes.Stloc_1));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getPurchased));
            il.Append(il.Create(OpCodes.Brfalse_S, lblNotPurchased));

            // Purchased: check if upgradable
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getIsUpgradable));
            il.Append(il.Create(OpCodes.Brfalse_S, lblMaxUpgrade));

            // Upgradable: "UPG (" + FormatAdLabel("AD_UPG_" + itemID + "_" + UpgradeCount, CalcRequiredAdsForUpgrade(UpgradeCount)) + ")"
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getUpgradeCount));
            il.Append(il.Create(OpCodes.Stloc_2));

            il.Append(il.Create(OpCodes.Ldstr, "AD_UPG_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vItemId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, "_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vUpgCnt));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Stloc_3));

            il.Append(il.Create(OpCodes.Ldstr, "UPG ("));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Call, mCalcUpgradeAds));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Ldstr, ")"));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Ret));

            il.Append(lblMaxUpgrade);
            il.Append(il.Create(OpCodes.Ret));

            // Not purchased (or Training item):
            il.Append(lblNotPurchased);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemCoin));
            il.Append(il.Create(OpCodes.Stloc_0));

            // Key: if TRAINING (9) use "AD_TRAIN_", else "AD_ITEM_"
            var lblIsTrain = il.Create(OpCodes.Ldstr, "AD_TRAIN_");
            var lblAfterPrefix = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemType));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)9));
            il.Append(il.Create(OpCodes.Beq_S, lblIsTrain));
            il.Append(il.Create(OpCodes.Ldstr, "AD_ITEM_"));
            il.Append(il.Create(OpCodes.Br_S, lblAfterPrefix));
            il.Append(lblIsTrain);
            il.Append(lblAfterPrefix);
            il.Append(il.Create(OpCodes.Ldloca_S, vItemId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Stloc_3));

            // return coin.ToString() + " (" + FormatAdLabel(key, CalcRequiredAds(sii.cash, coin)) + ")"
            il.Append(il.Create(OpCodes.Ldloca_S, vCoin));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, " ("));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Ldstr, ")"));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Ret));

            il.Append(lblNull);
            il.Append(il.Create(OpCodes.Ret));
        }

        // 2. Patch UIShopSkillDescUpdater.Refresh(SkillAttribute skill)
        //    Replace skill.GetCost(skillLevel).ToString() with GUIEventHandler.GetSkillPriceString(skill, skillLevel)
        {
            var m = uiShopSkillDescType.Methods.First(x => x.Name == "Refresh" && x.Parameters.Count == 1 && x.Parameters[0].ParameterType.Name == "SkillAttribute");
            for (int i = 0; i < m.Body.Instructions.Count; i++) {
                var ins = m.Body.Instructions[i];
                if (ins.OpCode == OpCodes.Callvirt && ins.Operand is MethodReference mr && mr.Name == "GetCost") {
                    // Original sequence at i-2..i+3:
                    // i-2: ldarg.1 (skill)
                    // i-1: ldloc.0 (skillLevel)
                    // i  : callvirt instance int32 SkillAttribute::GetCost(int32)
                    // i+1: stloc.1
                    // i+2: ldloca.s 1
                    // i+3: call instance string [mscorlib]System.Int32::ToString()
                    m.Body.Instructions[i].OpCode = OpCodes.Call;
                    m.Body.Instructions[i].Operand = mGetSkillPriceStr;
                    m.Body.Instructions[i + 1].OpCode = OpCodes.Nop;
                    m.Body.Instructions[i + 1].Operand = null;
                    m.Body.Instructions[i + 2].OpCode = OpCodes.Nop;
                    m.Body.Instructions[i + 2].Operand = null;
                    m.Body.Instructions[i + 3].OpCode = OpCodes.Nop;
                    m.Body.Instructions[i + 3].Operand = null;
                    break;
                }
            }
        }

        // 3. Patch UIBuyCoinButton.Update() & Buy()
        {
            var mUpdate = uiBuyCoinBtnType.Methods.First(x => x.Name == "Update");
            var priceTextField = uiBuyCoinBtnType.Fields.First(f => f.Name == "priceText");
            var dirtyField = uiBuyCoinBtnType.Fields.First(f => f.Name == "dirty");
            var il = mUpdate.Body.GetILProcessor();
            foreach (var ins in mUpdate.Body.Instructions.ToList()) {
                if (ins.OpCode == OpCodes.Ldfld && ins.Operand is FieldReference fr && fr.Name == "_PriceTextRoot") {
                    var prev = ins.Previous; // ldarg.0
                    // priceText = GUIEventHandler.GetPackPriceString(purchaseItemData);
                    il.InsertBefore(prev, il.Create(OpCodes.Ldarg_0));
                    il.InsertBefore(prev, il.Create(OpCodes.Ldloc_0)); // purchaseItemData
                    il.InsertBefore(prev, il.Create(OpCodes.Call, mGetPackPriceStr));
                    il.InsertBefore(prev, il.Create(OpCodes.Stfld, priceTextField));
                    break;
                }
            }

            var mBuy = uiBuyCoinBtnType.Methods.First(x => x.Name == "Buy");
            var ilBuy = mBuy.Body.GetILProcessor();
            foreach (var ins in mBuy.Body.Instructions.ToList()) {
                if (ins.OpCode == OpCodes.Stloc_0) {
                    ilBuy.InsertAfter(ins, ilBuy.Create(OpCodes.Stloc_0));
                    ilBuy.InsertAfter(ins, ilBuy.Create(OpCodes.Ldstr, "Playing Unity LevelPlay Ad...\n[App: 800423173 | BP_Rewarded_Android]"));
                    break;
                }
            }
            var lastRet = mBuy.Body.Instructions.Last();
            ilBuy.InsertBefore(lastRet, ilBuy.Create(OpCodes.Ldarg_0));
            ilBuy.InsertBefore(lastRet, ilBuy.Create(OpCodes.Ldc_I4_1));
            ilBuy.InsertBefore(lastRet, ilBuy.Create(OpCodes.Stfld, dirtyField));
        }

        // 4. Add Unity LevelPlay Bridge methods + _adRewardMsg, StartAdRewardPopup(string), OnAdWatchComplete() to GUIEventHandler
        var adRewardMsgField = new FieldDefinition("_adRewardMsg", FieldAttributes.Public | FieldAttributes.Static, stringType);
        guiEventHandlerType.Fields.Add(adRewardMsgField);

        var mTriggerRewarded = new MethodDefinition("TriggerUnityRewardedAd", MethodAttributes.Public | MethodAttributes.Static, voidType);
        mTriggerRewarded.Parameters.Add(new ParameterDefinition("rewardDesc", ParameterAttributes.None, stringType));
        {
            mTriggerRewarded.Body.InitLocals = true;
            var vAjc = new VariableDefinition(mod.ImportReference(androidJavaClassDef));
            var vArr = new VariableDefinition(new Mono.Cecil.ArrayType(objectType));
            mTriggerRewarded.Body.Variables.Add(vAjc);
            mTriggerRewarded.Body.Variables.Add(vArr);
            var il = mTriggerRewarded.Body.GetILProcessor();
            var lblTryStart = il.Create(OpCodes.Ldstr, "com.touchwizvault.ads.UnityLevelPlayBridge");
            var lblCatch = il.Create(OpCodes.Pop);
            var lblRet = il.Create(OpCodes.Ret);

            il.Append(il.Create(OpCodes.Ldsfld, gateReplay));
            il.Append(il.Create(OpCodes.Brtrue, lblRet));
            il.Append(lblTryStart);
            il.Append(il.Create(OpCodes.Newobj, ajcCtor));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldstr, "showRewardedAd"));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Newarr, objectType));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, ajoCallStatic));
            il.Append(il.Create(OpCodes.Leave_S, lblRet));
            il.Append(lblCatch);
            il.Append(il.Create(OpCodes.Leave_S, lblRet));
            il.Append(lblRet);

            mTriggerRewarded.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
                TryStart = lblTryStart,
                TryEnd = lblCatch,
                HandlerStart = lblCatch,
                HandlerEnd = lblRet,
                CatchType = mod.ImportReference(core.MainModule.GetType("System.Exception"))
            });
            guiEventHandlerType.Methods.Add(mTriggerRewarded);
        }

        var mTriggerInterstitial = new MethodDefinition("TriggerUnityInterstitialAd", MethodAttributes.Public | MethodAttributes.Static, voidType);
        mTriggerInterstitial.Parameters.Add(new ParameterDefinition("contextDesc", ParameterAttributes.None, stringType));
        {
            mTriggerInterstitial.Body.InitLocals = true;
            var vAjc = new VariableDefinition(mod.ImportReference(androidJavaClassDef));
            var vArr = new VariableDefinition(new Mono.Cecil.ArrayType(objectType));
            mTriggerInterstitial.Body.Variables.Add(vAjc);
            mTriggerInterstitial.Body.Variables.Add(vArr);
            var il = mTriggerInterstitial.Body.GetILProcessor();
            var lblTryStart = il.Create(OpCodes.Ldstr, "com.touchwizvault.ads.UnityLevelPlayBridge");
            var lblCatch = il.Create(OpCodes.Pop);
            var lblRet = il.Create(OpCodes.Ret);

            il.Append(lblTryStart);
            il.Append(il.Create(OpCodes.Newobj, ajcCtor));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldstr, "showInterstitialAd"));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Newarr, objectType));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Stelem_Ref));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, ajoCallStatic));
            il.Append(il.Create(OpCodes.Leave_S, lblRet));
            il.Append(lblCatch);
            il.Append(il.Create(OpCodes.Leave_S, lblRet));
            il.Append(lblRet);

            mTriggerInterstitial.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Catch) {
                TryStart = lblTryStart,
                TryEnd = lblCatch,
                HandlerStart = lblCatch,
                HandlerEnd = lblRet,
                CatchType = mod.ImportReference(core.MainModule.GetType("System.Exception"))
            });
            guiEventHandlerType.Methods.Add(mTriggerInterstitial);
        }

        var uiMsgBoxInstanceGetter = uiMessageBoxType.Properties.First(p => p.Name == "instance").GetMethod;
        var uiMsgBoxBegin5 = uiMessageBoxType.Methods.First(m => m.Name == "Begin" && m.Parameters.Count == 5);
        var uiMsgBoxBegin2 = uiMessageBoxType.Methods.First(m => m.Name == "Begin" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType == stringType);

        var mOnAdWatchComplete = new MethodDefinition("OnAdWatchComplete", MethodAttributes.Public, voidType);
        {
            var il = mOnAdWatchComplete.Body.GetILProcessor();
            var lblRet = il.Create(OpCodes.Ret);
            il.Append(il.Create(OpCodes.Call, uiMsgBoxInstanceGetter));
            il.Append(il.Create(OpCodes.Brfalse_S, lblRet));
            il.Append(il.Create(OpCodes.Call, uiMsgBoxInstanceGetter));
            il.Append(il.Create(OpCodes.Ldstr, "UNITY LEVELPLAY AD REWARD"));
            il.Append(il.Create(OpCodes.Ldsfld, adRewardMsgField));
            il.Append(il.Create(OpCodes.Callvirt, uiMsgBoxBegin2));
            il.Append(lblRet);
            guiEventHandlerType.Methods.Add(mOnAdWatchComplete);
        }

        var mStartAdRewardPopup = new MethodDefinition("StartAdRewardPopup", MethodAttributes.Public, voidType);
        mStartAdRewardPopup.Parameters.Add(new ParameterDefinition("msg", ParameterAttributes.None, stringType));
        {
            var il = mStartAdRewardPopup.Body.GetILProcessor();
            var completed = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldsfld, gateReplay));
            il.Append(il.Create(OpCodes.Brtrue, completed));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Call, gateBegin));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(completed);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Stsfld, adRewardMsgField));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, mOnAdWatchComplete));
            il.Append(il.Create(OpCodes.Ret));
            guiEventHandlerType.Methods.Add(mStartAdRewardPopup);
        }

        // 5. Patch PurchaseManager.Start() to initialize UnityAds on boot & Purchase() to enforce multi-ad pack requirements!
        {
            var mStart = purchaseMgrType.Methods.First(x => x.Name == "Start");
            var ilStart = mStart.Body.GetILProcessor();
            var firstIns = mStart.Body.Instructions[0];
            ilStart.InsertBefore(firstIns, ilStart.Create(OpCodes.Ldstr, "init"));
            ilStart.InsertBefore(firstIns, ilStart.Create(OpCodes.Call, mTriggerRewarded));

            var m = purchaseMgrType.Methods.First(x => x.Name == "Purchase" && x.Parameters.Count == 2);
            var purchasingItemField = purchaseMgrType.Fields.First(f => f.Name == "purchasingItem");
            var getPidName = purchaseItemDataType.Properties.First(p => p.Name == "name").GetMethod;
            var getPidDisplayName = purchaseItemDataType.Properties.First(p => p.Name == "displayName").GetMethod;

            m.Body.Instructions.Clear();
            m.Body.Variables.Clear();
            m.Body.ExceptionHandlers.Clear();
            m.Body.InitLocals = true;
            var vReqAds = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            m.Body.Variables.Add(vReqAds);
            m.Body.Variables.Add(vKey);

            var il = m.Body.GetILProcessor();
            var lblRet = il.Create(OpCodes.Ret);
            var lblProgressOnly = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Brfalse_S, lblRet));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Stfld, purchasingItemField));

            // int reqAds = GUIEventHandler.CalcRequiredAdsForPack(item);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Call, mCalcPackAds));
            il.Append(il.Create(OpCodes.Stloc_0));

            // string key = "AD_PACK_" + item.name;
            il.Append(il.Create(OpCodes.Ldstr, "AD_PACK_"));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getPidName));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Stloc_1));

            // if (!GUIEventHandler.RecordAdWatchAndCheckReady(key, reqAds)) goto lblProgressOnly;
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mRecordAdWatch));
            il.Append(il.Create(OpCodes.Brfalse_S, lblProgressOnly));

            // All required ads watched -> play ad & grant pack!
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getPidDisplayName));
            il.Append(il.Create(OpCodes.Call, mTriggerRewarded));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, purchaseMgrType.Methods.First(m => m.Name == "OnPurchaseCheatSuccesful")));
            il.Append(il.Create(OpCodes.Ret));

            // Progress only -> play ad & show updated progress (e.g. 1/5 ADS or 1/40 ADS)
            il.Append(lblProgressOnly);
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getPidDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, "\nAd Progress: "));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(lblRet);
        }

        // Patch GUIEventHandler.OnResultNext() & OnResultRetry() to trigger BP_Interstitial_Android!
        foreach (var methodName in new[] { "OnResultNext", "OnResultRetry" }) {
            var mRes = guiEventHandlerType.Methods.FirstOrDefault(m => m.Name == methodName);
            if (mRes != null) {
                var il = mRes.Body.GetILProcessor();
                foreach (var ins in mRes.Body.Instructions.ToList()) {
                    if (ins.OpCode == OpCodes.Ldfld && ins.Operand is FieldReference fr && fr.Name == "_adPanel") {
                        var prev = ins.Previous;
                        il.InsertBefore(prev, il.Create(OpCodes.Ldstr, "Post-Match Interstitial (BP_Interstitial_Android)"));
                        il.InsertBefore(prev, il.Create(OpCodes.Call, mTriggerInterstitial));
                        break;
                    }
                }
            }
        }

        // 6. Patch GUIEventHandler.OnEnterGachaShop() & UIShopItemPanel.Buy() so upgrading items uses multi-ad scaling!
        {
            var mEnterGacha = guiEventHandlerType.Methods.First(m => m.Name == "OnEnterGachaShop");
            mEnterGacha.Body.Instructions.Clear();
            mEnterGacha.Body.Variables.Clear();
            mEnterGacha.Body.ExceptionHandlers.Clear();
            mEnterGacha.Body.InitLocals = true;
            var vShopItem = new VariableDefinition(uiShopItemType);
            var vItemInfo = new VariableDefinition(shopItemInfoType);
            var vLevelStatus = new VariableDefinition(statUpgradeInfoType);
            var vItemId = new VariableDefinition(intType);
            var vUpgCnt = new VariableDefinition(intType);
            var vReqAds = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            mEnterGacha.Body.Variables.Add(vShopItem);
            mEnterGacha.Body.Variables.Add(vItemInfo);
            mEnterGacha.Body.Variables.Add(vLevelStatus);
            mEnterGacha.Body.Variables.Add(vItemId);
            mEnterGacha.Body.Variables.Add(vUpgCnt);
            mEnterGacha.Body.Variables.Add(vReqAds);
            mEnterGacha.Body.Variables.Add(vKey);

            var selectedShopItemField = guiEventHandlerType.Fields.First(f => f.Name == "_selectedShopItem");
            var getItemInfo = uiShopItemType.Properties.First(p => p.Name == "itemInfo").GetMethod;
            var upgradeItemMethod = shopItemInfoType.Methods.First(m => m.Name == "UpgradeItem");
            var getDisplayName = shopItemInfoType.Properties.First(p => p.Name == "displayName").GetMethod;
            var getItemId = shopItemInfoType.Properties.First(p => p.Name == "itemID").GetMethod;
            var getUpgradeCount = shopItemInfoType.Properties.First(p => p.Name == "UpgradeCount").GetMethod;
            var getLevelStatus = saveDataType.Methods.First(m => m.Name == "GetLevelStatus");
            var getUpgrade = saveDataType.Methods.First(m => m.Name == "GetUpgrade");
            var addStat = statUpgradeInfoType.Methods.First(m => m.Name == "Add" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType == statUpgradeInfoType);
            var getWearingItems = saveDataType.Methods.First(m => m.Name == "GetWearingItems");
            var applyItemEffect = shopDataType.Methods.First(m => m.Name == "ApplyItemEffect");
            var setStats = athleteType.Methods.First(m => m.Name == "SetStats" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType == statUpgradeInfoType);
            var broadcastCurrent = guiEventHandlerType.Methods.First(m => m.Name == "BroadcastMessageToCurrentPanel" && m.Parameters.Count == 1);

            var il = mEnterGacha.Body.GetILProcessor();
            var lblCapsuleBonus = il.Create(OpCodes.Nop);
            var lblUpgProgress = il.Create(OpCodes.Nop);
            var lblRet = il.Create(OpCodes.Ret);

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, selectedShopItemField));
            il.Append(il.Create(OpCodes.Isinst, uiShopItemType));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Brfalse, lblCapsuleBonus));

            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemInfo));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Brfalse, lblCapsuleBonus));

            // Compute key = "AD_UPG_" + itemID + "_" + UpgradeCount and reqAds = CalcRequiredAdsForUpgrade(UpgradeCount)
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getItemId));
            il.Append(il.Create(OpCodes.Stloc_3));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getUpgradeCount));
            il.Append(il.Create(OpCodes.Stloc_S, vUpgCnt));
            il.Append(il.Create(OpCodes.Ldloc_S, vUpgCnt));
            il.Append(il.Create(OpCodes.Call, mCalcUpgradeAds));
            il.Append(il.Create(OpCodes.Stloc_S, vReqAds));

            il.Append(il.Create(OpCodes.Ldstr, "AD_UPG_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vItemId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, "_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vUpgCnt));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Stloc_S, vKey));

            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_S, vReqAds));
            il.Append(il.Create(OpCodes.Call, mRecordAdWatch));
            il.Append(il.Create(OpCodes.Brfalse_S, lblUpgProgress));

            // Upgrade ready! Upgrade the item!
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, upgradeItemMethod));

            // Save PlayerPrefsX
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));

            // Update player stats
            il.Append(il.Create(OpCodes.Call, getLevelStatus));
            il.Append(il.Create(OpCodes.Stloc_2));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Call, getUpgrade));
            il.Append(il.Create(OpCodes.Callvirt, addStat));
            il.Append(il.Create(OpCodes.Pop));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Call, getWearingItems));
            il.Append(il.Create(OpCodes.Call, applyItemEffect));
            il.Append(il.Create(OpCodes.Ldsfld, gmInstanceField));
            il.Append(il.Create(OpCodes.Callvirt, gmPlayerGetter));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Callvirt, setStats));

            // Refresh UI
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Call, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Call, broadcastCurrent));

            // Show Ad Reward Popup
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, " upgraded (+1 Stat Level) via Ads!"));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Call, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ret));

            // Upgrade ad progress (< reqAds)
            il.Append(lblUpgProgress);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Call, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Call, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, "\nUpgrade Ad Progress: "));
            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_S, vReqAds));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Call, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ret));

            // Fallback if Capsule Shop clicked from main shop menu: grant +1,500 Gold & +25 Star Cash per 1 Ad!
            il.Append(lblCapsuleBonus);
            var grantCapsule = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldsfld, gateReplay));
            il.Append(il.Create(OpCodes.Brtrue, grantCapsule));
            il.Append(il.Create(OpCodes.Ldstr, "Capsule bonus"));
            il.Append(il.Create(OpCodes.Call, gateBegin));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(grantCapsule);
            il.Append(il.Create(OpCodes.Call, getCoin));
            il.Append(il.Create(OpCodes.Ldc_I4, 1500));
            il.Append(il.Create(OpCodes.Add));
            il.Append(il.Create(OpCodes.Call, setCoin));
            il.Append(il.Create(OpCodes.Call, getCoco));
            il.Append(il.Create(OpCodes.Ldc_I4_S, (sbyte)25));
            il.Append(il.Create(OpCodes.Add));
            il.Append(il.Create(OpCodes.Call, setCoco));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Call, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldstr, "Capsule Ad Bonus!\nReceived +1,500 Gold & +25 Star Cash!"));
            il.Append(il.Create(OpCodes.Call, mStartAdRewardPopup));
            il.Append(lblRet);
        }

        // Update UIShopItemPanel.Buy() so clicking Upgrade calls GUIEventHandler.instance.OnEnterGachaShop() directly
        {
            var mBuy = uiShopItemPanelType.Methods.First(m => m.Name == "Buy");
            var mEnterGacha = guiEventHandlerType.Methods.First(m => m.Name == "OnEnterGachaShop");
            var mTrain = gameManagerType.Methods.First(m => m.Name == "Train");
            var mBuySet = gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType == setItemInfoType);
            var mBuyNormal = gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType == shopItemInfoType);
            var listItemsField = uiShopItemPanelType.Fields.First(f => f.Name == "_ListItems");
            var getSelectedItem = mod.ImportReference(listItemsField.FieldType.Resolve().Properties.First(p => p.Name == "SelectedItem").GetMethod);
            var getItemInfo = uiShopItemType.Properties.First(p => p.Name == "itemInfo").GetMethod;
            var getItemType = shopItemInfoType.Properties.First(p => p.Name == "itemType").GetMethod;
            var getPurchased = shopItemInfoType.Properties.First(p => p.Name == "purchased").GetMethod;
            var getIsUpgradable = shopItemInfoType.Properties.First(p => p.Name == "isUpgradable").GetMethod;

            mBuy.Body.Instructions.Clear();
            mBuy.Body.Variables.Clear();
            mBuy.Body.ExceptionHandlers.Clear();
            mBuy.Body.InitLocals = true;
            var vShopItem = new VariableDefinition(uiShopItemType);
            var vItemInfo = new VariableDefinition(shopItemInfoType);
            mBuy.Body.Variables.Add(vShopItem);
            mBuy.Body.Variables.Add(vItemInfo);

            var il = mBuy.Body.GetILProcessor();
            var lblRet = il.Create(OpCodes.Ret);
            var lblNotTrain = il.Create(OpCodes.Nop);
            var lblNotSet = il.Create(OpCodes.Nop);
            var lblUpgrade = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Ldfld, listItemsField));
            il.Append(il.Create(OpCodes.Callvirt, getSelectedItem));
            il.Append(il.Create(OpCodes.Isinst, uiShopItemType));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Brfalse_S, lblRet));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemInfo));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Brfalse_S, lblRet));

            // if (itemInfo.itemType == TRAINING (9)) -> Train
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getItemType));
            il.Append(il.Create(OpCodes.Ldc_I4, 9));
            il.Append(il.Create(OpCodes.Bne_Un_S, lblNotTrain));
            il.Append(il.Create(OpCodes.Ldsfld, gmInstanceField));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, mTrain));
            il.Append(il.Create(OpCodes.Ret));

            // if (itemInfo.purchased) -> if (itemInfo.isUpgradable) -> GUIEventHandler.instance.OnEnterGachaShop()
            il.Append(lblNotTrain);
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getPurchased));
            il.Append(il.Create(OpCodes.Brtrue_S, lblUpgrade));

            // Not purchased yet: check if SET or normal item
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Isinst, setItemInfoType));
            il.Append(il.Create(OpCodes.Brfalse_S, lblNotSet));
            il.Append(il.Create(OpCodes.Ldsfld, gmInstanceField));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Castclass, setItemInfoType));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Callvirt, mBuySet));
            il.Append(il.Create(OpCodes.Ret));

            il.Append(lblNotSet);
            il.Append(il.Create(OpCodes.Ldsfld, gmInstanceField));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, mBuyNormal));
            il.Append(il.Create(OpCodes.Ret));

            // Purchased: upgrade via Ad!
            il.Append(lblUpgrade);
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getIsUpgradable));
            il.Append(il.Create(OpCodes.Brfalse_S, lblRet));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Callvirt, mEnterGacha));
            il.Append(lblRet);
        }

        // 7. Patch GameManager.BuySkill(int id) to check wallet first, or require cost-aligned multi-ads!
        {
            var mBuySkill = gameManagerType.Methods.First(m => m.Name == "BuySkill");
            var skillMgrType = gmSkillMgrGetter.ReturnType.Resolve();
            var getSkillAttr = skillMgrType.Methods.First(m => m.Name == "GetSkillAttribute");
            var maxLevelField = skillAttrType.Fields.First(f => f.Name == "_MaxLevel");
            var getSkillCost = skillAttrType.Methods.First(m => m.Name == "GetCost");
            var getSkillLevel = saveDataType.Methods.First(m => m.Name == "GetSkillLevel");
            var skillBuyMethod = skillMgrType.Methods.First(m => m.Name == "Buy");
            var addEventCount = saveDataType.Methods.First(m => m.Name == "AddEventCount");
            var onMaxLevel = guiEventHandlerType.Methods.First(m => m.Name == "OnShopSkillMaxLevel");
            var broadcastCurrent = guiEventHandlerType.Methods.First(m => m.Name == "BroadcastMessageToCurrentPanel" && m.Parameters.Count == 1);

            mBuySkill.Body.Instructions.Clear();
            mBuySkill.Body.Variables.Clear();
            mBuySkill.Body.ExceptionHandlers.Clear();
            mBuySkill.Body.InitLocals = true;
            var vSkill = new VariableDefinition(skillAttrType);
            var vLevel = new VariableDefinition(intType);
            var vCost = new VariableDefinition(intType);
            var vWallet = new VariableDefinition(intType);
            var vReqAds = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            mBuySkill.Body.Variables.Add(vSkill);
            mBuySkill.Body.Variables.Add(vLevel);
            mBuySkill.Body.Variables.Add(vCost);
            mBuySkill.Body.Variables.Add(vWallet);
            mBuySkill.Body.Variables.Add(vReqAds);
            mBuySkill.Body.Variables.Add(vKey);

            var il = mBuySkill.Body.GetILProcessor();
            var lblRet = il.Create(OpCodes.Ret);
            var lblNotMax = il.Create(OpCodes.Nop);
            var lblUseAd = il.Create(OpCodes.Nop);
            var lblAdProgress = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmSkillMgrGetter));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getSkillAttr));
            il.Append(il.Create(OpCodes.Stloc_0));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Brfalse, lblRet));

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Call, getSkillLevel));
            il.Append(il.Create(OpCodes.Stloc_1));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldfld, maxLevelField));
            il.Append(il.Create(OpCodes.Blt_S, lblNotMax));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Callvirt, onMaxLevel));
            il.Append(il.Create(OpCodes.Ret));

            // Check wallet vs cost
            il.Append(lblNotMax);
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Callvirt, getSkillCost));
            il.Append(il.Create(OpCodes.Stloc_2));

            il.Append(il.Create(OpCodes.Ldloc_0));
            EmitGetMember(il, skillAttrType, "coco");
            il.Append(il.Create(OpCodes.Call, getMoney));
            il.Append(il.Create(OpCodes.Stloc_3));

            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Blt_S, lblUseAd));

            // Enough Gold/Stars in wallet! Deduct cost and unlock immediately without ad
            il.Append(il.Create(OpCodes.Ldloc_0));
            EmitGetMember(il, skillAttrType, "coco");
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Sub));
            il.Append(il.Create(OpCodes.Call, setMoney));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmSkillMgrGetter));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, skillBuyMethod));
            il.Append(il.Create(OpCodes.Ldstr, "BuySkill"));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Call, addEventCount));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ret));

            // Not enough Gold/Stars -> use multi-ad progress!
            il.Append(lblUseAd);
            il.Append(il.Create(OpCodes.Ldloc_0));
            EmitGetMember(il, skillAttrType, "coco");
            il.Append(il.Create(OpCodes.Ldloc_2));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Stloc_S, vReqAds));

            il.Append(il.Create(OpCodes.Ldstr, "AD_SKILL_"));
            il.Append(il.Create(OpCodes.Ldarga_S, mBuySkill.Parameters[0]));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Ldstr, "_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vLevel));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat4));
            il.Append(il.Create(OpCodes.Stloc_S, vKey));

            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_S, vReqAds));
            il.Append(il.Create(OpCodes.Call, mRecordAdWatch));
            il.Append(il.Create(OpCodes.Brfalse_S, lblAdProgress));

            // All required ads watched -> unlock skill!
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmSkillMgrGetter));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, skillBuyMethod));
            il.Append(il.Create(OpCodes.Ldstr, "BuySkill"));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Call, addEventCount));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldloc_0));
            EmitGetMember(il, skillAttrType, "displayName");
            il.Append(il.Create(OpCodes.Ldstr, " unlocked / upgraded (+1 LV) via Ads!"));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ret));

            // Progress only -> update UI & show progress popup
            il.Append(lblAdProgress);
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldloc_0));
            EmitGetMember(il, skillAttrType, "displayName");
            il.Append(il.Create(OpCodes.Ldstr, "\nSkill Ad Progress: "));
            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_S, vReqAds));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(lblRet);
        }

        // 8. Patch GameManager.BuyItem(ShopItemInfo item, bool skipNotice)
        //    - If player has enough Gold/Stars in wallet: deduct cost and unlock immediately!
        //    - Otherwise: record ad watch toward CalcRequiredAds(item.cash, item.coin) and unlock only when ready!
        {
            var mBuyItem = gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType == shopItemInfoType);
            var getPurchased = shopItemInfoType.Properties.First(p => p.Name == "purchased").GetMethod;
            var getCash = shopItemInfoType.Properties.First(p => p.Name == "cash").GetMethod;
            var getItemCoin = shopItemInfoType.Properties.First(p => p.Name == "coin").GetMethod;
            var getItemId = shopItemInfoType.Properties.First(p => p.Name == "itemID").GetMethod;
            var onAlreadyPurchased = guiEventHandlerType.Methods.First(m => m.Name == "OnAlreadyPurchased");
            var onBuyItemOnInfo = shopItemInfoType.Methods.First(m => m.Name == "OnBuyItem");
            var onBoughtItemEv = gmEventHandlerField.FieldType.Resolve().Methods.First(m => m.Name == "OnBoughtItem");
            var onBuyItemAch = gmAchMgrGetter.ReturnType.Resolve().Methods.First(m => m.Name == "OnBuyItem");
            var onStatusChanged = gmSkillMgrGetter.ReturnType.Resolve().Methods.First(m => m.Name == "OnStatusChanged");
            var broadcastCurrent = guiEventHandlerType.Methods.First(m => m.Name == "BroadcastMessageToCurrentPanel" && m.Parameters.Count == 1);
            var getDisplayName = shopItemInfoType.Properties.First(p => p.Name == "displayName").GetMethod;

            mBuyItem.Body.Instructions.Clear();
            mBuyItem.Body.Variables.Clear();
            mBuyItem.Body.ExceptionHandlers.Clear();
            mBuyItem.Body.InitLocals = true;
            var vCost = new VariableDefinition(intType);
            var vWallet = new VariableDefinition(intType);
            var vItemId = new VariableDefinition(intType);
            var vReqAds = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            mBuyItem.Body.Variables.Add(vCost);
            mBuyItem.Body.Variables.Add(vWallet);
            mBuyItem.Body.Variables.Add(vItemId);
            mBuyItem.Body.Variables.Add(vReqAds);
            mBuyItem.Body.Variables.Add(vKey);

            var il = mBuyItem.Body.GetILProcessor();
            var lblRet = il.Create(OpCodes.Ret);
            var lblCheckMoney = il.Create(OpCodes.Nop);
            var lblUseAd = il.Create(OpCodes.Nop);
            var lblAdProgress = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Brfalse, lblRet));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getPurchased));
            il.Append(il.Create(OpCodes.Brfalse_S, lblCheckMoney));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Callvirt, onAlreadyPurchased));
            il.Append(il.Create(OpCodes.Ret));

            // Check wallet vs item.coin
            il.Append(lblCheckMoney);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getItemCoin));
            il.Append(il.Create(OpCodes.Stloc_0));

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Call, getMoney));
            il.Append(il.Create(OpCodes.Stloc_1));

            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Blt_S, lblUseAd));

            // Enough Gold/Stars in wallet -> deduct cost & unlock immediately!
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Sub));
            il.Append(il.Create(OpCodes.Call, setMoney));

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, onBuyItemOnInfo));
            il.Append(il.Create(OpCodes.Ldsfld, gmEventHandlerField));
            il.Append(il.Create(OpCodes.Callvirt, onBoughtItemEv));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmAchMgrGetter));
            il.Append(il.Create(OpCodes.Callvirt, onBuyItemAch));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmSkillMgrGetter));
            il.Append(il.Create(OpCodes.Callvirt, onStatusChanged));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ret));

            // Not enough Gold/Stars -> use multi-ad progress!
            il.Append(lblUseAd);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getItemId));
            il.Append(il.Create(OpCodes.Stloc_2));

            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Stloc_3));

            il.Append(il.Create(OpCodes.Ldstr, "AD_ITEM_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vItemId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Stloc_S, vKey));

            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Call, mRecordAdWatch));
            il.Append(il.Create(OpCodes.Brfalse_S, lblAdProgress));

            // All required ads watched -> unlock & equip item!
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, onBuyItemOnInfo));
            il.Append(il.Create(OpCodes.Ldsfld, gmEventHandlerField));
            il.Append(il.Create(OpCodes.Callvirt, onBoughtItemEv));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmAchMgrGetter));
            il.Append(il.Create(OpCodes.Callvirt, onBuyItemAch));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Call, gmSkillMgrGetter));
            il.Append(il.Create(OpCodes.Callvirt, onStatusChanged));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, " unlocked & equipped via Ads!"));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ret));

            // Ad progress (< reqAds) -> refresh UI and show progress popup!
            il.Append(lblAdProgress);
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, "\nItem Ad Progress: "));
            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(lblRet);
        }

        // Patch GameManager.BuyItem(eBoosterType type, ShopItemInfo item) so boosters also check wallet or use Ad
        {
            var mBoost = gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.Name == "eBoosterType");
            var getBoostCount = saveDataType.Methods.First(m => m.Name == "GetBoostCount");
            var setBoostCount = saveDataType.Methods.First(m => m.Name == "SetBoostCount");
            var broadcastCurrent = guiEventHandlerType.Methods.First(m => m.Name == "BroadcastMessageToCurrentPanel" && m.Parameters.Count == 1);

            mBoost.Body.Instructions.Clear();
            mBoost.Body.Variables.Clear();
            mBoost.Body.ExceptionHandlers.Clear();
            var il = mBoost.Body.GetILProcessor();
            var grantBooster = il.Create(OpCodes.Nop);
            il.Append(il.Create(OpCodes.Ldsfld, gateReplay));
            il.Append(il.Create(OpCodes.Brtrue, grantBooster));
            il.Append(il.Create(OpCodes.Ldstr, "Booster"));
            il.Append(il.Create(OpCodes.Call, gateBegin));
            il.Append(il.Create(OpCodes.Ret));
            il.Append(grantBooster);
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Ldarg_1));
            il.Append(il.Create(OpCodes.Call, getBoostCount));
            il.Append(il.Create(OpCodes.Ldc_I4, 5));
            il.Append(il.Create(OpCodes.Add));
            il.Append(il.Create(OpCodes.Call, setBoostCount));
            il.Append(il.Create(OpCodes.Call, ppxInstanceGetter));
            il.Append(il.Create(OpCodes.Callvirt, ppxSave));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Booster (+5 Matches) unlocked via Ad!"));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ret));
        }

        // 9. Helper method on GUIEventHandler for Training & LevelUp ad checks when wallet is insufficient:
        //    bool CheckOrPayForTraining(ShopItemInfo item)
        var mCheckOrPayTrain = new MethodDefinition("CheckOrPayForTraining", MethodAttributes.Public | MethodAttributes.Static, boolType);
        mCheckOrPayTrain.Parameters.Add(new ParameterDefinition("item", ParameterAttributes.None, shopItemInfoType));
        {
            mCheckOrPayTrain.Body.InitLocals = true;
            var vCost = new VariableDefinition(intType);
            var vWallet = new VariableDefinition(intType);
            var vItemId = new VariableDefinition(intType);
            var vReqAds = new VariableDefinition(intType);
            var vKey = new VariableDefinition(stringType);
            mCheckOrPayTrain.Body.Variables.Add(vCost);
            mCheckOrPayTrain.Body.Variables.Add(vWallet);
            mCheckOrPayTrain.Body.Variables.Add(vItemId);
            mCheckOrPayTrain.Body.Variables.Add(vReqAds);
            mCheckOrPayTrain.Body.Variables.Add(vKey);

            var getCash = shopItemInfoType.Properties.First(p => p.Name == "cash").GetMethod;
            var getItemCoin = shopItemInfoType.Properties.First(p => p.Name == "coin").GetMethod;
            var getItemId = shopItemInfoType.Properties.First(p => p.Name == "itemID").GetMethod;
            var getDisplayName = shopItemInfoType.Properties.First(p => p.Name == "displayName").GetMethod;
            var broadcastCurrent = guiEventHandlerType.Methods.First(m => m.Name == "BroadcastMessageToCurrentPanel" && m.Parameters.Count == 1);

            var il = mCheckOrPayTrain.Body.GetILProcessor();
            var lblUseAd = il.Create(OpCodes.Nop);
            var lblAdProgress = il.Create(OpCodes.Nop);

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemCoin));
            il.Append(il.Create(OpCodes.Stloc_0));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Call, getMoney));
            il.Append(il.Create(OpCodes.Stloc_1));

            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Blt_S, lblUseAd));

            // Wallet has enough -> deduct & return true
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Ldloc_1));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Sub));
            il.Append(il.Create(OpCodes.Call, setMoney));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // Not enough money -> check ad progress
            il.Append(lblUseAd);
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getItemId));
            il.Append(il.Create(OpCodes.Stloc_2));

            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getCash));
            il.Append(il.Create(OpCodes.Ldloc_0));
            il.Append(il.Create(OpCodes.Call, mCalcRequiredAds));
            il.Append(il.Create(OpCodes.Stloc_3));

            il.Append(il.Create(OpCodes.Ldstr, "AD_TRAIN_"));
            il.Append(il.Create(OpCodes.Ldloca_S, vItemId));
            il.Append(il.Create(OpCodes.Call, intToString));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Stloc_S, vKey));

            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Call, mRecordAdWatch));
            il.Append(il.Create(OpCodes.Brfalse_S, lblAdProgress));

            // Ready! Trigger ad popup & return true
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, " Training Completed via Ads!"));
            il.Append(il.Create(OpCodes.Call, strConcat2));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ldc_I4_1));
            il.Append(il.Create(OpCodes.Ret));

            // Not ready -> refresh UI, trigger progress popup, return false
            il.Append(lblAdProgress);
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "Refresh"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldstr, "OnPurchaseSuccess"));
            il.Append(il.Create(OpCodes.Callvirt, broadcastCurrent));
            il.Append(il.Create(OpCodes.Ldsfld, guiInstanceField));
            il.Append(il.Create(OpCodes.Ldarg_0));
            il.Append(il.Create(OpCodes.Callvirt, getDisplayName));
            il.Append(il.Create(OpCodes.Ldstr, "\nTraining Ad Progress: "));
            il.Append(il.Create(OpCodes.Ldloc_S, vKey));
            il.Append(il.Create(OpCodes.Ldloc_3));
            il.Append(il.Create(OpCodes.Call, mFormatAdLabel));
            il.Append(il.Create(OpCodes.Call, strConcat3));
            il.Append(il.Create(OpCodes.Callvirt, mStartAdRewardPopup));
            il.Append(il.Create(OpCodes.Ldc_I4_0));
            il.Append(il.Create(OpCodes.Ret));

            guiEventHandlerType.Methods.Add(mCheckOrPayTrain);
        }

        // Patch GameManager.Train(ShopItemInfo item):
        // Check training limits first, and replace wallet check & deduction with GUIEventHandler.CheckOrPayForTraining(item)
        {
            var mTrain = gameManagerType.Methods.First(m => m.Name == "Train");
            // In original Train:
            //   int num = ((!cash) ? SaveData.GetCoin() : SaveData.GetCoco());
            //   if (num < item.coin) { ... return; }
            //   ... checks isTrainingAvailable and upgrade.Compare(statUpgradeInfo) ...
            //   SaveData.SetUpgrade(upgrade);
            //   if (cash) SaveData.SetCoco(num - item.coin); else SaveData.SetCoin(num - item.coin);
            // Let's make the initial num = 99999999 so it validates isTrainingAvailable & stat limits first,
            // and right before SaveData.SetUpgrade(upgrade), call if (!GUIEventHandler.CheckOrPayForTraining(item)) return;
            // and nop out SetCoco / SetCoin since CheckOrPayForTraining already deducted if paid via wallet!
            for (int i = 0; i < mTrain.Body.Instructions.Count; i++) {
                var ins = mTrain.Body.Instructions[i];
                if (ins.OpCode == OpCodes.Call && ins.Operand is MethodReference mr) {
                    if (mr.Name == "GetCoin" || mr.Name == "GetCoco") {
                        ins.OpCode = OpCodes.Ldc_I4;
                        ins.Operand = 99999999;
                    } else if (mr.Name == "SetCoco" || mr.Name == "SetCoin") {
                        ins.OpCode = OpCodes.Pop;
                        ins.Operand = null;
                    }
                }
            }
            var ilTrain = mTrain.Body.GetILProcessor();
            foreach (var ins in mTrain.Body.Instructions.ToList()) {
                if (ins.OpCode == OpCodes.Call && ins.Operand is MethodReference mr && mr.Name == "SetUpgrade") {
                    var prev = ins.Previous; // ldloc.2 (upgrade) before call SetUpgrade
                    var lblProceed = prev;
                    var retIns = ilTrain.Create(OpCodes.Ret);
                    ilTrain.InsertBefore(lblProceed, ilTrain.Create(OpCodes.Ldarg_1));
                    ilTrain.InsertBefore(lblProceed, ilTrain.Create(OpCodes.Call, mCheckOrPayTrain));
                    ilTrain.InsertBefore(lblProceed, ilTrain.Create(OpCodes.Brtrue_S, lblProceed));
                    ilTrain.InsertBefore(lblProceed, retIns);
                    break;
                }
            }
        }

        var getMethod = mod.ImportReference(core.MainModule.GetType("System.Reflection.MethodBase").Methods.First(m => m.Name == "GetMethodFromHandle" && m.Parameters.Count == 1));
        var deferredActions = new[] {
            purchaseMgrType.Methods.First(m => m.Name == "Purchase" && m.Parameters.Count == 2),
            guiEventHandlerType.Methods.First(m => m.Name == "OnEnterGachaShop"),
            gameManagerType.Methods.First(m => m.Name == "BuySkill"),
            gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType == shopItemInfoType),
            gameManagerType.Methods.First(m => m.Name == "BuyItem" && m.Parameters.Count == 2 && m.Parameters[0].ParameterType.Name == "eBoosterType"),
            gameManagerType.Methods.First(m => m.Name == "Train")
        };
        foreach (var m in deferredActions) {
            if (m.ReturnType.FullName != "System.Void") throw new Exception("Expected void action: " + m.FullName);
            var il = m.Body.GetILProcessor();
            var first = m.Body.Instructions[0];
            void Emit(Instruction ins) => il.InsertBefore(first, ins);
            Emit(il.Create(OpCodes.Ldarg_0));
            Emit(il.Create(OpCodes.Ldtoken, m));
            Emit(il.Create(OpCodes.Call, getMethod));
            Emit(il.Create(OpCodes.Ldc_I4, m.Parameters.Count));
            Emit(il.Create(OpCodes.Newarr, objectType));
            for (int i = 0; i < m.Parameters.Count; i++) {
                Emit(il.Create(OpCodes.Dup));
                Emit(il.Create(OpCodes.Ldc_I4, i));
                Emit(il.Create(OpCodes.Ldarg, m.Parameters[i]));
                if (m.Parameters[i].ParameterType.IsValueType) Emit(il.Create(OpCodes.Box, m.Parameters[i].ParameterType));
                Emit(il.Create(OpCodes.Stelem_Ref));
            }
            Emit(il.Create(OpCodes.Call, gateCapture));
            Emit(il.Create(OpCodes.Brtrue, first));
            Emit(il.Create(OpCodes.Ret));
        }
        // New guards can push old short branches outside their signed-byte range.
        foreach (var type in mod.Types) foreach (var method in type.Methods.Where(m => m.HasBody))
            foreach (var ins in method.Body.Instructions) {
                if (ins.OpCode.OperandType == OperandType.ShortInlineBrTarget) {
                    string name = ins.OpCode.Name.Replace(".s", "");
                    ins.OpCode = (OpCode)typeof(OpCodes).GetFields().First(f => f.FieldType == typeof(OpCode) && ((OpCode)f.GetValue(null)!).Name == name).GetValue(null)!;
                }
            }
        if (mod.AssemblyReferences.Any(r => r.Name == "System.Private.CoreLib" || r.Name == "System.Runtime"))
            throw new Exception("Host runtime reference leaked into game");

        string outPath = Path.Combine(managedDir, "Assembly-CSharp.patched.dll");
        asm.Write(outPath);
        asm.Dispose();
        File.Copy(outPath, dllPath, true);
        Console.WriteLine("Patched Assembly-CSharp.dll successfully!");

        var decompiler = new CSharpDecompiler(dllPath, new DecompilerSettings());
        foreach (var t in new[] { "UIShopSkillDescUpdater", "UIBuyCoinButton", "PurchaseManager", "GUIEventHandler", "GameEventHandler" }) {
            Console.WriteLine($"==================== {t} ====================");
            Console.WriteLine(decompiler.DecompileTypeAsString(new FullTypeName(t)));
        }
    }
}
