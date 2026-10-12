// Kwinkit-VS / Core / KwinkitVS.cs
// Component-based visual logic: WHEN (trigger) -> THEN (ordered commands).
// Add Component > "Kwinkit-VS" (or GameObject > Kwinkit-VS > New Logic Object).
// A hidden-lifetime manager (DontDestroyOnLoad) is created automatically at runtime and drives every component.
// Optional icons live in Kwinkit-VS/Icons/<name>.png (see icon list); built-in fallbacks are used if missing.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Kwinkit.VS
{
    public enum TriggerType { OnStart, StartDelay, OnInterval, OnBoolChange }
    public enum BoolChange { Any, BecomesTrue, BecomesFalse }
    public enum CommandType { PrintLog, Wait, SetBool, ToggleGameObject, RequireBool }
    public enum ActiveMode { On, Off, Toggle }

    [Serializable]
    public class VSCommand
    {
        public bool enabled = true;
        public CommandType type;
        public string text = "";      // log message, or bool key
        public bool boolValue = true;
        public float seconds = 1f;
        public GameObject target;
        public ActiveMode mode = ActiveMode.Toggle;
    }

    /// <summary>Named booleans persisted to a JSON file in persistentDataPath.</summary>
    public static class BoolStore
    {
        [Serializable] public class Entry { public string key; public bool value; }
        [Serializable] public class Data { public List<Entry> entries = new List<Entry>(); }

        static Dictionary<string, bool> cache;
        public static event Action<string, bool> Changed;
        public static string FilePath => Path.Combine(Application.persistentDataPath, "kwinkit_vs_bools.json");

        static void Load()
        {
            if (cache != null) return;
            cache = new Dictionary<string, bool>();
            try
            {
                if (!File.Exists(FilePath)) return;
                var d = JsonUtility.FromJson<Data>(File.ReadAllText(FilePath));
                if (d != null) foreach (var e in d.entries) cache[e.key] = e.value;
            }
            catch (Exception ex) { Debug.LogWarning("[Kwinkit-VS] Could not load bools: " + ex.Message); }
        }

        static void Save()
        {
            var d = new Data();
            foreach (var kv in cache) d.entries.Add(new Entry { key = kv.Key, value = kv.Value });
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(d, true)); }
            catch (Exception ex) { Debug.LogWarning("[Kwinkit-VS] Could not save bools: " + ex.Message); }
        }

        public static bool Get(string key, bool fallback = false) { Load(); return cache.TryGetValue(key ?? "", out var v) ? v : fallback; }

        public static void Set(string key, bool value)
        {
            if (string.IsNullOrEmpty(key)) return;
            Load();
            bool existed = cache.TryGetValue(key, out var old);
            if (existed && old == value) return;
            cache[key] = value; Save();
            if (old != value) Changed?.Invoke(key, value); // an unset key counts as false
        }

        public static void ClearAll() { cache = new Dictionary<string, bool>(); Save(); }
    }

    /// <summary>Omnipresent runtime driver. Runs command sequences and ticks timers for all KwinkitVS components.</summary>
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

        void OnApplicationQuit() { quitting = true; }
        void OnDestroy() { if (instance == this) instance = null; }

        void Update()
        {
            scratch.Clear(); scratch.AddRange(registry); // snapshot: commands may enable/disable components
            float dt = Time.deltaTime;
            foreach (var c in scratch) if (c != null && c.isActiveAndEnabled) c.Tick(dt);
        }
    }

    [AddComponentMenu("Kwinkit-VS")]
    public class KwinkitVS : MonoBehaviour
    {
        public TriggerType trigger = TriggerType.OnStart;
        public float seconds = 1f;                 // StartDelay: delay, OnInterval: period
        public string boolKey = "";                // OnBoolChange
        public BoolChange boolChange = BoolChange.Any;
        public bool runOnce;
        public bool allowOverlap;
        public List<VSCommand> commands = new List<VSCommand>();

        bool running, hasFired, delayPending, boolPending;
        float timer;
        public bool IsRunning => running;

        void OnEnable() { BoolStore.Changed += OnBool; KwinkitVSManager.Register(this); }
        void OnDisable() { BoolStore.Changed -= OnBool; KwinkitVSManager.Unregister(this); }

        void Start()
        {
            timer = 0f;
            if (trigger == TriggerType.OnStart) Fire();
            else if (trigger == TriggerType.StartDelay) delayPending = true;
        }

        // Deferred to Tick() so chained bool triggers cannot recurse within one call stack.
        void OnBool(string key, bool value)
        {
            if (trigger != TriggerType.OnBoolChange || key != boolKey) return;
            if (boolChange == BoolChange.BecomesTrue && !value) return;
            if (boolChange == BoolChange.BecomesFalse && value) return;
            boolPending = true;
        }

        internal void Tick(float dt)
        {
            if (boolPending) { boolPending = false; Fire(); }
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
                        case CommandType.PrintLog: Debug.Log("[Kwinkit-VS] " + c.text, this); break;
                        case CommandType.Wait: yield return new WaitForSeconds(Mathf.Max(0f, c.seconds)); break;
                        case CommandType.SetBool: BoolStore.Set(c.text, c.boolValue); break;
                        case CommandType.RequireBool: if (BoolStore.Get(c.text) != c.boolValue) yield break; break;
                        case CommandType.ToggleGameObject:
                            if (c.target == null) { Debug.LogWarning("[Kwinkit-VS] Toggle GameObject has no target.", this); break; }
                            c.target.SetActive(c.mode == ActiveMode.Toggle ? !c.target.activeSelf : c.mode == ActiveMode.On);
                            break;
                    }
                }
            }
            finally { running = false; }
        }
    }

#if UNITY_EDITOR
    // ------------------------------------------------------------------ editor UI helpers
    static class KwinkitUI
    {
        static string root;
        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();
        static GUIStyle cardStyle, bannerTitle, bannerSub, section, bigButton;

        public static bool Pro => EditorGUIUtility.isProSkin;
        public static Color CardBg => Pro ? new Color(.21f, .21f, .23f) : new Color(.80f, .80f, .82f);
        public static Color IconTint => Pro ? new Color(.85f, .85f, .88f) : new Color(.25f, .25f, .28f);
        public static void ClearCache() { cache.Clear(); }

        // Resolved from this script's location, so the Kwinkit-VS folder can live anywhere under Assets/.
        static string Root
        {
            get
            {
                if (root != null) return root;
                root = "Assets/Kwinkit-VS";
                foreach (var g in AssetDatabase.FindAssets("KwinkitVS t:MonoScript"))
                {
                    var p = AssetDatabase.GUIDToAssetPath(g);
                    if (Path.GetFileNameWithoutExtension(p) != "KwinkitVS") continue;
                    root = Path.GetDirectoryName(Path.GetDirectoryName(p)).Replace('\\', '/');
                    break;
                }
                return root;
            }
        }

        public static Texture Icon(string name, string builtin = null)
        {
            if (!cache.ContainsKey(name)) cache[name] = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Icons/" + name + ".png");
            var t = cache[name];
            if (t != null) return t;
            return builtin != null ? EditorGUIUtility.FindTexture(builtin) : null;
        }

        public static GUIStyle Card => cardStyle ??= new GUIStyle { padding = new RectOffset(14, 8, 6, 7), margin = new RectOffset(4, 4, 3, 3) };
        public static GUIStyle BannerTitle => bannerTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Color.white } };
        public static GUIStyle BannerSub => bannerSub ??= new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(.6f, .65f, .75f) } };
        public static GUIStyle Section => section ??= new GUIStyle(EditorStyles.miniBoldLabel) { margin = new RectOffset(6, 0, 6, 2) };
        public static GUIStyle BigButton => bigButton ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };

        public static void BeginCard(Color accent)
        {
            var r = EditorGUILayout.BeginVertical(Card);
            if (Event.current.type != EventType.Repaint) return;
            EditorGUI.DrawRect(r, CardBg);
            EditorGUI.DrawRect(new Rect(r.x, r.y, 4, r.height), accent);
        }
        public static void EndCard() { EditorGUILayout.EndVertical(); }

        public static void IconBox(string icon, string builtin, Color tint)
        {
            var old = GUI.contentColor; GUI.contentColor = tint;
            GUILayout.Label(Icon(icon, builtin), GUILayout.Width(18), GUILayout.Height(18));
            GUI.contentColor = old;
        }

        public static bool IconButton(string icon, string fallbackText, string tip)
        {
            var tex = Icon(icon);
            var c = tex != null ? new GUIContent(tex, tip) : new GUIContent(fallbackText, tip);
            var oc = GUI.contentColor; var ob = GUI.backgroundColor;
            GUI.contentColor = IconTint; GUI.backgroundColor = Pro ? new Color(.3f, .3f, .34f) : new Color(.9f, .9f, .92f);
            bool hit = GUILayout.Button(c, EditorStyles.miniButton, GUILayout.Width(24), GUILayout.Height(18));
            GUI.contentColor = oc; GUI.backgroundColor = ob;
            return hit;
        }

        public static void Warn(string msg)
        {
            EditorGUILayout.BeginHorizontal();
            IconBox("error", "console.erroricon.sml", new Color(1f, .45f, .4f));
            var old = GUI.contentColor; GUI.contentColor = new Color(1f, .5f, .45f);
            GUILayout.Label(msg, EditorStyles.miniLabel);
            GUI.contentColor = old;
            EditorGUILayout.EndHorizontal();
        }
    }

    [CustomEditor(typeof(KwinkitVS))]
    public class KwinkitVSEditor : Editor
    {
        SerializedProperty cmds;
        static readonly Color TriggerColor = new Color(.30f, .75f, .45f);

        static string TriggerIcon(TriggerType t) => t switch { TriggerType.OnStart => "play", TriggerType.StartDelay => "hourglass", TriggerType.OnInterval => "repeat", _ => "bool" };
        static string TriggerBuiltin(TriggerType t) => t switch { TriggerType.OnStart => "d_PlayButton", TriggerType.OnInterval => "d_preAudioLoopOff", _ => null };
        static string CmdIcon(CommandType t) => t switch { CommandType.PrintLog => "log", CommandType.Wait => "timer", CommandType.SetBool => "save", CommandType.ToggleGameObject => "eye", _ => "shield" };
        static string CmdBuiltin(CommandType t) => t switch { CommandType.PrintLog => "d_UnityEditor.ConsoleWindow", CommandType.ToggleGameObject => "d_scenevis_visible_hover", _ => null };
        static string CmdLabel(CommandType t) => t switch { CommandType.PrintLog => "Print Log", CommandType.Wait => "Wait", CommandType.SetBool => "Set Bool (saved)", CommandType.ToggleGameObject => "Toggle GameObject", _ => "Require Bool (stop if not)" };
        static Color CmdColor(CommandType t) => t switch
        {
            CommandType.PrintLog => new Color(.30f, .60f, 1f), CommandType.Wait => new Color(.65f, .65f, .72f),
            CommandType.SetBool => new Color(.70f, .50f, 1f), CommandType.ToggleGameObject => new Color(1f, .62f, .25f), _ => new Color(1f, .82f, .25f)
        };

        void OnEnable() { cmds = serializedObject.FindProperty("commands"); KwinkitUI.ClearCache(); }
        public override bool RequiresConstantRepaint() => Application.isPlaying;
        SerializedProperty P(string n) => serializedObject.FindProperty(n);

        public override void OnInspectorGUI()
        {
            var t = (KwinkitVS)target;
            serializedObject.Update();
            var oldIcon = EditorGUIUtility.GetIconSize(); var oldLabel = EditorGUIUtility.labelWidth;
            EditorGUIUtility.SetIconSize(new Vector2(14, 14)); EditorGUIUtility.labelWidth = 90;

            Banner();
            DrawTrigger();
            GUILayout.Label("▼", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
            DrawCommands(t);
            DrawFooter(t);

            EditorGUIUtility.labelWidth = oldLabel; EditorGUIUtility.SetIconSize(oldIcon);
            serializedObject.ApplyModifiedProperties();
        }

        void Banner()
        {
            var r = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(.09f, .10f, .13f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(.30f, .62f, 1f));
            float x = r.x + 10;
            var logo = KwinkitUI.Icon("logo");
            if (logo != null) { GUI.DrawTexture(new Rect(x, r.y + 7, 24, 24), logo, ScaleMode.ScaleToFit); x += 32; }
            GUI.Label(new Rect(x, r.y + 3, r.width - x, 18), "KWINKIT-VS", KwinkitUI.BannerTitle);
            GUI.Label(new Rect(x, r.y + 20, r.width - x, 16), "WHEN trigger  →  THEN run commands in order", KwinkitUI.BannerSub);
        }

        void DrawTrigger()
        {
            var trig = P("trigger");
            var type = (TriggerType)trig.enumValueIndex;
            KwinkitUI.BeginCard(TriggerColor);
            EditorGUILayout.BeginHorizontal();
            KwinkitUI.IconBox(TriggerIcon(type), TriggerBuiltin(type), TriggerColor);
            GUILayout.Label("WHEN", EditorStyles.miniBoldLabel, GUILayout.Width(40));
            EditorGUILayout.PropertyField(trig, GUIContent.none);
            EditorGUILayout.EndHorizontal();

            switch (type)
            {
                case TriggerType.StartDelay: EditorGUILayout.PropertyField(P("seconds"), new GUIContent("Delay (s)")); break;
                case TriggerType.OnInterval: EditorGUILayout.PropertyField(P("seconds"), new GUIContent("Every (s)")); break;
                case TriggerType.OnBoolChange:
                    EditorGUILayout.PropertyField(P("boolKey"), new GUIContent("Bool key"));
                    EditorGUILayout.PropertyField(P("boolChange"), new GUIContent("Change"));
                    if (string.IsNullOrEmpty(P("boolKey").stringValue)) KwinkitUI.Warn("Enter the bool key to watch");
                    break;
            }
            if (type != TriggerType.OnStart) EditorGUILayout.PropertyField(P("runOnce"), new GUIContent("Run once"));
            if (type == TriggerType.OnInterval || type == TriggerType.OnBoolChange)
                EditorGUILayout.PropertyField(P("allowOverlap"), new GUIContent("Allow overlap"));
            if (P("seconds").floatValue < 0f) P("seconds").floatValue = 0f;
            KwinkitUI.EndCard();
        }

        void DrawCommands(KwinkitVS t)
        {
            GUILayout.Label("THEN RUN", KwinkitUI.Section);
            int del = -1, up = -1, down = -1;
            for (int i = 0; i < cmds.arraySize; i++)
            {
                var e = cmds.GetArrayElementAtIndex(i);
                var typeP = e.FindPropertyRelative("type");
                var enabledP = e.FindPropertyRelative("enabled");
                var ct = (CommandType)typeP.enumValueIndex;

                KwinkitUI.BeginCard(CmdColor(ct));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(enabledP, GUIContent.none, GUILayout.Width(16));
                KwinkitUI.IconBox(CmdIcon(ct), CmdBuiltin(ct), CmdColor(ct));
                EditorGUILayout.PropertyField(typeP, GUIContent.none);
                if (KwinkitUI.IconButton("arrow-up", "▲", "Move up") && i > 0) up = i;
                if (KwinkitUI.IconButton("arrow-down", "▼", "Move down") && i < cmds.arraySize - 1) down = i;
                if (KwinkitUI.IconButton("delete", "✕", "Delete command")) del = i;
                EditorGUILayout.EndHorizontal();

                using (new EditorGUI.DisabledScope(!enabledP.boolValue)) CommandFields(e, ct);
                KwinkitUI.EndCard();
            }

            if (del >= 0) cmds.DeleteArrayElementAtIndex(del);
            else if (up >= 0) cmds.MoveArrayElement(up, up - 1);
            else if (down >= 0) cmds.MoveArrayElement(down, down + 1);

            if (cmds.arraySize == 0) GUILayout.Label("No commands yet. Add one below.", EditorStyles.centeredGreyMiniLabel);

            GUILayout.Space(4);
            var addContent = new GUIContent("  Add Command", KwinkitUI.Icon("plus", "d_Toolbar Plus"));
            if (GUILayout.Button(addContent, KwinkitUI.BigButton, GUILayout.Height(28)))
            {
                var m = new GenericMenu();
                foreach (CommandType c in Enum.GetValues(typeof(CommandType)))
                {
                    var ct = c;
                    m.AddItem(new GUIContent(CmdLabel(ct)), false, () => AddCommand(t, ct));
                }
                m.ShowAsContext();
            }
        }

        static void AddCommand(KwinkitVS t, CommandType ct)
        {
            Undo.RecordObject(t, "Add Kwinkit-VS Command");
            t.commands.Add(new VSCommand { type = ct, text = ct == CommandType.PrintLog ? "Hello" : "" });
            EditorUtility.SetDirty(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        void CommandFields(SerializedProperty e, CommandType ct)
        {
            var text = e.FindPropertyRelative("text");
            var boolValue = e.FindPropertyRelative("boolValue");
            switch (ct)
            {
                case CommandType.PrintLog:
                    EditorGUILayout.PropertyField(text, new GUIContent("Message")); break;
                case CommandType.Wait:
                    var s = e.FindPropertyRelative("seconds");
                    EditorGUILayout.PropertyField(s, new GUIContent("Seconds"));
                    if (s.floatValue < 0f) s.floatValue = 0f;
                    break;
                case CommandType.SetBool:
                case CommandType.RequireBool:
                    EditorGUILayout.PropertyField(text, new GUIContent("Bool key"));
                    EditorGUILayout.PropertyField(boolValue, new GUIContent(ct == CommandType.SetBool ? "Set to" : "Must be"));
                    if (string.IsNullOrEmpty(text.stringValue)) KwinkitUI.Warn("Enter a bool key");
                    break;
                case CommandType.ToggleGameObject:
                    var target = e.FindPropertyRelative("target");
                    EditorGUILayout.PropertyField(target, new GUIContent("Target"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("mode"), new GUIContent("Mode"));
                    if (target.objectReferenceValue == null) KwinkitUI.Warn("No target assigned");
                    break;
            }
        }

        void DrawFooter(KwinkitVS t)
        {
            GUILayout.Space(6);
            if (!Application.isPlaying)
            {
                GUILayout.Label("The runtime manager is created automatically when you press Play.", EditorStyles.centeredGreyMiniLabel);
                return;
            }
            EditorGUILayout.BeginHorizontal();
            var old = GUI.contentColor;
            GUI.contentColor = t.IsRunning ? new Color(.4f, 1f, .5f) : new Color(.6f, .6f, .65f);
            GUILayout.Label(t.IsRunning ? "● Running" : "○ Idle", EditorStyles.boldLabel);
            GUI.contentColor = old;
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Run now", GUILayout.Width(90))) t.Fire();
            EditorGUILayout.EndHorizontal();
        }
    }

    static class KwinkitMenu
    {
        [MenuItem("GameObject/Kwinkit-VS/New Logic Object", false, 10)]
        static void Create(MenuCommand mc)
        {
            var go = new GameObject("Kwinkit-VS Logic");
            GameObjectUtility.SetParentAndAlign(go, mc.context as GameObject);
            Undo.RegisterCreatedObjectUndo(go, "Create Kwinkit-VS Logic Object");
            go.AddComponent<KwinkitVS>();
            Selection.activeGameObject = go;
        }

        [MenuItem("Tools/Kwinkit-VS/Reveal Saved Bools File")]
        static void Reveal() { EditorUtility.RevealInFinder(BoolStore.FilePath); }

        [MenuItem("Tools/Kwinkit-VS/Clear Saved Bools")]
        static void Clear() { if (EditorUtility.DisplayDialog("Kwinkit-VS", "Delete all saved booleans?", "Delete", "Cancel")) BoolStore.ClearAll(); }
    }
#endif
}
