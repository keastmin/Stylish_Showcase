using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectR.UI
{
    /// <summary>Installs the authored prefab in gameplay scenes without changing their scene assets.</summary>
    public static class PauseSettingsBootstrap
    {
        public const string ResourcePath = "PauseSettings";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Initialize()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            PauseSettingsController.ApplySavedAudio();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AfterSceneLoad() => EnsureForGameplay();

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            // A replacement gameplay scene starts with restored world/cursor state.
            if (mode == LoadSceneMode.Single && PauseSettingsController.Instance != null)
                PauseSettingsController.Instance.CloseImmediately();
            EnsureForGameplay();
        }

        public static void EnsureForGameplay()
        {
            bool hasPlayer = Object.FindAnyObjectByType<PlayerCore>() != null;
            if (PauseSettingsController.Instance != null)
            {
                PauseSettingsController.Instance.SetSceneAvailability(hasPlayer);
                return;
            }
            if (!hasPlayer) return;
            var prefab = Resources.Load<GameObject>(ResourcePath);
            if (prefab == null)
            {
                Debug.LogWarning("[PauseSettings] Missing Resources/PauseSettings prefab. Rebuild it with Tools/Project R/Pause Settings/Rebuild Prefab.");
                return;
            }
            Object.Instantiate(prefab).name = "PauseSettings";
        }
    }
}
