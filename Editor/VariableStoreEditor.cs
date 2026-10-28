// Kwinkit-VS / Editor / VariableStoreEditor.cs
// Inspector for Variable Store assets, plus the shared "pick a store, then a variable" UI used by commands and triggers.
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Kwinkit.VS
{
    /// <summary>Shared drawing for store / variable / operation / comparison / value fields.</summary>
    static class KwinkitVarUI
    {
        static string Sym(CompareOp o)
        {
            switch (o)
            {
                case CompareOp.Equal: return "==";
                case CompareOp.NotEqual: return "!=";
                case CompareOp.Greater: return ">";
                case CompareOp.Less: return "<";
                case CompareOp.GreaterOrEqual: return ">=";
                default: return "<=";
            }
        }

        // "coins-in-bag = 10  (int)". In Play mode the live value is shown instead of the default.
        static string Label(VariableStore store, VariableDef d)
        {
            var live = Application.isPlaying ? VariableRuntime.Peek(store, d.name) : null;
            string text = d.name + " = " + VariableRuntime.Display(live ?? d) + "  (" + d.type.ToString().ToLowerInvariant() + ")";
            return text.Replace('/', '∕'); // '/' would create submenus in a popup
        }

        /// <summary>Draws the Store field (with a New button) and a dropdown of its variables. Returns the selected variable, or null.</summary>
        public static VariableDef StoreAndVariable(SerializedProperty storeP, SerializedProperty varP)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(storeP, new GUIContent("Store"));
            if (storeP.objectReferenceValue == null && GUILayout.Button("New", GUILayout.Width(44))) CreateStore(storeP);
            EditorGUILayout.EndHorizontal();

            var store = storeP.objectReferenceValue as VariableStore;
            if (store == null) { KwinkitUI.Warn("Drop a Variable Store here"); return null; }

            var names = new List<string>(); var labels = new List<string>();
            foreach (var d in store.variables)
                if (!string.IsNullOrEmpty(d.name) && !names.Contains(d.name)) { names.Add(d.name); labels.Add(Label(store, d)); }
            if (names.Count == 0) { KwinkitUI.Warn("This store has no variables yet"); return null; }

            int idx = names.IndexOf(varP.stringValue);
            if (idx < 0) // nothing chosen yet, or the variable was renamed or deleted
            {
                names.Insert(0, varP.stringValue);
                labels.Insert(0, string.IsNullOrEmpty(varP.stringValue) ? "- select a variable -" : "! missing: " + varP.stringValue);
                idx = 0;
            }
            int sel = EditorGUILayout.Popup("Variable", idx, labels.ToArray());
            varP.stringValue = names[sel];
            var chosen = store.variables.Find(d => d.name == varP.stringValue);
            if (chosen == null) KwinkitUI.Warn("Choose a variable from the store");
            return chosen;
        }

        static void CreateStore(SerializedProperty storeP)
        {
            string path = EditorUtility.SaveFilePanelInProject("New Variable Store", "NewVariableStore", "asset", "Choose where to save the Variable Store");
            if (string.IsNullOrEmpty(path)) return;
            var s = ScriptableObject.CreateInstance<VariableStore>();
            AssetDatabase.CreateAsset(s, path);
            AssetDatabase.SaveAssets();
            storeP.objectReferenceValue = s;
            storeP.serializedObject.ApplyModifiedProperties();
            GUIUtility.ExitGUI(); // required after a modal panel opened from OnGUI
        }

        public static void OpPopup(SerializedProperty opP, VariableType t)
        {
            var allowed = t == VariableType.Bool ? new[] { SetOperation.Set, SetOperation.Toggle } : new[] { SetOperation.Set, SetOperation.Add };
            int i = Array.IndexOf(allowed, (SetOperation)opP.enumValueIndex);
            if (i < 0) i = 0;
            var names = new string[allowed.Length];
            for (int k = 0; k < allowed.Length; k++) names[k] = allowed[k] == SetOperation.Add && t == VariableType.String ? "Append" : allowed[k].ToString();
            i = EditorGUILayout.Popup("Operation", i, names);
            opP.enumValueIndex = (int)allowed[i];
        }

        public static void ComparePopup(SerializedProperty cmpP, VariableType t, string label)
        {
            bool numeric = t == VariableType.Int || t == VariableType.Float;
            var allowed = numeric ? (CompareOp[])Enum.GetValues(typeof(CompareOp)) : new[] { CompareOp.Equal, CompareOp.NotEqual };
            int i = Array.IndexOf(allowed, (CompareOp)cmpP.enumValueIndex);
            if (i < 0) i = 0;
            var names = new string[allowed.Length];
            for (int k = 0; k < allowed.Length; k++) names[k] = Sym(allowed[k]);
            i = EditorGUILayout.Popup(label, i, names);
            cmpP.enumValueIndex = (int)allowed[i];
        }

        /// <summary>Value field matching the variable's type, stored in the operand's matching slot.</summary>
        public static void Operand(SerializedProperty valueP, VariableType t, string label)
        {
            string slot = t == VariableType.Bool ? "boolValue" : t == VariableType.Int ? "intValue" : t == VariableType.Float ? "floatValue" : "stringValue";
            EditorGUILayout.PropertyField(valueP.FindPropertyRelative(slot), new GUIContent(label));
        }
    }

    [CustomEditor(typeof(VariableStore))]
    public class VariableStoreEditor : Editor
    {
        const float TypeW = 60f, ValueW = 78f;
        SerializedProperty list;
        string search = "";

        void OnEnable() { list = serializedObject.FindProperty("variables"); KwinkitUI.ClearCache(); }
        public override bool RequiresConstantRepaint() => Application.isPlaying;

        public override void OnInspectorGUI()
        {
            var store = (VariableStore)target;
            serializedObject.Update();
            var oldIcon = EditorGUIUtility.GetIconSize(); EditorGUIUtility.SetIconSize(new Vector2(14, 14));

            KwinkitUI.Banner("VARIABLE STORE", list.arraySize + " variables  ·  saves to " + Path.GetFileName(VariableRuntime.SavePath(store)));
            DrawToolbar(store);
            DrawTable(store);
            DrawFooter(store);

            EditorGUIUtility.SetIconSize(oldIcon);
            serializedObject.ApplyModifiedProperties();
        }

        void DrawToolbar(VariableStore store)
        {
            GUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Search", GUILayout.Width(44));
            search = EditorGUILayout.TextField(search);
            if (GUILayout.Button(new GUIContent("  Add", KwinkitUI.Icon("plus", "d_Toolbar Plus")), GUILayout.Width(66)))
            {
                var m = new GenericMenu();
                foreach (VariableType t in Enum.GetValues(typeof(VariableType)))
                {
                    var tt = t;
                    m.AddItem(new GUIContent(tt.ToString()), false, () => AddVariable(store, tt));
                }
                m.ShowAsContext();
            }
            EditorGUILayout.EndHorizontal();
        }

        static void AddVariable(VariableStore s, VariableType t)
        {
            Undo.RecordObject(s, "Add Variable");
            string baseName = t.ToString().ToLowerInvariant(), n = baseName;
            int k = 1;
            while (s.variables.Exists(v => v.name == n)) n = baseName + "-" + (++k);
            s.variables.Add(new VariableDef { name = n, type = t });
            EditorUtility.SetDirty(s);
        }

        void DrawTable(VariableStore store)
        {
            bool live = Application.isPlaying;
            EditorGUILayout.BeginHorizontal();
            GUILayout.Label("Type", EditorStyles.miniBoldLabel, GUILayout.Width(TypeW));
            GUILayout.Label("Name", EditorStyles.miniBoldLabel, GUILayout.ExpandWidth(true));
            GUILayout.Label("Default", EditorStyles.miniBoldLabel, GUILayout.Width(ValueW));
            if (live) GUILayout.Label("Live", EditorStyles.miniBoldLabel, GUILayout.Width(ValueW));
            GUILayout.Space(30);
            EditorGUILayout.EndHorizontal();

            var counts = new Dictionary<string, int>();
            for (int i = 0; i < list.arraySize; i++)
            {
                string n = list.GetArrayElementAtIndex(i).FindPropertyRelative("name").stringValue;
                counts[n] = (counts.TryGetValue(n, out var c) ? c : 0) + 1;
            }

            int del = -1, shown = 0;
            for (int i = 0; i < list.arraySize; i++)
            {
                var e = list.GetArrayElementAtIndex(i);
                var nameP = e.FindPropertyRelative("name");
                var typeP = e.FindPropertyRelative("type");
                if (search.Length > 0 && nameP.stringValue.IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                shown++;

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(typeP, GUIContent.none, GUILayout.Width(TypeW));
                var oldBg = GUI.backgroundColor;
                if (string.IsNullOrEmpty(nameP.stringValue) || counts[nameP.stringValue] > 1) GUI.backgroundColor = new Color(1f, .45f, .4f); // empty or duplicate name
                EditorGUILayout.PropertyField(nameP, GUIContent.none);
                GUI.backgroundColor = oldBg;

                var type = (VariableType)typeP.enumValueIndex;
                string slot = type == VariableType.Bool ? "boolValue" : type == VariableType.Int ? "intValue" : type == VariableType.Float ? "floatValue" : "stringValue";
                EditorGUILayout.PropertyField(e.FindPropertyRelative(slot), GUIContent.none, GUILayout.Width(ValueW));
                if (live) LiveField(store, nameP.stringValue);
                if (KwinkitUI.IconButton("delete", "✕", "Delete variable")) del = i;
                EditorGUILayout.EndHorizontal();
            }
            if (del >= 0) list.DeleteArrayElementAtIndex(del);

            if (list.arraySize == 0) GUILayout.Label("No variables yet. Use Add to create one.", EditorStyles.centeredGreyMiniLabel);
            else if (shown == 0) GUILayout.Label("No variable matches the search.", EditorStyles.centeredGreyMiniLabel);
        }

        // Editable live value (Play mode only). Edits are applied to the running game and saved like any other change.
        static void LiveField(VariableStore store, string name)
        {
            var v = VariableRuntime.Peek(store, name);
            if (v == null) { GUILayout.Label("-", GUILayout.Width(ValueW)); return; }
            var oldBg = GUI.backgroundColor; GUI.backgroundColor = new Color(.5f, 1f, .6f);
            EditorGUI.BeginChangeCheck();
            switch (v.type)
            {
                case VariableType.Bool: v.boolValue = EditorGUILayout.Toggle(v.boolValue, GUILayout.Width(ValueW)); break;
                case VariableType.Int: v.intValue = EditorGUILayout.IntField(v.intValue, GUILayout.Width(ValueW)); break;
                case VariableType.Float: v.floatValue = EditorGUILayout.FloatField(v.floatValue, GUILayout.Width(ValueW)); break;
                default: v.stringValue = EditorGUILayout.TextField(v.stringValue, GUILayout.Width(ValueW)); break;
            }
            if (EditorGUI.EndChangeCheck()) VariableRuntime.MarkChanged(store, name);
            GUI.backgroundColor = oldBg;
        }

        void DrawFooter(VariableStore store)
        {
            GUILayout.Space(8);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("saveFileName"), new GUIContent("Save file name"));
            EditorGUILayout.LabelField("Saved to", VariableRuntime.SavePath(store), EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Reveal Save Folder"))
            {
                Directory.CreateDirectory(VariableRuntime.SaveFolder);
                EditorUtility.RevealInFinder(VariableRuntime.SaveFolder);
            }
            if (GUILayout.Button("Delete Saved Values") && EditorUtility.DisplayDialog("Kwinkit-VS", "Delete the saved values of '" + store.name + "' and return to the defaults?", "Delete", "Cancel"))
                VariableRuntime.ResetStore(store);
            EditorGUILayout.EndHorizontal();
            GUILayout.Label("Defaults live in this asset. Live values are kept in the save file, so Play mode never changes this asset.", EditorStyles.centeredGreyMiniLabel);
        }
    }
}
