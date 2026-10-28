// Kwinkit-VS / Core / KwinkitVS.cs
// Component-based visual logic: WHEN (trigger) -> THEN (ordered commands).
// Add Component > "Kwinkit-VS" (or GameObject > Kwinkit-VS > New Logic Object).
// A manager (DontDestroyOnLoad) is created automatically at runtime and drives every component.
// Variables live in Variable Store assets (see VariableStore.cs).
// Optional icons live in Kwinkit-VS/Icons/<name>.png (see icon list); built-in fallbacks are used if missing.
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Kwinkit.VS
{
    // Enum values keep their slots so older scenes still load: SetBool / RequireBool / OnBoolChange became the *Variable versions.
    public enum TriggerType { OnStart, StartDelay, OnInterval, OnVariableChange, OnKeyPress }
    public enum CommandType { PrintLog, Wait, SetVariable, ToggleGameObject, RequireVariable, SetPosition, SetRotation, PlaySound, DestroyObject, GetInput }
    public enum ActiveMode { On, Off, Toggle }

    [Serializable]
    public class VSCommand
    {
        public bool enabled = true;
        public CommandType type;
        public string text = "";      // PrintLog message
        public float seconds = 1f;
        public GameObject target;     // ToggleGameObject, SetPosition, SetRotation, DestroyObject
        public ActiveMode mode = ActiveMode.Toggle;
        public Vector3 vector;        // SetPosition: position, SetRotation: euler angles
        public bool useLocal;         // SetPosition / SetRotation: local space instead of world space
        public bool additive;         // SetPosition / SetRotation: add to the current value instead of replacing it
        public AudioClip clip;        // PlaySound
        public float volume = 1f;     // PlaySound
        public bool waitForEnd;       // PlaySound: pause the sequence until the clip finishes
        public KeyCode key = KeyCode.Space; // GetInput
        public VariableStore store;   // SetVariable, RequireVariable
        public string variable = "";
        public SetOperation op = SetOperation.Set;
        public CompareOp compare = CompareOp.Equal;
        public VariableDef value = new VariableDef(); // operand: the value to set / add / compare against
    }

    /// <summary>Keyboard polling that works with the legacy Input Manager and, when it is the only active handler, the Input System package.</summary>
    public static class VSInput
    {
        public static bool KeyDown(KeyCode k)
        {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb == null) return false;
            string n = k.ToString();
            if (k == KeyCode.Return) n = "Enter";
            else if (n.StartsWith("Alpha")) n = "Digit" + n.Substring(5);
            return Enum.TryParse(n, out UnityEngine.InputSystem.Key key) && key != UnityEngine.InputSystem.Key.None && kb[key].wasPressedThisFrame;
#else
            return Input.GetKeyDown(k);
#endif
        }
    }

    /// <summary>Omnipresent runtime driver. Runs command sequences, ticks timers and saves variables for all KwinkitVS components.</summary>
    [AddComponentMenu("")]
    public class KwinkitVSManager : MonoBehaviour
    {
        static KwinkitVSManager instance;
        static bool quitting;
        readonly List<KwinkitVS> registry = new List<KwinkitVS>();
        readonly List<KwinkitVS> scratch = new List<KwinkitVS>();

        public static KwinkitVSManager Instance
        {
            get
            {
                if (instance == null && !quitting && Application.isPlaying)
                {
                    var go = new GameObject("[Kwinkit-VS Manager]");
                    DontDestroyOnLoad(go);
                    instance = go.AddComponent<KwinkitVSManager>();
                }
                return instance;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { instance = null; quitting = false; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { _ = Instance; }

        public static void Register(KwinkitVS c) { var m = Instance; if (m != null && !m.registry.Contains(c)) m.registry.Add(c); }
        public static void Unregister(KwinkitVS c) { if (instance != null) instance.registry.Remove(c); }

        void OnApplicationPause(bool paused) { if (paused) VariableRuntime.Flush(); }
        void OnApplicationQuit() { VariableRuntime.Flush(); quitting = true; }
        void OnDestroy() { if (instance == this) instance = null; }

        void Update()
        {
            scratch.Clear(); scratch.AddRange(registry); // snapshot: commands may enable/disable components
            float dt = Time.deltaTime;
            foreach (var c in scratch) if (c != null && c.isActiveAndEnabled) c.Tick(dt);
        }

        void LateUpdate() { VariableRuntime.Flush(); } // batches all variable changes of the frame into one write per store
    }

    [AddComponentMenu("Kwinkit-VS")]
    public class KwinkitVS : MonoBehaviour
    {
        public TriggerType trigger = TriggerType.OnStart;
        public float seconds = 1f;                 // StartDelay: delay, OnInterval: period
        public VariableStore store;                // OnVariableChange
        public string variable = "";
        public bool useCondition;                  // OnVariableChange: only fire when the new value passes the comparison
        public CompareOp compare = CompareOp.Equal;
        public VariableDef value = new VariableDef();
        public KeyCode key = KeyCode.Space;        // OnKeyPress
        public bool runOnce;
        public bool allowOverlap;
        public List<VSCommand> commands = new List<VSCommand>();

        bool running, hasFired, delayPending, varPending;
        float timer;
        public bool IsRunning => running;

        void OnEnable() { VariableRuntime.Changed += OnVariable; KwinkitVSManager.Register(this); }
        void OnDisable() { VariableRuntime.Changed -= OnVariable; KwinkitVSManager.Unregister(this); }

        void Start()
        {
            timer = 0f;
            if (trigger == TriggerType.OnStart) Fire();
            else if (trigger == TriggerType.StartDelay) delayPending = true;
        }

        // Deferred to Tick() so chained variable triggers cannot recurse within one call stack.
        void OnVariable(VariableStore s, string name)
        {
            if (trigger != TriggerType.OnVariableChange || s != store || name != variable) return;
            if (useCondition && !VariableRuntime.Check(store, variable, compare, value)) return;
            varPending = true;
        }

        internal void Tick(float dt)
        {
            if (varPending) { varPending = false; Fire(); }
            if (trigger == TriggerType.StartDelay && delayPending)
            {
                timer += dt;
                if (timer >= seconds) { delayPending = false; Fire(); }
            }
            else if (trigger == TriggerType.OnInterval)
            {
                timer += dt;
                if (timer >= Mathf.Max(0.01f, seconds)) { timer = 0f; Fire(); }
            }
            else if (trigger == TriggerType.OnKeyPress && VSInput.KeyDown(key)) Fire();
        }

        /// <summary>Runs the command list now. Also callable from code or UnityEvents.</summary>
        public void Fire()
        {
            if (!isActiveAndEnabled) return;
            if (runOnce && hasFired) return;
            if (running && !allowOverlap) return;
            var m = KwinkitVSManager.Instance;
            if (m == null) return;
            hasFired = true;
            m.StartCoroutine(Execute()); // runs on the manager so deactivating this object cannot kill the sequence
        }

        IEnumerator Execute()
        {
            running = true;
            try
            {
                for (int i = 0; i < commands.Count; i++)
                {
                    if (this == null) yield break;
                    var c = commands[i];
                    if (!c.enabled) continue;
                    switch (c.type)
                    {
                        // commands that pause or stop the sequence need yield, so they live here
                        case CommandType.Wait: yield return new WaitForSeconds(Mathf.Max(0f, c.seconds)); break;
                        case CommandType.RequireVariable:
                            if (!HasVariable(c) || !VariableRuntime.Check(c.store, c.variable, c.compare, c.value)) yield break;
                            break;
                        case CommandType.GetInput:
                            yield return null; // skip the frame that started this sequence, so the same key press is not counted twice
                            while (!VSInput.KeyDown(c.key)) { if (this == null) yield break; yield return null; }
                            break;
                        case CommandType.PlaySound:
                            if (PlaySound(c) && c.waitForEnd) yield return new WaitForSeconds(c.clip.length);
                            break;
                        default: Apply(c); break;
                    }
                }
            }
            finally { running = false; }
        }

        bool HasTarget(VSCommand c)
        {
            if (c.target != null) return true;
            Debug.LogWarning("[Kwinkit-VS] " + c.type + " has no target.", this);
            return false;
        }

        bool HasVariable(VSCommand c)
        {
            if (c.store != null && !string.IsNullOrEmpty(c.variable)) return true;
            Debug.LogWarning("[Kwinkit-VS] " + c.type + " needs a Variable Store and a variable.", this);
            return false;
        }

        // Instant commands.
        void Apply(VSCommand c)
        {
            switch (c.type)
            {
                case CommandType.PrintLog: Debug.Log("[Kwinkit-VS] " + c.text, this); break;
                case CommandType.SetVariable:
                    if (HasVariable(c)) VariableRuntime.Apply(c.store, c.variable, c.op, c.value);
                    break;
                case CommandType.ToggleGameObject:
                    if (HasTarget(c)) c.target.SetActive(c.mode == ActiveMode.Toggle ? !c.target.activeSelf : c.mode == ActiveMode.On);
                    break;
                case CommandType.SetPosition:
                    {
                        if (!HasTarget(c)) break;
                        var tr = c.target.transform;
                        if (c.useLocal) tr.localPosition = c.additive ? tr.localPosition + c.vector : c.vector;
                        else tr.position = c.additive ? tr.position + c.vector : c.vector;
                        break;
                    }
                case CommandType.SetRotation:
                    {
                        if (!HasTarget(c)) break;
                        var tr = c.target.transform;
                        var q = Quaternion.Euler(c.vector);
                        if (c.useLocal) tr.localRotation = c.additive ? tr.localRotation * q : q;
                        else tr.rotation = c.additive ? q * tr.rotation : q;
                        break;
                    }
                case CommandType.DestroyObject:
                    if (!HasTarget(c)) break;
                    if (!c.target.scene.IsValid()) { Debug.LogWarning("[Kwinkit-VS] Destroy Object needs a scene object, not a prefab asset.", this); break; }
                    Destroy(c.target);
                    break;
            }
        }

        // Plays the clip as non-spatial (2D) audio on a temporary object. Returns false when no clip is assigned.
        bool PlaySound(VSCommand c)
        {
            if (c.clip == null) { Debug.LogWarning("[Kwinkit-VS] Play Sound has no clip.", this); return false; }
            var go = new GameObject("[Kwinkit-VS Sound]");
            var src = go.AddComponent<AudioSource>();
            src.clip = c.clip; src.volume = Mathf.Clamp01(c.volume); src.spatialBlend = 0f;
            src.Play();
            Destroy(go, c.clip.length + 0.1f);
            return true;
        }
    }
}