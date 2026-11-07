using UnityEditor;
using UnityEngine;

/// <summary>Shared visual language for the player skill authoring windows.</summary>
internal static class SkillStudioGUI
{
    public static readonly Color Accent = new(.3f, .84f, .9f);
    public static readonly Color HitColor = new(1f, .65f, .23f);
    public static readonly Color PathColor = new(.69f, .48f, 1f);
    public static readonly Color TimelineBackground = new(.105f, .11f, .13f);
    public static readonly Color TimelineGrid = new(.22f, .24f, .27f);
    public static readonly Color StripBackground = new(.12f, .14f, .18f);
    public const float TimelineLabelWidth = 168f;
    public const float TimelineRowHeight = 24f;

    public static void Header(string title, string subtitle)
    {
        Rect rect = GUILayoutUtility.GetRect(0, 51, GUILayout.ExpandWidth(true));
        EditorGUI.DrawRect(rect, new Color(.08f, .13f, .17f));
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 4, rect.height), Accent);
        GUI.Label(new Rect(rect.x + 14, rect.y + 6, rect.width - 24, 22), title,
            new GUIStyle(EditorStyles.boldLabel) { fontSize = 17, normal = { textColor = Color.white } });
        GUI.Label(new Rect(rect.x + 14, rect.y + 29, rect.width - 24, 18), subtitle, EditorStyles.miniLabel);
    }

    public static void Section(string title)
    {
        EditorGUILayout.Space(7);
        EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
    }
}
