// Kwinkit-VS / Editor / KwinkitVSEditor.cs
// Inspector UI for the Kwinkit-VS component, icon loading and menu items.
// Editor-only: Unity compiles anything in an "Editor" folder out of builds.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kwinkit.VS
{
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

        // Resolved from KwinkitVS.cs, so the Kwinkit-VS folder can live anywhere under Assets/.
        public static string Root
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

        // Looks in <Root>/Icons first, then anywhere under <Root>, matching the file name case-insensitively.
        static Texture2D LoadIcon(string name)
        {
            var t = AssetDatabase.LoadAssetAtPath<Texture2D>(Root + "/Icons/" + name + ".png");
            if (t != null || !AssetDatabase.IsValidFolder(Root)) return t;
            foreach (var g in AssetDatabase.FindAssets(name + " t:Texture2D", new[] { Root }))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (string.Equals(Path.GetFileNameWithoutExtension(p), name, StringComparison.OrdinalIgnoreCase)) return AssetDatabase.LoadAssetAtPath<Texture2D>(p);
            }
            return null;
        }

        public static Texture Icon(string name, string builtin = null)
        {
            if (!cache.ContainsKey(name)) cache[name] = LoadIcon(name);
            var t = cache[name];
            if (t != null) return t;
            return builtin != null ? EditorGUIUtility.FindTexture(builtin) : null;
        }

        public static GUIStyle Card => cardStyle ??= new GUIStyle { padding = new RectOffset(14, 8, 6, 7), margin = new RectOffset(4, 4, 3, 3) };
        public static GUIStyle BannerTitle => bannerTitle ??= new GUIStyle(EditorStyles.boldLabel) { fontSize = 13, normal = { textColor = Color.white } };
        public static GUIStyle BannerSub => bannerSub ??= new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(.6f, .65f, .75f) } };
        public static GUIStyle Section => section ??= new GUIStyle(EditorStyles.miniBoldLabel) { margin = new RectOffset(6, 0, 6, 2) };
        public static GUIStyle BigButton => bigButton ??= new GUIStyle(GUI.skin.button) { fontStyle = FontStyle.Bold, fontSize = 12 };

        public static void Banner(string title, string sub)
        {
            var r = GUILayoutUtility.GetRect(0, 38, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(.09f, .10f, .13f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 2, r.width, 2), new Color(.30f, .62f, 1f));
            float x = r.x + 10;
            var logo = Icon("logo");
            if (logo != null) { GUI.DrawTexture(new Rect(x, r.y + 7, 24, 24), logo, ScaleMode.ScaleToFit); x += 32; }
            GUI.Label(new Rect(x, r.y + 3, r.width - x, 18), title, BannerTitle);
            GUI.Label(new Rect(x, r.y + 20, r.width - x, 16), sub, BannerSub);
        }

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

        static string TriggerIcon(TriggerType t) => t switch { TriggerType.OnStart => "play", TriggerType.StartDelay => "hourglass", TriggerType.OnInterval => "rotate", TriggerType.OnKeyPress => "keyboard", _ => "bool" };
        static string TriggerBuiltin(TriggerType t) => t switch { TriggerType.OnStart => "d_PlayButton", TriggerType.OnInterval => "d_preAudioLoopOff", _ => null };
        static string CmdIcon(CommandType t) => t switch
        {
            CommandType.PrintLog => "log", CommandType.Wait => "timer", CommandType.SetVariable => "save", CommandType.ToggleGameObject => "eye",
            CommandType.SetPosition => "move", CommandType.SetRotation => "rotate", CommandType.PlaySound => "sound",
            CommandType.DestroyObject => "delete", CommandType.GetInput => "keyboard", _ => "shield"
        };
        static string CmdBuiltin(CommandType t) => t switch
        {
            CommandType.PrintLog => "d_UnityEditor.ConsoleWindow", CommandType.ToggleGameObject => "d_scenevis_visible_hover",
            CommandType.PlaySound => "d_AudioSource Icon", CommandType.DestroyObject => "d_TreeEditor.Trash", _ => null
        };
        // Add Command menu paths (submenus group related commands).
        static string CmdMenu(CommandType t) => t switch
        {
            CommandType.PrintLog => "Debug/Print Log", CommandType.Wait => "Flow/Wait", CommandType.GetInput => "Flow/Get Input (wait for key)",
            CommandType.RequireVariable => "Flow/Require Variable (stop if not)", CommandType.SetVariable => "Data/Set Variable",
            CommandType.ToggleGameObject => "Object/Toggle GameObject", CommandType.SetPosition => "Object/Set Position",
            CommandType.SetRotation => "Object/Set Rotation", CommandType.DestroyObject => "Object/Destroy Object", _ => "Audio/Play Sound"
        };
        static Color CmdColor(CommandType t) => t switch
        {
            CommandType.PrintLog => new Color(.30f, .60f, 1f), CommandType.Wait => new Color(.65f, .65f, .72f),
            CommandType.SetVariable => new Color(.70f, .50f, 1f), CommandType.ToggleGameObject => new Color(1f, .62f, .25f),
            CommandType.SetPosition => new Color(.25f, .80f, .75f), CommandType.SetRotation => new Color(.70f, .90f, .30f),
            CommandType.PlaySound => new Color(1f, .45f, .65f), CommandType.DestroyObject => new Color(1f, .38f, .35f),
            CommandType.GetInput => new Color(.85f, .75f, .55f), _ => new Color(1f, .82f, .25f)
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

            KwinkitUI.Banner("KWINKIT-VS", "WHEN trigger  →  THEN run commands in order");
            DrawTrigger();
            GUILayout.Label("▼", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 12 });
            DrawCommands(t);
            DrawFooter(t);

            EditorGUIUtility.labelWidth = oldLabel; EditorGUIUtility.SetIconSize(oldIcon);
            serializedObject.ApplyModifiedProperties();
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
                case TriggerType.OnKeyPress: EditorGUILayout.PropertyField(P("key"), new GUIContent("Key")); break;
                case TriggerType.OnVariableChange:
                    {
                        var def = KwinkitVarUI.StoreAndVariable(P("store"), P("variable"));
                        if (def == null) break;
                        EditorGUILayout.PropertyField(P("useCondition"), new GUIContent("Only when"));
                        if (P("useCondition").boolValue)
                        {
                            KwinkitVarUI.ComparePopup(P("compare"), def.type, "Value is");
                            KwinkitVarUI.Operand(P("value"), def.type, "Compared to");
                        }
                        break;
                    }
            }
            if (type != TriggerType.OnStart) EditorGUILayout.PropertyField(P("runOnce"), new GUIContent("Run once"));
            if (type == TriggerType.OnInterval || type == TriggerType.OnVariableChange || type == TriggerType.OnKeyPress)
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
                    m.AddItem(new GUIContent(CmdMenu(ct)), false, () => AddCommand(t, ct));
                }
                m.ShowAsContext();
            }
        }

        static void AddCommand(KwinkitVS t, CommandType ct)
        {
            Undo.RecordObject(t, "Add Kwinkit-VS Command");
            var cmd = new VSCommand { type = ct, text = ct == CommandType.PrintLog ? "Hello" : "" };
            if (ct == CommandType.SetVariable || ct == CommandType.RequireVariable)
                foreach (var other in t.commands) if (other.store != null) cmd.store = other.store; // reuse the store of the previous variable command
            t.commands.Add(cmd);
            EditorUtility.SetDirty(t);
            PrefabUtility.RecordPrefabInstancePropertyModifications(t);
        }

        void CommandFields(SerializedProperty e, CommandType ct)
        {
            switch (ct)
            {
                case CommandType.PrintLog:
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("text"), new GUIContent("Message")); break;
                case CommandType.Wait:
                    var s = e.FindPropertyRelative("seconds");
                    EditorGUILayout.PropertyField(s, new GUIContent("Seconds"));
                    if (s.floatValue < 0f) s.floatValue = 0f;
                    break;
                case CommandType.SetVariable:
                    {
                        var def = KwinkitVarUI.StoreAndVariable(e.FindPropertyRelative("store"), e.FindPropertyRelative("variable"));
                        if (def == null) break;
                        var opP = e.FindPropertyRelative("op");
                        KwinkitVarUI.OpPopup(opP, def.type);
                        var op = (SetOperation)opP.enumValueIndex;
                        if (op != SetOperation.Toggle) KwinkitVarUI.Operand(e.FindPropertyRelative("value"), def.type, op == SetOperation.Add ? (def.type == VariableType.String ? "Text" : "Amount") : "Value");
                        break;
                    }
                case CommandType.RequireVariable:
                    {
                        var def = KwinkitVarUI.StoreAndVariable(e.FindPropertyRelative("store"), e.FindPropertyRelative("variable"));
                        if (def == null) break;
                        KwinkitVarUI.ComparePopup(e.FindPropertyRelative("compare"), def.type, "Continue if");
                        KwinkitVarUI.Operand(e.FindPropertyRelative("value"), def.type, "Value");
                        GUILayout.Label("Stops the sequence when the check fails.", EditorStyles.miniLabel);
                        break;
                    }
                case CommandType.ToggleGameObject:
                case CommandType.SetPosition:
                case CommandType.SetRotation:
                case CommandType.DestroyObject:
                    var target = e.FindPropertyRelative("target");
                    EditorGUILayout.PropertyField(target, new GUIContent("Target"));
                    if (ct == CommandType.ToggleGameObject) EditorGUILayout.PropertyField(e.FindPropertyRelative("mode"), new GUIContent("Mode"));
                    if (ct == CommandType.SetPosition || ct == CommandType.SetRotation)
                    {
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("vector"), new GUIContent(ct == CommandType.SetPosition ? "Position" : "Rotation"));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("useLocal"), new GUIContent("Local space"));
                        EditorGUILayout.PropertyField(e.FindPropertyRelative("additive"), new GUIContent("Add to current"));
                    }
                    if (target.objectReferenceValue == null) KwinkitUI.Warn("No target assigned");
                    else if (ct == CommandType.DestroyObject && EditorUtility.IsPersistent(target.objectReferenceValue)) KwinkitUI.Warn("Target must be a scene object, not an asset");
                    break;
                case CommandType.PlaySound:
                    var clip = e.FindPropertyRelative("clip");
                    EditorGUILayout.PropertyField(clip, new GUIContent("Clip"));
                    EditorGUILayout.Slider(e.FindPropertyRelative("volume"), 0f, 1f, new GUIContent("Volume"));
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("waitForEnd"), new GUIContent("Wait until done"));
                    if (clip.objectReferenceValue == null) KwinkitUI.Warn("No clip assigned");
                    break;
                case CommandType.GetInput:
                    EditorGUILayout.PropertyField(e.FindPropertyRelative("key"), new GUIContent("Key"));
                    GUILayout.Label("Pauses the sequence until the key is pressed.", EditorStyles.miniLabel);
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

    // Assigns Icons/kwinkitvs.png as the KwinkitVS script icon (Inspector header, Hierarchy and Scene view gizmo).
    // The icon is stored in KwinkitVS.cs.meta, so after the first assignment it persists and ships with the package.
    [InitializeOnLoad]
    static class KwinkitScriptIcon
    {
        const string Flag = "KwinkitVS.IconAssigned";
        static KwinkitScriptIcon() { Schedule(); }
        public static void Schedule() { EditorApplication.delayCall -= ApplyAuto; EditorApplication.delayCall += ApplyAuto; }
        static void ApplyAuto() { Apply(false); }

        /// <summary>Assigns the icon. Returns a message describing what happened. Automatic runs try once per editor session.</summary>
        public static string Apply(bool force)
        {
            KwinkitUI.ClearCache();
            var icon = KwinkitUI.Icon("kwinkitvs") as Texture2D;
            if (icon == null) return "kwinkitvs.png was not found under " + KwinkitUI.Root + " (put it in the Icons folder).";
            foreach (var g in AssetDatabase.FindAssets("KwinkitVS t:MonoScript"))
            {
                var p = AssetDatabase.GUIDToAssetPath(g);
                if (Path.GetFileNameWithoutExtension(p) != "KwinkitVS") continue;
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(p);
                if (script == null) return "KwinkitVS.cs could not be loaded.";
                if (EditorGUIUtility.GetIconForObject(script) == icon) return "The icon is already assigned to " + p + ".";
                if (!force && SessionState.GetBool(Flag, false)) return "";
                SessionState.SetBool(Flag, true);
                EditorGUIUtility.SetIconForObject(script, icon);
                var importer = AssetImporter.GetAtPath(p);
                if (importer != null) { EditorUtility.SetDirty(importer); importer.SaveAndReimport(); }
                return "Assigned kwinkitvs.png to " + p + ". If the Add Component list still shows the old icon, restart Unity.";
            }
            return "KwinkitVS.cs was not found.";
        }
    }

    // Re-applies the icon when kwinkitvs.png is added or reimported after the scripts compiled.
    class KwinkitIconPostprocessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p.EndsWith("/kwinkitvs.png", StringComparison.OrdinalIgnoreCase)) { KwinkitScriptIcon.Schedule(); return; }
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

        [MenuItem("Tools/Kwinkit-VS/Reveal Save Folder")]
        static void Reveal()
        {
            Directory.CreateDirectory(VariableRuntime.SaveFolder);
            EditorUtility.RevealInFinder(VariableRuntime.SaveFolder);
        }

        [MenuItem("Tools/Kwinkit-VS/Delete All Saved Variables")]
        static void ClearSaves()
        {
            if (!EditorUtility.DisplayDialog("Kwinkit-VS", "Delete every saved variable file for this project?", "Delete", "Cancel")) return;
            if (Directory.Exists(VariableRuntime.SaveFolder)) Directory.Delete(VariableRuntime.SaveFolder, true);
        }

        [MenuItem("Tools/Kwinkit-VS/Apply Component Icon")]
        static void ApplyIcon() { Debug.Log("[Kwinkit-VS] " + KwinkitScriptIcon.Apply(true)); }
    }
}