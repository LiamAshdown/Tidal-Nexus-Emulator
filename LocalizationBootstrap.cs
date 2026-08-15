using UnityEngine;
using UnityEngine.Localization.Settings;

namespace TidalNexus.StandaloneServer
{

    public static class LocalizationBootstrap
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void EnsureSettings()
        {

            if (LocalizationSettings.HasSettings)
            {
                return;
            }

            LocalizationSettings settings =
                ScriptableObject.CreateInstance<LocalizationSettings>();
            settings.name = "Runtime Localization Settings";

            settings.hideFlags = HideFlags.HideAndDontSave;

            LocalizationSettings.Instance = settings;

            Debug.Log(
                "[Localization] no active settings found - created one at runtime " +
                $"(locales provider: {settings.GetAvailableLocales()?.GetType().Name ?? "NULL"}, " +
                $"string database: {settings.GetStringDatabase()?.GetType().Name ?? "NULL"})");
        }
    }
}
