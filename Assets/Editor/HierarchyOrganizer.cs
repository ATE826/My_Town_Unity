using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Наводит порядок в иерархии сцены: группирует разрозненное и сортирует.
// Ничего не удаляет и не двигает в пространстве: мировые позиции всех объектов проверяются до и после.
// Перед работой копия сцены кладётся в <проект>/Backups.
public static class HierarchyOrganizer
{
    [MenuItem("Tools/Organize Hierarchy")]
    private static void RunFromMenu()
    {
        Run(SceneManager.GetActiveScene());
    }

    public static bool Run(Scene scene)
    {
        GameObject city = FindRoot(scene, "City");

        if (city == null)
        {
            Debug.LogWarning("[Hierarchy] Корневой объект City не найден — порядок не наводим.");
            return false;
        }

        // Уже наведён порядок (группа Traffic существует)
        if (city.transform.Find("Traffic") != null)
        {
            return false;
        }

        MakeBackup(scene);

        Dictionary<Transform, Pose> before = Snapshot(scene, out int countBefore);

        Transform environment = city.transform.Find("Environment");
        Transform construction = city.transform.Find("Construction");

        // ---------- Environment ----------
        if (environment != null)
        {
            Transform lighting = Group("Lighting & Sound", environment);
            Move(environment.Find("Directional Light"), lighting);
            Move(environment.Find("Global Volume"), lighting);
            Move(environment.Find("Audio Source"), lighting);

            // Знак из корня иерархии — к уличным объектам
            GameObject sign = FindRoot(scene, "Sign");
            Transform objects = environment.Find("Objects");

            if (sign != null && objects != null)
            {
                Move(sign.transform, objects);
            }
        }

        // ---------- Construction ----------
        if (construction != null)
        {
            Transform vehicles = Group("Vehicles", construction);
            Move(construction.Find("Excavator"), vehicles);
            Move(construction.Find("BulldozerDrive"), vehicles);

            Transform site = Group("Site", construction);
            Move(construction.Find("Old house"), site);
            Move(construction.Find("Concrete_Block_Set"), site);
            Move(construction.Find("Construction fence"), site);

            foreach (Transform child in Children(construction))
            {
                if (child.name.StartsWith("Plastic Toilet Cabin", StringComparison.Ordinal))
                {
                    Move(child, site);
                }
            }

            GameObject crew = FindRoot(scene, "ConstructionCrew");

            if (crew != null)
            {
                Transform crewGroup = Group("Crew", construction);
                Move(crew.transform, crewGroup);
            }

            Order(construction, "Site", "Vehicles", "Crew");
        }

        // ---------- Traffic ----------
        Transform traffic = Group("Traffic", city.transform);

        foreach (string name in new[] { "Transport", "Cars_on_road_1", "Cars_on_road_2", "Cars_on_road_3", "Pedestrians_on_road", "Airplanes" })
        {
            Move(city.transform.Find(name), traffic);
        }

        Order(traffic, "Transport", "Cars_on_road_1", "Cars_on_road_2", "Cars_on_road_3", "Pedestrians_on_road", "Airplanes");

        // ---------- Сортировка ----------
        if (environment != null)
        {
            Order(environment, "Lighting & Sound", "Road", "Buildings", "Parking", "Traffic lights", "Fence", "Objects");

            // Обычные группы с декорациями (без путевых точек и скриптов) — по алфавиту с учётом чисел
            foreach (string name in new[] { "Road", "Buildings", "Parking", "Traffic lights", "Fence", "Objects" })
            {
                SortNatural(environment.Find(name));
            }
        }

        if (construction != null)
        {
            Transform site = construction.Find("Site");

            if (site != null)
            {
                SortNatural(site.Find("Construction fence"));
            }
        }

        Order(city.transform, "Terrain", "Environment", "Construction", "Traffic", "Portal_city");
        OrderRoots(scene, "City", "Player", "Room");

        // ---------- Проверка: ничего не потеряно и не сдвинулось ----------
        Dictionary<Transform, Pose> after = Snapshot(scene, out int countAfter);

        float maxMove = 0f;
        float maxTurn = 0f;
        int missing = 0;

        foreach (var pair in before)
        {
            if (pair.Key == null || !after.TryGetValue(pair.Key, out Pose pose))
            {
                missing++;
                continue;
            }

            maxMove = Mathf.Max(maxMove, Vector3.Distance(pair.Value.position, pose.position));
            maxTurn = Mathf.Max(maxTurn, Quaternion.Angle(pair.Value.rotation, pose.rotation));
        }

        EditorSceneManager.MarkSceneDirty(scene);

        string report = "[Hierarchy] Готово. Объектов было " + countBefore + ", стало " + countAfter + " (новых групп: " + (countAfter - countBefore) +
                        "). Пропавших: " + missing + ". Макс. сдвиг: " + maxMove.ToString("F5") + " м, поворот: " + maxTurn.ToString("F4") + "°.";

        if (missing > 0 || maxMove > 0.01f || maxTurn > 0.1f)
        {
            Debug.LogError(report + " ВНИМАНИЕ: что-то сдвинулось — используйте Ctrl+Z или копию в папке Backups.");
        }
        else
        {
            Debug.Log(report);
        }

        return true;
    }

    // ---------------------------------------------------------

    private static GameObject FindRoot(Scene scene, string name)
    {
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            if (root.name == name)
            {
                return root;
            }
        }

        return null;
    }

    private static Transform Group(string name, Transform parent)
    {
        Transform existing = parent.Find(name);

        if (existing != null)
        {
            return existing;
        }

        var group = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(group, "Organize Hierarchy");
        group.transform.SetParent(parent, false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;
        group.transform.localScale = Vector3.one;
        return group.transform;
    }

    // Переносит объект в группу, сохраняя его положение в мире
    private static void Move(Transform child, Transform group)
    {
        if (child == null || group == null || child.parent == group)
        {
            return;
        }

        Undo.SetTransformParent(child, group, "Organize Hierarchy");
    }

    private static List<Transform> Children(Transform parent)
    {
        var list = new List<Transform>();

        foreach (Transform child in parent)
        {
            list.Add(child);
        }

        return list;
    }

    // Ставит перечисленных детей первыми в заданном порядке, остальных оставляет следом как были
    private static void Order(Transform parent, params string[] names)
    {
        int index = 0;

        foreach (string name in names)
        {
            Transform child = parent.Find(name);

            if (child != null)
            {
                child.SetSiblingIndex(index++);
            }
        }
    }

    private static void OrderRoots(Scene scene, params string[] names)
    {
        int index = 0;

        foreach (string name in names)
        {
            GameObject root = FindRoot(scene, name);

            if (root != null)
            {
                root.transform.SetSiblingIndex(index++);
            }
        }
    }

    // Только группы декораций: если среди детей есть скрипты или путевые точки (порядок важен) — не трогаем
    private static void SortNatural(Transform parent)
    {
        if (parent == null)
        {
            return;
        }

        foreach (Transform child in parent)
        {
            string lower = child.name.ToLowerInvariant();

            if (child.GetComponent<MonoBehaviour>() != null || lower.StartsWith("point") || lower.StartsWith("roadpath"))
            {
                return;
            }
        }

        List<Transform> children = Children(parent);
        children.Sort((a, b) => NaturalCompare(a.name, b.name));

        for (int i = 0; i < children.Count; i++)
        {
            children[i].SetSiblingIndex(i);
        }
    }

    private static int NaturalCompare(string a, string b)
    {
        string[] pa = Regex.Split(a.ToLowerInvariant(), @"(\d+)");
        string[] pb = Regex.Split(b.ToLowerInvariant(), @"(\d+)");

        for (int i = 0; i < Math.Min(pa.Length, pb.Length); i++)
        {
            if (pa[i] == pb[i])
            {
                continue;
            }

            bool bothNumbers = long.TryParse(pa[i], out long na) & long.TryParse(pb[i], out long nb);

            return bothNumbers ? na.CompareTo(nb) : string.CompareOrdinal(pa[i], pb[i]);
        }

        return pa.Length.CompareTo(pb.Length);
    }

    private static Dictionary<Transform, Pose> Snapshot(Scene scene, out int count)
    {
        var result = new Dictionary<Transform, Pose>();

        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            {
                result[t] = new Pose(t.position, t.rotation);
            }
        }

        count = result.Count;
        return result;
    }

    private static void MakeBackup(Scene scene)
    {
        try
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string backups = Path.Combine(projectRoot, "Backups");
            Directory.CreateDirectory(backups);

            string source = Path.Combine(projectRoot, scene.path);

            if (File.Exists(source))
            {
                string target = Path.Combine(backups, scene.name + "_before_organize_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".unity");
                File.Copy(source, target, false);
                Debug.Log("[Hierarchy] Копия сцены: " + target);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning("[Hierarchy] Не удалось сделать копию сцены: " + e.Message);
        }
    }
}
