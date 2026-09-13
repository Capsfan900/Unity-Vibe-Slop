using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace VibeGame1.EditorTools
{
    public static class LevelStudioInspector
    {
        public static bool Draw(LevelDefinition definition, IReadOnlyList<LevelObjectRecord> records)
        {
            if (definition == null || records == null || records.Count == 0) { EditorGUILayout.HelpBox("Select a level object.", MessageType.Info); return false; }
            if (records.Count > 1) return DrawMulti(definition, records);
            var serialized = new SerializedObject(definition);
            var property = serialized.FindProperty(SerializedPath(records[0].path));
            if (property == null) { EditorGUILayout.HelpBox("This record has no serialized inspector path.", MessageType.Warning); return false; }
            serialized.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.PropertyField(property, true);
            if (!EditorGUI.EndChangeCheck()) return false;
            serialized.ApplyModifiedProperties();
            return true;
        }

        static bool DrawMulti(LevelDefinition definition, IReadOnlyList<LevelObjectRecord> records)
        {
            EditorGUILayout.LabelField(records.Count + " objects selected", EditorStyles.boldLabel);
            string current = records.Select(x => x.meta == null ? "" : x.meta.zoneIdOverride).Distinct().Count() == 1 && records[0].meta != null ? records[0].meta.zoneIdOverride : "";
            string zone = EditorGUILayout.TextField("Zone Override", current);
            Vector3 offset = EditorGUILayout.Vector3Field("Position Offset", Vector3.zero);
            bool changed = false;
            if (GUILayout.Button("Apply Zone Override"))
            {
                Undo.RecordObject(definition, "Edit Level Object Zones");
                foreach (var record in records) if (record.meta != null) record.meta.zoneIdOverride = zone;
                changed = true;
            }
            if (offset != Vector3.zero && GUILayout.Button("Apply Position Offset"))
            {
                var tool = new LevelStudioSceneTool(definition);
                foreach (var record in records)
                {
                    Vector3 position; Quaternion rotation; Vector3 scale;
                    if (LevelStudioSceneTool.TryGetTransform(record, out position, out rotation, out scale))
                        tool.ApplyTransform(record, Matrix4x4.TRS(position + offset, rotation, scale));
                }
                changed = true;
            }
            if (changed) EditorUtility.SetDirty(definition);
            return changed;
        }

        static string SerializedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            var result = new System.Text.StringBuilder();
            for (int i = 0; i < path.Length; i++)
            {
                if (path[i] != '[') { result.Append(path[i]); continue; }
                int close = path.IndexOf(']', i);
                result.Append(".Array.data[").Append(path, i + 1, close - i - 1).Append(']');
                i = close;
            }
            return result.ToString();
        }
    }
}
