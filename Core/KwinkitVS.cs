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
    public enum CommandType { PrintLog, Wait, SetVariable, ToggleGameObject, RequireVariable, SetPosition, SetRotation, PlaySound, DestroyObject, GetInput, SetLightIntensity, Move }
    public enum ActiveMode { On, Off, Toggle }
    public enum VectorSource { Constant, GameObject }
    public enum AxisSource { Constant, Variable, KeepCurrent }

    // Move uses a short list of targets, actions and position sources.
    public enum MoveTarget { GameObject, Self, MainCamera, Player, Character }
    public enum MoveAction { Set, Add, Subtract }
    public enum MoveValue { GameObject, PlayerPosition, SelfPosition, MainCameraPosition, Vector3, X, Y, Z }

    [Serializable]
    public class VSNumberReference
    {
        public ValueSource source = ValueSource.Constant;
        public VariableStore store;
        public string variable = "";
    }

    [Serializable]
    public class VSAxisReference
    {
        public AxisSource source = AxisSource.Constant;
        public VariableStore store;
        public string variable = "";
    }

    public static class VSNumber
    {
        public static bool Resolve(float constant, VSNumberReference reference, out float result)
        {
            result = constant;
            if (reference == null || reference.source == ValueSource.Constant) return true;
            if (!VariableRuntime.TryResolve(reference.store, reference.variable, VariableType.Float, out var v)) return false;
            result = v.floatValue;
            return true;
        }
    }

    [Serializable]
    public class VSCommand
    {
        public bool enabled = true;
        public CommandType type;
        public string text = "";      // PrintLog message
        public float seconds = 1f;
        public VSNumberReference secondsRef = new VSNumberReference();
        public GameObject target;     // ToggleGameObject, SetPosition, SetRotation, DestroyObject
        public ActiveMode mode = ActiveMode.Toggle;
        public Vector3 vector;        // SetPosition: position, SetRotation: euler angles
        public bool useLocal;         // SetPosition / SetRotation: local space instead of world space
        public bool additive;         // SetPosition / SetRotation: add to the current value instead of replacing it
        public AudioClip clip;        // PlaySound
        public float volume = 1f;
        public VSNumberReference volumeRef = new VSNumberReference();     // PlaySound
        public bool waitForEnd;       // PlaySound: pause the sequence until the clip finishes
        public KeyCode key = KeyCode.Space; // GetInput
        public VariableStore store;   // SetVariable, RequireVariable
        public string variable = "";
        public SetOperation op = SetOperation.Set;
        public CompareOp compare = CompareOp.Equal;
        public VariableDef value = new VariableDef(); // operand: the value to set / add / compare against
        public ValueSource valueSource;
        public VariableStore operandStore;
        public string operandVariable = "";
        public VectorSource vectorSource;
        public GameObject referenceObject;
        public Vector3 offset;
        public Light lightTarget;
        public float intensity = 1f;
        public VSNumberReference intensityRef = new VSNumberReference();
        public VSAxisReference xRef = new VSAxisReference();
        public VSAxisReference yRef = new VSAxisReference();
        public VSAxisReference zRef = new VSAxisReference();
        public VSNumberReference offsetXRef = new VSNumberReference();
        public VSNumberReference offsetYRef = new VSNumberReference();
        public VSNumberReference offsetZRef = new VSNumberReference();

        // Move. Existing command fields above are kept for older scenes.
        public MoveTarget moveTarget = MoveTarget.Self;
        public Character moveCharacter;
        public MoveAction moveAction = MoveAction.Set;
        public MoveValue moveValue = MoveValue.GameObject;
        public Vector3 moveVector;
        public float moveAxisValue;
        public float duration;
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
        public VSNumberReference secondsRef = new VSNumberReference();
        public VariableStore store;                // OnVariableChange
        public string variable = "";
        public bool useCondition;                  // OnVariableChange: only fire when the new value passes the comparison
        public CompareOp compare = CompareOp.Equal;
        public VariableDef value = new VariableDef();
        public ValueSource valueSource;
        public VariableStore operandStore;
        public string operandVariable = "";
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
            if (useCondition && (!TryOperand(store, variable, value, valueSource, operandStore, operandVariable, out var resolved) ||
                !VariableRuntime.Check(store, variable, compare, resolved))) return;
            varPending = true;
        }

        internal void Tick(float dt)
        {
            if (varPending) { varPending = false; Fire(); }
            if (trigger == TriggerType.StartDelay && delayPending)
            {
                if (!VSNumber.Resolve(seconds, secondsRef, out var delay)) return;
                timer += dt;
                if (timer >= Mathf.Max(0f, delay)) { delayPending = false; Fire(); }
            }
            else if (trigger == TriggerType.OnInterval)
            {
                if (!VSNumber.Resolve(seconds, secondsRef, out var interval)) return;
                timer += dt;
                if (timer >= Mathf.Max(0.01f, interval)) { timer = 0f; Fire(); }
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
                        case CommandType.Wait:
                            if (VSNumber.Resolve(c.seconds, c.secondsRef, out var waitSeconds))
                                yield return new WaitForSeconds(Mathf.Max(0f, waitSeconds));
                            break;
                        case CommandType.RequireVariable:
                            if (!HasVariable(c) || !TryOperand(c.store, c.variable, c.value, c.valueSource, c.operandStore, c.operandVariable, out var checkedValue) ||
                                !VariableRuntime.Check(c.store, c.variable, c.compare, checkedValue)) yield break;
                            break;
                        case CommandType.GetInput:
                            yield return null; // skip the frame that started this sequence, so the same key press is not counted twice
                            while (!VSInput.KeyDown(c.key)) { if (this == null) yield break; yield return null; }
                            break;
                        case CommandType.PlaySound:
                            if (PlaySound(c) && c.waitForEnd) yield return new WaitForSeconds(c.clip.length);
                            break;
                        case CommandType.Move:
                            if (c.duration <= 0f)
                            {
                                if (TryMove(c, out var instantTarget, out var instantEnd))
                                    instantTarget.position = instantEnd;
                            }
                            else yield return Move(c);
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

        static bool TryOperand(VariableStore targetStore, string targetName, VariableDef constant,
            ValueSource source, VariableStore operandStore, string operandName, out VariableDef result)
        {
            result = constant;
            if (source == ValueSource.Constant) return true;
            var target = VariableRuntime.Find(targetStore, targetName);
            return target != null && VariableRuntime.TryResolve(operandStore, operandName, target.type, out result);
        }

        static bool ResolveAxis(float constant, float current, VSAxisReference axis, out float result)
        {
            result = constant;
            if (axis == null || axis.source == AxisSource.Constant) return true;
            if (axis.source == AxisSource.KeepCurrent) { result = current; return true; }
            if (!VariableRuntime.TryResolve(axis.store, axis.variable, VariableType.Float, out var found)) return false;
            result = found.floatValue;
            return true;
        }

        bool TryVector(VSCommand c, Vector3 current, out Vector3 v)
        {
            v = c.vector;
            if (c.vectorSource == VectorSource.GameObject)
            {
                if (c.referenceObject == null)
                {
                    Debug.LogWarning("[Kwinkit-VS] Position/rotation reference GameObject is missing.", this);
                    return false;
                }
                var tr = c.referenceObject.transform;
                v = c.type == CommandType.SetPosition
                    ? (c.useLocal ? tr.localPosition : tr.position)
                    : (c.useLocal ? tr.localEulerAngles : tr.eulerAngles);
                if (!VSNumber.Resolve(c.offset.x, c.offsetXRef, out var ox) ||
                    !VSNumber.Resolve(c.offset.y, c.offsetYRef, out var oy) ||
                    !VSNumber.Resolve(c.offset.z, c.offsetZRef, out var oz)) return false;
                v += new Vector3(ox, oy, oz);
                return true;
            }
            // KeepCurrent preserves the axis for absolute assignment; for additive moves,
            // it contributes zero so that axis is not modified.
            Vector3 fallback = c.additive ? Vector3.zero : current;
            if (!ResolveAxis(c.vector.x, fallback.x, c.xRef, out var x) ||
                !ResolveAxis(c.vector.y, fallback.y, c.yRef, out var y) ||
                !ResolveAxis(c.vector.z, fallback.z, c.zRef, out var z)) return false;
            v = new Vector3(x, y, z);
            return true;
        }

        // Resolve references when Move runs, then move to the sampled destination.
        Transform MoveTransform(VSCommand c)
        {
            switch (c.moveTarget)
            {
                case MoveTarget.Self: return transform;
                case MoveTarget.MainCamera:
                    var camera = Camera.main;
                    return camera != null ? camera.transform : null;
                case MoveTarget.Player:
                    var player = Character.Player;
                    return player != null ? player.transform : null;
                case MoveTarget.Character:
                    return c.moveCharacter != null ? c.moveCharacter.transform : null;
                default: return c.target != null ? c.target.transform : null;
            }
        }

        Transform MoveSource(VSCommand c)
        {
            switch (c.moveValue)
            {
                case MoveValue.SelfPosition: return transform;
                case MoveValue.MainCameraPosition:
                    var camera = Camera.main;
                    return camera != null ? camera.transform : null;
                case MoveValue.PlayerPosition:
                    var player = Character.Player;
                    return player != null ? player.transform : null;
                default: return c.referenceObject != null ? c.referenceObject.transform : null;
            }
        }

        bool TryMove(VSCommand c, out Transform targetTransform, out Vector3 end)
        {
            end = Vector3.zero;
            targetTransform = MoveTransform(c);
            if (targetTransform == null || !targetTransform.gameObject.scene.IsValid())
            {
                Debug.LogWarning("[Kwinkit-VS] Move: Transform missing or not in a scene.", this);
                return false;
            }

            Vector3 start = targetTransform.position;
            Vector3 value;
            bool singleAxis = c.moveValue == MoveValue.X || c.moveValue == MoveValue.Y || c.moveValue == MoveValue.Z;

            if (c.moveValue == MoveValue.Vector3)
                value = c.moveVector;
            else if (singleAxis)
            {
                value = Vector3.zero;
                if (c.moveValue == MoveValue.X) value.x = c.moveAxisValue;
                else if (c.moveValue == MoveValue.Y) value.y = c.moveAxisValue;
                else value.z = c.moveAxisValue;
            }
            else
            {
                var source = MoveSource(c);
                if (source == null)
                {
                    Debug.LogWarning("[Kwinkit-VS] Move: Value source not found.", this);
                    return false;
                }
                value = source.position;
            }

            if (c.moveAction == MoveAction.Add) end = start + value;
            else if (c.moveAction == MoveAction.Subtract) end = start - value;
            else if (singleAxis)
            {
                // Set a single coordinate without altering the other two.
                end = start;
                if (c.moveValue == MoveValue.X) end.x = value.x;
                else if (c.moveValue == MoveValue.Y) end.y = value.y;
                else end.z = value.z;
            }
            else end = value;
            return true;
        }

        // Longer Move commands run over time; later commands wait for completion.
        IEnumerator Move(VSCommand c)
        {
            if (!TryMove(c, out var targetTransform, out var end)) yield break;
            var start = targetTransform.position;
            float duration = Mathf.Max(0f, c.duration);
            float elapsed = 0f;
            while (elapsed < duration)
            {
                if (targetTransform == null) yield break;
                elapsed += Time.deltaTime;
                targetTransform.position = Vector3.Lerp(start, end, Mathf.Clamp01(elapsed / duration));
                if (elapsed < duration) yield return null;
            }
        }

        // Instant commands.
        void Apply(VSCommand c)
        {
            switch (c.type)
            {
                case CommandType.PrintLog: Debug.Log("[Kwinkit-VS] " + c.text, this); break;
                case CommandType.SetVariable:
                    if (!HasVariable(c)) break;
                    if (c.op == SetOperation.Toggle)
                        VariableRuntime.Apply(c.store, c.variable, c.op, c.value);
                    else if (TryOperand(c.store, c.variable, c.value, c.valueSource,
                        c.operandStore, c.operandVariable, out var setValue))
                        VariableRuntime.Apply(c.store, c.variable, c.op, setValue);
                    break;
                case CommandType.ToggleGameObject:
                    if (HasTarget(c)) c.target.SetActive(c.mode == ActiveMode.Toggle ? !c.target.activeSelf : c.mode == ActiveMode.On);
                    break;
                case CommandType.SetPosition:
                    {
                        if (!HasTarget(c)) break;
                        var tr = c.target.transform;
                        if (!TryVector(c, c.useLocal ? tr.localPosition : tr.position, out var position)) break;
                        if (c.useLocal) tr.localPosition = c.additive ? tr.localPosition + position : position;
                        else tr.position = c.additive ? tr.position + position : position;
                        break;
                    }
                case CommandType.SetRotation:
                    {
                        if (!HasTarget(c)) break;
                        var tr = c.target.transform;
                        if (!TryVector(c, c.useLocal ? tr.localEulerAngles : tr.eulerAngles, out var rotation)) break;
                        var q = Quaternion.Euler(rotation);
                        if (c.useLocal) tr.localRotation = c.additive ? tr.localRotation * q : q;
                        else tr.rotation = c.additive ? q * tr.rotation : q;
                        break;
                    }
                case CommandType.SetLightIntensity:
                    if (c.lightTarget == null) Debug.LogWarning("[Kwinkit-VS] Set Light Intensity needs a Light.", this);
                    else if (VSNumber.Resolve(c.intensity, c.intensityRef, out var intensity))
                        c.lightTarget.intensity = Mathf.Max(0f, intensity);
                    break;
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
            if (!VSNumber.Resolve(c.volume, c.volumeRef, out var volume)) { Destroy(go); return false; }
            src.clip = c.clip; src.volume = Mathf.Clamp01(volume); src.spatialBlend = 0f;
            src.Play();
            Destroy(go, c.clip.length + 0.1f);
            return true;
        }
    }
}