using UnityEditor;

[InitializeOnLoad]
public static class TimelineActivationEditorLifecycle
{
    static TimelineActivationEditorLifecycle()
    {
        AssemblyReloadEvents.beforeAssemblyReload += TimelineActivationRegistry.RestoreAll;
        EditorApplication.quitting += TimelineActivationRegistry.RestoreAll;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                TimelineActivationRegistry.RestoreAll();
        };
    }
}

public sealed class TimelineActivationSaveProcessor : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        TimelineActivationRegistry.SuspendForSave();
        EditorApplication.delayCall -= TimelineActivationRegistry.ResumeAfterSave;
        EditorApplication.delayCall += TimelineActivationRegistry.ResumeAfterSave;
        return paths;
    }
}
