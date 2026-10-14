// Test doubles shaped after the members Potassium 1.0.0 references. Not game code.
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public enum GTZone
{
    forest,
    city,
    monkeBlocks,
    monkeBlocksShared,
}

public class ZoneManagement
{
    public static ZoneManagement instance;
    public HashSet<GTZone> Active = new HashSet<GTZone>();
    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool IsZoneActive(GTZone zone) => Active.Contains(zone);
}

namespace GorillaTagScripts.Builder
{
    public class SharedBlocksManager
    {
        public int StartCalls;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Start() => StartCalls++;
        public void InvokeStart() => Start(); // what Unity would do
    }
}

/// <summary>Stand-in with only the members CRIX's desktop-camera option looks up (no Unity objects in tests).</summary>
public class GorillaTagger
{
    public static GorillaTagger Instance { get; set; }
    public object thirdPersonCamera;
}

public class NetworkSystem
{
    public static NetworkSystem Instance { get; set; }
    public bool InRoom { get; set; }
}

public class NetPlayer
{
    public bool IsLocal { get; set; }
    public bool InRoom { get; set; } = true;
}

public class VRRig
{
    public int BuildInitializeCalls;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void BuildInitialize() => BuildInitializeCalls++;
}

public class RigContainer
{
    private readonly VRRig rig = new VRRig();
    public VRRig Rig => rig;
}

public class VRRigCache
{
    public static bool isInitialized;
    private static Queue<RigContainer> freeRigs = new Queue<RigContainer>();
    private static Dictionary<NetPlayer, RigContainer> rigsInUse = new Dictionary<NetPlayer, RigContainer>();
    private static readonly List<RigContainer> allRigContainers = new List<RigContainer>();
    public static event Action OnPostSpawnRig;
    public static int PostSpawnEvents;

    private int rigAmount = 19;

    public static List<RigContainer> AllRigContainers => allRigContainers;

    public static void ResetForTest()
    {
        isInitialized = false;
        freeRigs.Clear();
        rigsInUse.Clear();
        allRigContainers.Clear();
        PostSpawnEvents = 0;
        OnPostSpawnRig = () => PostSpawnEvents++;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void InitializeVRRigCache()
    {
        allRigContainers.Add(new RigContainer()); // local rig
        for (int i = 0; i < rigAmount; i++)
        {
            RigContainer rig = SpawnRig();
            rig.Rig.BuildInitialize();
            freeRigs.Enqueue(rig);
        }
        isInitialized = true;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private RigContainer SpawnRig()
    {
        var container = new RigContainer();
        allRigContainers.Add(container);
        return container;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public bool TryGetVrrig(NetPlayer targetPlayer, out RigContainer playerRig)
    {
        if (rigsInUse.TryGetValue(targetPlayer, out playerRig))
            return true;
        if (freeRigs.Count == 0)
            return false;
        playerRig = freeRigs.Dequeue();
        rigsInUse[targetPlayer] = playerRig;
        return true;
    }

    public static int FreeCount => freeRigs.Count;
}

public class GorillaScoreBoard
{
    public int Updates;
}

public class GorillaScoreboardTotalUpdater
{
    public static List<GorillaScoreBoard> allScoreboards = new List<GorillaScoreBoard>();
    public static GorillaScoreboardTotalUpdater instance;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void UpdateActiveScoreboards()
    {
        foreach (GorillaScoreBoard board in allScoreboards)
            instance.UpdateScoreboard(board);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void UpdateScoreboard(GorillaScoreBoard board) => board.Updates++;
}

namespace GorillaNetworking
{
    public class GorillaScoreboardSpawner
    {
        public int Joins;
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void OnJoinedRoom() => Joins++;
    }

    public class CosmeticsThrottler
    {
        public float DrawAllDistance = 10f;
        public float MaxDrawDistance { get; set; } = 20f;
        public int DrawAllCount = 10;
        public int DrawMaxCount { get; set; } = 19;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Awake() { }
        public void InvokeAwake() => Awake();
    }
}

public class GameEntityManager
{
    public static GameEntityManager activeManager { get; set; }
    public int ScanCount;
    [MethodImpl(MethodImplOptions.NoInlining)]
    public void OnRigDeactivated(RigContainer container) => ScanCount++;
}

public class RoomSystem
{
    public static int LeftRoomCalls;

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void OnLeftRoom()
    {
        LeftRoomCalls++;
        GC.Collect(0);
        GC.Collect();
    }

    public static void InvokeOnLeftRoom() => OnLeftRoom();
}

public class SimpleUnloadUnusedAssets
{
    public int Unloads;
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void OnEnable() => Unloads++;
    public void InvokeOnEnable() => OnEnable();
}

/// <summary>Stand-in for a map object: a layer and a set of component types.</summary>
public class FakeMapObject
{
    public int Layer;
    public List<Type> Components = new List<Type>();
}

public class CustomMapLoader
{
    private static List<int> APPROVED_LAYERS = new List<int> { 0, 1, 2, 4, 5, 9, 11, 18, 20 };
    private static List<Type> componentAllowlist = new List<Type> { typeof(string), typeof(int), typeof(Uri), typeof(Version) };
    private static List<string> componentTypeStringAllowList = new List<string> { "Collections.Generic", "Text.StringBuilder" };

    public static List<Type> Allowlist => componentAllowlist;

    // Same shape as the game's (object here instead of GameObject so it runs without Unity).
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool SanitizeObject(object gameObject, object mapRoot)
    {
        var obj = (FakeMapObject)gameObject;
        if (!APPROVED_LAYERS.Contains(obj.Layer))
            obj.Layer = 0;
        foreach (Type component in obj.Components)
        {
            bool allowed = componentAllowlist.Contains(component);
            if (!allowed)
            {
                string name = component.ToString();
                foreach (string fragment in componentTypeStringAllowList)
                {
                    if (name.Contains(fragment))
                    {
                        allowed = true;
                        break;
                    }
                }
            }
            if (!allowed)
                return false;
        }
        return true;
    }

    public static bool Invoke(FakeMapObject obj) => SanitizeObject(obj, null);
}

namespace GorillaTag
{
    public class TextureTransitionerManager
    {
        public static int Updates;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void LateUpdate() => Updates++;
        public void Tick() => LateUpdate();
    }
}

public class VoiceLoudnessReactor
{
    private float attack;
    private float decay;
    public int Updates;

    public VoiceLoudnessReactor(float attack = 0f, float decay = 0f)
    {
        this.attack = attack;
        this.decay = decay;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Update() => Updates++;
    public void Tick() => Update();
}

namespace GorillaTag.Cosmetics
{
    public class EnvironmentProximityReactor
    {
        public int Updates;
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Update() => Updates++;
        public void Tick() => Update();
    }
}
