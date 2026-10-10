using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Сам вешает скрипты на бульдозер и дом в открытой сцене (один раз за сеанс редактора,
// только если они ещё не настроены) и сохраняет сцену.
// Вручную то же самое делают пункты меню Tools → Setup Bulldozer… и Prepare House….
[InitializeOnLoad]
public static class AutoSetup
{
    private const string BulldozerModel = "Assets/bulldozer/source/LP_Tractor2.fbx";
    private const string HouseModel = "Assets/old-house/source/1.fbx";
    private const string SessionKey = "AutoSetup.Done";

    static AutoSetup()
    {
        EditorApplication.delayCall += Run;
        EditorSceneManager.sceneOpened += (scene, mode) => EditorApplication.delayCall += Run;
    }

    [MenuItem("Tools/Run Auto Setup (Bulldozer + House)")]
    private static void RunFromMenu()
    {
        SessionState.SetBool(SessionKey, false);
        Run();
    }

    private static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode ||
            EditorApplication.isCompiling ||
            EditorApplication.isUpdating ||
            SessionState.GetBool(SessionKey, false))
        {
            return;
        }

        Scene scene = SceneManager.GetActiveScene();

        if (!scene.isLoaded)
        {
            return;
        }

        var bulldozers = new List<GameObject>();
        var houses = new List<GameObject>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                GameObject go = t.gameObject;

                if (!PrefabUtility.IsOutermostPrefabInstanceRoot(go))
                {
                    continue;
                }

                string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(go);

                if (path == BulldozerModel && go.GetComponentInParent<BulldozerController>() == null)
                {
                    bulldozers.Add(go);
                }
                else if (path == HouseModel && go.GetComponentInParent<HouseDemolition>() == null)
                {
                    houses.Add(go);
                }
            }
        }

        if (bulldozers.Count == 0 && houses.Count == 0)
        {
            return;
        }

        // Одна попытка за сеанс: при ошибке не крутимся по кругу после каждой перекомпиляции
        SessionState.SetBool(SessionKey, true);

        BulldozerSetup.Silent = true;
        HouseFractureSetup.Silent = true;

        bool changed = false;

        foreach (GameObject bulldozer in bulldozers)
        {
            try
            {
                changed |= BulldozerSetup.Setup(bulldozer);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        foreach (GameObject house in houses)
        {
            try
            {
                changed |= HouseFractureSetup.Run(house);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        BulldozerSetup.Silent = false;
        HouseFractureSetup.Silent = false;

        if (changed)
        {
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[AutoSetup] Скрипты повешены на бульдозер и дом, сцена сохранена.");
        }
    }
}
