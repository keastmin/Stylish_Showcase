using UnityEditor;

[InitializeOnLoad]
public static class SkillCameraPreviewLifecycle
{
    static SkillCameraPreviewLifecycle()
    {
        AssemblyReloadEvents.beforeAssemblyReload += PlayerSkillCameraDirector.RestoreAllPreviews;
        EditorApplication.quitting += PlayerSkillCameraDirector.RestoreAllPreviews;
        EditorApplication.update += PlayerSkillCameraDirector.RefreshPreviewOutputs;
        EditorApplication.playModeStateChanged += state =>
        {
            if (state == PlayModeStateChange.ExitingEditMode || state == PlayModeStateChange.ExitingPlayMode)
                PlayerSkillCameraDirector.RestoreAllPreviews();
        };
    }
}

public sealed class SkillCameraPreviewSaveProcessor : AssetModificationProcessor
{
    private static string[] OnWillSaveAssets(string[] paths)
    {
        PlayerSkillCameraDirector.SuspendPreviewsForSave();
        EditorApplication.delayCall -= PlayerSkillCameraDirector.ResumePreviewsAfterSave;
        EditorApplication.delayCall += PlayerSkillCameraDirector.ResumePreviewsAfterSave;
        return paths;
    }
}
