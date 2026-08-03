#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace Wayfu.Lamkn
{
    [CustomEditor(typeof(Map))]
    public class MapMovePathEditor : Editor
    {
        public override void OnInspectorGUI() => DrawDefaultInspector();

        private void OnSceneGUI()
        {
            var map = (Map)target;
            var serialized = new SerializedObject(map);
            var paths = serialized.FindProperty("slotMovePaths");
            if (paths == null) return;

            for (int slot = 0; slot < paths.arraySize; slot++)
            {
                var route = paths.GetArrayElementAtIndex(slot);
                var positions = route.FindPropertyRelative("positions");
                if (positions == null) continue;

                Handles.color = Color.Lerp(Color.cyan, Color.yellow,
                    slot / Mathf.Max(1f, paths.arraySize - 1f));
                for (int i = 0; i < positions.arraySize; i++)
                {
                    var marker = positions.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (marker == null) continue;

                    float size = HandleUtility.GetHandleSize(marker.position) * 0.1f;
                    Handles.Label(marker.position + Vector3.up * size * 1.5f, $"Slot {slot} / {i}");
                    EditorGUI.BeginChangeCheck();
                    Vector3 next = Handles.PositionHandle(marker.position, marker.rotation);
                    if (!EditorGUI.EndChangeCheck()) continue;

                    Undo.RecordObject(marker, "Move gun path point");
                    marker.position = next;
                    EditorUtility.SetDirty(marker);
                    serialized.Update();
                }
            }
        }
    }
}
#endif
