// Kwinkit-VS / Core / VariableStore.cs
// Variable Store asset (a design-time list of typed variables) plus the runtime that holds live values and saves them as JSON.
// The asset holds names, types and DEFAULTS only. Live values are kept in memory and in a save file, never written back into the asset.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace Kwinkit.VS
{
    public enum VariableType { Bool, Int, Float, String }
    public enum SetOperation { Set, Add, Toggle }
    public enum CompareOp { Equal, NotEqual, Greater, Less, GreaterOrEqual, LessOrEqual }

    /// <summary>One variable: name, type and a value slot per type (only the slot matching the type is used).</summary>
    [Serializable]
    public class VariableDef
    {
        public string name = "";
        public VariableType type;
        public bool boolValue;
        public int intValue;
        public float floatValue;
        public string stringValue = "";
    }

    [CreateAssetMenu(fileName = "NewVariableStore", menuName = "Kwinkit-VS/Variable Store")]
    public class VariableStore : ScriptableObject
    {
        public List<VariableDef> variables = new List<VariableDef>();
        [Tooltip("Name of the JSON save file. Leave empty to use the asset name.")]
        public string saveFileName = "";
    }

    /// <summary>Live values, change events and persistence for Variable Stores.</summary>
    public static class VariableRuntime
    {
        class State { public readonly Dictionary<string, VariableDef> values = new Dictionary<string, VariableDef>(); public bool dirty; }
        [Serializable] public class Entry { public string name; public string type; public string value; }
        [Serializable] public class SaveFile { public List<Entry> variables = new List<Entry>(); }

        static readonly Dictionary<VariableStore, State> states = new Dictionary<VariableStore, State>();
        static readonly HashSet<string> warned = new HashSet<string>();
        public static event Action<VariableStore, string> Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { states.Clear(); warned.Clear(); Changed = null; }

        public static string SaveFolder => Path.Combine(Application.persistentDataPath, "KwinkitVS");

        public static string SavePath(VariableStore s)
        {
            string n = string.IsNullOrWhiteSpace(s.saveFileName) ? s.name : s.saveFileName.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
            return Path.Combine(SaveFolder, n + ".json");
        }

        // ---------------------------------------------------------------- access
        static State GetState(VariableStore s)
        {
            if (states.TryGetValue(s, out var st)) return st;
            st = new State();
            foreach (var d in s.variables)
                if (!string.IsNullOrEmpty(d.name) && !st.values.ContainsKey(d.name))
                    st.values[d.name] = new VariableDef { name = d.name, type = d.type, boolValue = d.boolValue, intValue = d.intValue, floatValue = d.floatValue, stringValue = d.stringValue };
            Load(s, st);
            states[s] = st;
            return st;
        }

        /// <summary>Live variable if the store is active (Play mode), otherwise null. Never creates state.</summary>
        public static VariableDef Peek(VariableStore s, string name)
        {
            if (s == null || !states.TryGetValue(s, out var st)) return null;
            return st.values.TryGetValue(name ?? "", out var v) ? v : null;
        }

        static void WarnOnce(string key, string msg, UnityEngine.Object ctx) { if (warned.Add(key)) Debug.LogWarning("[Kwinkit-VS] " + msg, ctx); }

        public static VariableDef Find(VariableStore s, string name)
        {
            if (s == null) { WarnOnce("nostore", "A variable command has no Variable Store assigned.", null); return null; }
            if (GetState(s).values.TryGetValue(name ?? "", out var v)) return v;
            WarnOnce(s.GetInstanceID() + ":" + name, "Variable '" + name + "' was not found in store '" + s.name + "'.", s);
            return null;
        }

        public static string Display(VariableDef v)
        {
            switch (v.type)
            {
                case VariableType.Bool: return v.boolValue ? "true" : "false";
                case VariableType.Int: return v.intValue.ToString(CultureInfo.InvariantCulture);
                case VariableType.Float: return v.floatValue.ToString("R", CultureInfo.InvariantCulture);
                default: return v.stringValue ?? "";
            }
        }

        // ---------------------------------------------------------------- operations
        public static void Apply(VariableStore s, string name, SetOperation op, VariableDef operand)
        {
            var v = Find(s, name);
            if (v == null) return;
            string before = Display(v);
            switch (v.type)
            {
                case VariableType.Bool: v.boolValue = op == SetOperation.Toggle ? !v.boolValue : operand.boolValue; break;
                case VariableType.Int: v.intValue = op == SetOperation.Add ? v.intValue + operand.intValue : operand.intValue; break;
                case VariableType.Float: v.floatValue = op == SetOperation.Add ? v.floatValue + operand.floatValue : operand.floatValue; break;
                default: v.stringValue = op == SetOperation.Add ? (v.stringValue ?? "") + (operand.stringValue ?? "") : operand.stringValue ?? ""; break;
            }
            if (Display(v) != before) MarkChanged(s, name);
        }

        public static bool Check(VariableStore s, string name, CompareOp op, VariableDef operand)
        {
            var v = Find(s, name);
            if (v == null) return false;
            int c;
            switch (v.type)
            {
                case VariableType.Bool: c = v.boolValue.CompareTo(operand.boolValue); break;
                case VariableType.Int: c = v.intValue.CompareTo(operand.intValue); break;
                case VariableType.Float: c = Mathf.Approximately(v.floatValue, operand.floatValue) ? 0 : v.floatValue.CompareTo(operand.floatValue); break;
                default: c = string.CompareOrdinal(v.stringValue ?? "", operand.stringValue ?? ""); break;
            }
            switch (op)
            {
                case CompareOp.Equal: return c == 0;
                case CompareOp.NotEqual: return c != 0;
                case CompareOp.Greater: return c > 0;
                case CompareOp.Less: return c < 0;
                case CompareOp.GreaterOrEqual: return c >= 0;
                default: return c <= 0;
            }
        }

        /// <summary>Flags the store for saving and raises Changed. Call after editing a live value directly.</summary>
        public static void MarkChanged(VariableStore s, string name)
        {
            if (s != null && states.TryGetValue(s, out var st)) st.dirty = true;
            Changed?.Invoke(s, name);
        }

        // ---------------------------------------------------------------- persistence
        /// <summary>Writes every modified store to disk. Called by the manager once per frame, on pause and on quit.</summary>
        public static void Flush()
        {
            foreach (var kv in states)
            {
                if (!kv.Value.dirty || kv.Key == null) continue;
                kv.Value.dirty = false;
                Save(kv.Key, kv.Value);
            }
        }

        static void Save(VariableStore s, State st)
        {
            var f = new SaveFile();
            var seen = new HashSet<string>();
            foreach (var d in s.variables)
                if (!string.IsNullOrEmpty(d.name) && seen.Add(d.name) && st.values.TryGetValue(d.name, out var v))
                    f.variables.Add(new Entry { name = v.name, type = v.type.ToString(), value = Display(v) });
            try { Directory.CreateDirectory(SaveFolder); File.WriteAllText(SavePath(s), JsonUtility.ToJson(f, true)); }
            catch (Exception ex) { Debug.LogWarning("[Kwinkit-VS] Could not save '" + s.name + "': " + ex.Message, s); }
        }

        static void Load(VariableStore s, State st)
        {
            try
            {
                string path = SavePath(s);
                if (!File.Exists(path)) return;
                var f = JsonUtility.FromJson<SaveFile>(File.ReadAllText(path));
                if (f == null) return;
                foreach (var e in f.variables)
                    if (st.values.TryGetValue(e.name ?? "", out var v) && e.type == v.type.ToString()) Parse(v, e.value);
            }
            catch (Exception ex) { Debug.LogWarning("[Kwinkit-VS] Could not load '" + s.name + "': " + ex.Message, s); }
        }

        static void Parse(VariableDef v, string raw)
        {
            switch (v.type)
            {
                case VariableType.Bool: if (bool.TryParse(raw, out var b)) v.boolValue = b; break;
                case VariableType.Int: if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)) v.intValue = i; break;
                case VariableType.Float: if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)) v.floatValue = f; break;
                default: v.stringValue = raw ?? ""; break;
            }
        }

        /// <summary>Deletes the save file and drops live values, so the next access starts from the asset defaults.</summary>
        public static void ResetStore(VariableStore s)
        {
            if (s == null) return;
            states.Remove(s);
            try { string p = SavePath(s); if (File.Exists(p)) File.Delete(p); }
            catch (Exception ex) { Debug.LogWarning("[Kwinkit-VS] Could not delete save file: " + ex.Message, s); }
        }
    }
}
