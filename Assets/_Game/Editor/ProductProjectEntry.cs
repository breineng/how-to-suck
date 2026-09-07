using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace HowToSuck.Editor
{
    // Keep the ordinary Play button on the same entry as the shipped player.
    [InitializeOnLoad]
    public static class ProductProjectEntry
    {
        const string Folder = "Assets/_Game/Production/Scenes/";
        const string Initialized = "HowToSuck.ProductEntry.Configured";
        static string Preference => "HowToSuck.ProductEntry." + Application.dataPath;

        static ProductProjectEntry() => EditorApplication.delayCall += ConfigureOnce;

        static void ConfigureOnce()
        {
            if (SessionState.GetBool(Initialized, false) || EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            if (EditorPrefs.GetBool(Preference, true)) UseGameEntry();
            SessionState.SetBool(Initialized, true);
        }

        public static void UseGameEntry()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(Folder + "ProductBootstrap.unity");
            if (scene != null) EditorSceneManager.playModeStartScene = scene;
        }

        [MenuItem("How to Suck/Играть", priority = 0)]
        public static void PlayGame()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            UseGameEntry();
            EditorApplication.EnterPlaymode();
        }

        [MenuItem("How to Suck/Play всегда через главное меню", priority = 10)]
        static void ToggleEntry()
        {
            bool enabled = !EditorPrefs.GetBool(Preference, true);
            EditorPrefs.SetBool(Preference, enabled);
            if (enabled) UseGameEntry(); else EditorSceneManager.playModeStartScene = null;
        }

        [MenuItem("How to Suck/Play всегда через главное меню", true)]
        static bool ToggleEntryChecked()
        {
            Menu.SetChecked("How to Suck/Play всегда через главное меню", EditorPrefs.GetBool(Preference, true));
            return !EditorApplication.isPlayingOrWillChangePlaymode;
        }

        static void Open(string name)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var path = Folder + name + ".unity";
            EditorSceneManager.OpenScene(path);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
        }

        [MenuItem("How to Suck/Сцены/Главное меню", priority = 20)] static void OpenEntry() => Open("ProductEntry");
        [MenuItem("How to Suck/Сцены/Контракты", priority = 21)] static void OpenLobby() => Open("ProductLobby");
        [MenuItem("How to Suck/Сцены/Старый дом", priority = 22)] static void OpenHouse() => Open("OldHouseNetwork");
        [MenuItem("How to Suck/Сцены/Супермаркет", priority = 23)] static void OpenMarket() => Open("SupermarketNetwork");
        [MenuItem("How to Suck/Сцены/Склад", priority = 24)] static void OpenWarehouse() => Open("WarehouseNetwork");
    }
}
