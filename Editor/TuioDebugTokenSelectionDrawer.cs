using UnityEditor;
using UnityEngine;

namespace BeyondFutureOne.TuioClient.Editor
{
    [CustomPropertyDrawer(typeof(TuioDebugTokenSelection))]
    public sealed class TuioDebugTokenSelectionDrawer : PropertyDrawer
    {
        private const int Columns = 6;
        private const int Rows = Tuio11CanvasAdapter.SupportedTokenCount / Columns;

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return (Rows + 1) * EditorGUIUtility.singleLineHeight + Rows * EditorGUIUtility.standardVerticalSpacing;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var maskProperty = property.FindPropertyRelative("_mask");
            var lineHeight = EditorGUIUtility.singleLineHeight;
            var spacing = EditorGUIUtility.standardVerticalSpacing;
            var labelRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.LabelField(labelRect, label);

            if (maskProperty == null)
            {
                EditorGUI.EndProperty();
                return;
            }

            var mask = maskProperty.intValue;
            var gridRect = EditorGUI.IndentedRect(new Rect(position.x, position.y, position.width, lineHeight));
            var gridX = gridRect.x;
            var gridWidth = gridRect.width;
            var cellWidth = gridWidth / Columns;

            EditorGUI.BeginChangeCheck();
            for (var tokenIndex = 0; tokenIndex < Tuio11CanvasAdapter.SupportedTokenCount; tokenIndex++)
            {
                var row = tokenIndex / Columns;
                var column = tokenIndex % Columns;
                var tokenId = Tuio11CanvasAdapter.MinSupportedTokenId + tokenIndex;
                var cellRect = new Rect(
                    gridX + column * cellWidth,
                    position.y + (row + 1) * (lineHeight + spacing),
                    cellWidth,
                    lineHeight);
                var bit = 1 << tokenIndex;
                var enabled = (mask & bit) != 0;

                if (EditorGUI.ToggleLeft(cellRect, tokenId.ToString(), enabled) != enabled)
                {
                    mask ^= bit;
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                maskProperty.intValue = mask;
            }

            EditorGUI.EndProperty();
        }
    }
}
