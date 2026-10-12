using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Создаёт в сцене объект ConstructionCrew (бригада строителей) и аниматор рабочих (стойка + ходьба).
public static class ConstructionCrewSetup
{
    private const string PrefabPath = "Assets/Models/Characters/Bubba_The_Handyman/URP/Prefabs/Handyman_ver_5.prefab";
    private const string PrefabFolder = "Assets/Models/Characters/Bubba_The_Handyman/URP/Prefabs";
    private const string WalkModel = "Assets/Models/Characters/Bubba_The_Handyman/URP/Animations/Handyman_walk_animation.fbx";
    private const string IdleModel = "Assets/Models/Characters/Bubba_The_Handyman/URP/Animations/Handyman_idle_1_animation.fbx";
    private const string ControllerPath = "Assets/Generated/WorkerAnimator.controller";

    public static bool Ensure(Scene scene)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            ConstructionCrew existing = root.GetComponentInChildren<ConstructionCrew>(true);

            if (existing != null)
            {
                return FillVariants(existing);
            }
        }

        var house = Object.FindFirstObjectByType<HouseDemolition>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

        if (house == null || prefab == null)
        {
            Debug.LogWarning("[Crew] Нет дома с HouseDemolition или префаба Handyman_ver_5 — бригаду не создаю.");
            return false;
        }

        RuntimeAnimatorController controller = GetOrCreateController();

        var crewObject = new GameObject("ConstructionCrew");
        Undo.RegisterCreatedObjectUndo(crewObject, "Construction Crew");
        SceneManager.MoveGameObjectToScene(crewObject, scene);

        var crew = crewObject.AddComponent<ConstructionCrew>();
        var so = new SerializedObject(crew);

        so.FindProperty("_house").objectReferenceValue = house;
        so.FindProperty("_workerPrefab").objectReferenceValue = prefab;
        so.FindProperty("_workerController").objectReferenceValue = controller;
        AssignVariants(so);

        GameObject blocks = GameObject.Find("Concrete_Block_Set");
        so.FindProperty("_blockSource").objectReferenceValue = blocks != null ? blocks.transform : null;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(scene);
        return true;
    }

    // Разные персонажи: Handyman_ver_1 … ver_5 (Handyman_Full — общий вариант, не берём)
    private static GameObject[] LoadVariants()
    {
        return AssetDatabase.FindAssets("t:Prefab Handyman_ver_", new[] { PrefabFolder })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(path => path)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .Where(prefab => prefab != null)
            .ToArray();
    }

    private static void AssignVariants(SerializedObject so)
    {
        GameObject[] variants = LoadVariants();
        SerializedProperty list = so.FindProperty("_workerPrefabs");
        list.arraySize = variants.Length;

        for (int i = 0; i < variants.Length; i++)
        {
            list.GetArrayElementAtIndex(i).objectReferenceValue = variants[i];
        }

        so.ApplyModifiedProperties();
    }

    // Уже созданная раньше бригада: добавляем список персонажей и размер 1.3
    private static bool FillVariants(ConstructionCrew crew)
    {
        var so = new SerializedObject(crew);

        if (so.FindProperty("_workerPrefabs").arraySize > 0)
        {
            return false;
        }

        AssignVariants(so);

        SerializedProperty scale = so.FindProperty("_workerScale");

        if (scale != null && Mathf.Approximately(scale.floatValue, 0f))
        {
            scale.floatValue = 1.3f;
        }

        so.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(crew.gameObject.scene);
        return true;
    }

    private static RuntimeAnimatorController GetOrCreateController()
    {
        var existing = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (existing != null)
        {
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }

        AnimationClip idle = LoadClip(IdleModel);
        AnimationClip walk = LoadClip(WalkModel);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);

        AnimatorStateMachine machine = controller.layers[0].stateMachine;

        AnimatorState idleState = machine.AddState("Idle");
        idleState.motion = idle;

        AnimatorState walkState = machine.AddState("Walk");
        walkState.motion = walk;

        machine.defaultState = idleState;

        AnimatorStateTransition toWalk = idleState.AddTransition(walkState);
        toWalk.hasExitTime = false;
        toWalk.duration = 0.15f;
        toWalk.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");

        AnimatorStateTransition toIdle = walkState.AddTransition(idleState);
        toIdle.hasExitTime = false;
        toIdle.duration = 0.15f;
        toIdle.AddCondition(AnimatorConditionMode.Less, 0.1f, "Speed");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimationClip LoadClip(string path)
    {
        return AssetDatabase.LoadAllAssetsAtPath(path)
            .OfType<AnimationClip>()
            .FirstOrDefault(c => !c.name.StartsWith("__preview__"));
    }
}
