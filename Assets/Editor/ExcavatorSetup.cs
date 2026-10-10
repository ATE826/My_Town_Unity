using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

// One-click setup: wraps the selected excavator model into a drivable "Excavator" object,
// finds the arm / bucket / wheels by name and wires up ExcavatorController.
public static class ExcavatorSetup
{
    [MenuItem("Tools/Setup Excavator From Selection")]
    private static void Setup()
    {
        GameObject model = Selection.activeGameObject;

        if (model == null)
        {
            EditorUtility.DisplayDialog("Excavator", "Выделите экскаватор в Hierarchy и запустите снова.", "OK");
            return;
        }

        if (model.GetComponentInParent<ExcavatorController>() != null ||
            model.GetComponentInChildren<ExcavatorController>() != null)
        {
            EditorUtility.DisplayDialog("Excavator", "Этот экскаватор уже настроен.", "OK");
            return;
        }

        Transform boom = Find(model.transform, "whole_arm") ?? Find(model.transform, "arm1");
        Transform bucket = Find(model.transform, "plough") ?? Find(model.transform, "PLOUGH1");

        if (boom == null || bucket == null || !TryBounds(model.transform, out Bounds body))
        {
            EditorUtility.DisplayDialog(
                "Excavator",
                "Не нашёл стрелу или ковш по именам (whole_arm / arm1, plough).\n\nЧасти модели:\n" + ListChildren(model.transform),
                "OK");
            return;
        }

        if (!TryBounds(bucket, out Bounds bucketBounds))
        {
            EditorUtility.DisplayDialog("Excavator", "У ковша нет мешей (Renderer).", "OK");
            return;
        }

        // «Вперёд» = от центра корпуса к ковшу
        Vector3 front = bucketBounds.center - body.center;
        front.y = 0f;
        front = front.sqrMagnitude < 0.01f ? model.transform.forward : front.normalized;
        Vector3 side = Vector3.Cross(Vector3.up, front);

        Transform oldParent = model.transform.parent;

        // Корневой объект: центр основания, смотрит вперёд
        var root = new GameObject("Excavator");
        Undo.RegisterCreatedObjectUndo(root, "Setup Excavator");
        root.transform.SetParent(oldParent, false);
        root.transform.SetPositionAndRotation(
            new Vector3(body.center.x, body.min.y, body.center.z),
            Quaternion.LookRotation(front, Vector3.up));
        Undo.SetTransformParent(model.transform, root.transform, "Setup Excavator");

        // Коллайдер и физика
        Bounds local = LocalBounds(root.transform, model.GetComponentsInChildren<Renderer>());
        var box = Undo.AddComponent<BoxCollider>(root);
        box.center = local.center;
        box.size = local.size;

        var rb = Undo.AddComponent<Rigidbody>(root);
        rb.mass = 8000f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 2f;

        // Точка касания ковша с землёй
        var tip = new GameObject("BucketTip").transform;
        tip.SetParent(bucket, false);
        float extent = Mathf.Abs(front.x) * bucketBounds.extents.x + Mathf.Abs(front.z) * bucketBounds.extents.z;
        tip.position = new Vector3(bucketBounds.center.x, bucketBounds.min.y, bucketBounds.center.z) + front * extent;

        // «Песок» в ковше
        Transform sand = BuildSand(bucket, tip, out Vector3 growAxis);

        // Сиденье и камера кабины
        var seat = new GameObject("CabSeat").transform;
        seat.SetParent(root.transform, false);
        seat.localPosition = new Vector3(local.center.x, local.min.y + local.size.y * 0.8f, local.center.z - local.size.z * 0.1f);

        var camObject = new GameObject("CabCamera");
        camObject.transform.SetParent(seat, false);
        var cam = camObject.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.enabled = false;
        var listener = camObject.AddComponent<AudioListener>();
        listener.enabled = false;

        // Звуки
        AudioSource drive = CreateSound(root.transform, "Sound_Drive", true);
        AudioSource scoop = CreateSound(root.transform, "Sound_Scoop", false);
        AudioSource dump = CreateSound(root.transform, "Sound_Dump", false);

        // Частицы песка
        ParticleSystem sandParticles = CreateSandParticles(tip, bucketBounds, front);

        // У этой модели гусеницы, а не колёса: колёса не вращаем, гусеницы ищет AutoDetectTracks
        var wheels = new List<Transform>();
        var wheelAxes = new List<Vector3>();
        float wheelRadius = 0.8f;

        // Контроллер
        var controller = Undo.AddComponent<ExcavatorController>(root);
        var so = new SerializedObject(controller);

        so.FindProperty("_boom").objectReferenceValue = boom;
        so.FindProperty("_bucket").objectReferenceValue = bucket;
        so.FindProperty("_bucketTip").objectReferenceValue = tip;
        so.FindProperty("_sandVisual").objectReferenceValue = sand;
        so.FindProperty("_sandGrowAxis").vector3Value = growAxis;
        so.FindProperty("_cabCamera").objectReferenceValue = cam;
        so.FindProperty("_sandParticles").objectReferenceValue = sandParticles;
        so.FindProperty("_driveSound").objectReferenceValue = drive;
        so.FindProperty("_scoopSound").objectReferenceValue = scoop;
        so.FindProperty("_dumpSound").objectReferenceValue = dump;
        so.FindProperty("_wheelRadius").floatValue = wheelRadius;

        SerializedProperty wheelsProp = so.FindProperty("_wheels");
        SerializedProperty axesProp = so.FindProperty("_wheelAxes");
        wheelsProp.arraySize = wheels.Count;
        axesProp.arraySize = wheels.Count;

        for (int i = 0; i < wheels.Count; i++)
        {
            wheelsProp.GetArrayElementAtIndex(i).objectReferenceValue = wheels[i];
            axesProp.GetArrayElementAtIndex(i).vector3Value = wheelAxes[i];
        }

        // Ось стрелы и ковша: горизонтальная, поперёк машины (в локальных осях самого узла)
        so.FindProperty("_boomAxis").vector3Value = boom.InverseTransformDirection(side).normalized;
        so.FindProperty("_bucketAxis").vector3Value = bucket.InverseTransformDirection(side).normalized;
        so.ApplyModifiedProperties();

        controller.AutoDetectTracks();
        EditorUtility.SetDirty(controller);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);

        var message = new StringBuilder();
        message.AppendLine("Экскаватор настроен: объект «Excavator».");
        message.AppendLine();
        message.AppendLine("Найдено: стрела = " + boom.name + ", ковш = " + bucket.name + ", гусениц найдено: " + new SerializedObject(controller).FindProperty("_tracks").arraySize + " (если 0 или не те, перетащите меши в поле Tracks).");

        if (!bucket.IsChildOf(boom))
        {
            message.AppendLine();
            message.AppendLine("Внимание: ковш не является дочерним для стрелы, поэтому при подъёме стрелы он не поедет за ней.");
        }

        if (Terrain.activeTerrains.Length > 0)
        {
            Terrain terrain = Terrain.activeTerrains[0];
            float cell = terrain.terrainData.size.x / (terrain.terrainData.heightmapResolution - 1);

            if (cell > 1f)
            {
                message.AppendLine();
                message.AppendLine("Рельеф грубый: одна точка высоты = " + cell.ToString("F1") + " м. Яма будет угловатой. Уменьшите размер Terrain или поднимите Heightmap Resolution.");
            }
        }
        else
        {
            message.AppendLine();
            message.AppendLine("В сцене нет Terrain: копать будет нечего.");
        }

        EditorUtility.DisplayDialog("Excavator", message.ToString(), "OK");
    }

    // ---------------------------------------------------------

    private static Transform Find(Transform root, string name)
    {
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            if (string.Equals(ShortName(t), name, StringComparison.OrdinalIgnoreCase))
            {
                return t;
            }
        }

        return null;
    }

    // Maya добавляет к именам префикс пространства имён: "poclain:plough" -> "plough"
    private static string ShortName(Transform t)
    {
        int colon = t.name.LastIndexOf(':');
        return colon >= 0 ? t.name.Substring(colon + 1) : t.name;
    }

    private static string ListChildren(Transform root)
    {
        var builder = new StringBuilder();
        int count = 0;

        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
        {
            builder.AppendLine(t.name);

            if (++count >= 120)
            {
                builder.AppendLine("...");
                break;
            }
        }

        return builder.ToString();
    }

    private static bool TryBounds(Transform t, out Bounds bounds)
    {
        Renderer[] renderers = t.GetComponentsInChildren<Renderer>();
        bounds = default;

        if (renderers.Length == 0)
        {
            return false;
        }

        bounds = renderers[0].bounds;

        foreach (Renderer r in renderers)
        {
            bounds.Encapsulate(r.bounds);
        }

        return true;
    }

    private static Bounds LocalBounds(Transform space, Renderer[] renderers)
    {
        Bounds result = default;
        bool first = true;

        foreach (Renderer r in renderers)
        {
            Bounds b = r.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = b.center + Vector3.Scale(b.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1,
                    (i & 2) == 0 ? -1 : 1,
                    (i & 4) == 0 ? -1 : 1));

                Vector3 p = space.InverseTransformPoint(corner);

                if (first)
                {
                    result = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                {
                    result.Encapsulate(p);
                }
            }
        }

        return result;
    }

    private static AudioSource CreateSound(Transform parent, string name, bool loop)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);

        var source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = loop;
        source.spatialBlend = 1f;
        source.maxDistance = 60f;

        return source;
    }

    // Песок в ковше: слой по дну, по форме внутреннего сечения самого ковша.
    // Контур берётся срезом меша ковша у дна и чуть сжимается, поэтому песок не выходит за стенки.
    // Растёт вверх по мере наполнения.
    private static Transform BuildSand(Transform bucket, Transform tip, out Vector3 growAxis)
    {
        growAxis = Vector3.up;

        if (!TryBucketLocalBounds(bucket, out Bounds b))
        {
            return null;
        }

        // Какая локальная ось ковша смотрит вверх (ковш в исходном положении)
        Vector3 up = bucket.InverseTransformDirection(Vector3.up);
        int axis = 0;
        if (Mathf.Abs(up.y) > Mathf.Abs(up[axis])) axis = 1;
        if (Mathf.Abs(up.z) > Mathf.Abs(up[axis])) axis = 2;

        float sign = up[axis] >= 0f ? 1f : -1f;
        growAxis = Vector3.zero;
        growAxis[axis] = 1f;

        int h1 = (axis + 1) % 3;
        int h2 = (axis + 2) % 3;

        float floor = sign > 0f ? b.min[axis] : b.max[axis];
        float size = b.size[axis];
        float sliceLevel = floor + sign * size * 0.15f;
        float height = size * 0.3f;

        // Контур ковша на высоте среза (2D: h1, h2)
        List<Vector2> hull = SliceHull(bucket, axis, h1, h2, sliceLevel);

        if (hull.Count < 3)
        {
            hull = new List<Vector2>
            {
                new Vector2(b.min[h1], b.min[h2]), new Vector2(b.max[h1], b.min[h2]),
                new Vector2(b.max[h1], b.max[h2]), new Vector2(b.min[h1], b.max[h2])
            };
        }

        Vector2 centroid = Vector2.zero;
        foreach (Vector2 point in hull) centroid += point;
        centroid /= hull.Count;

        // Сжимаем контур: стенки ковша имеют толщину
        for (int i = 0; i < hull.Count; i++)
        {
            hull[i] = centroid + (hull[i] - centroid) * 0.85f;
        }

        var pivot = new GameObject("SandPivot").transform;
        Undo.RegisterCreatedObjectUndo(pivot.gameObject, "Sand");
        pivot.SetParent(bucket, false);

        Vector3 origin = Vector3.zero;
        origin[h1] = centroid.x;
        origin[h2] = centroid.y;
        origin[axis] = floor + sign * size * 0.03f;
        pivot.localPosition = origin;
        pivot.localRotation = Quaternion.identity;
        pivot.localScale = Vector3.one;

        var sand = new GameObject("SandInBucket");
        sand.transform.SetParent(pivot, false);
        sand.AddComponent<MeshFilter>().sharedMesh = BuildPrismMesh(hull, centroid, axis, h1, h2, sign, height);

        var sandRenderer = sand.AddComponent<MeshRenderer>();
        sandRenderer.sharedMaterial = GetMaterial("SandMat", LitShader(), new Color(0.76f, 0.65f, 0.42f));
        sandRenderer.shadowCastingMode = ShadowCastingMode.Off;

        pivot.gameObject.SetActive(false);
        return pivot;
    }

    // Точки пересечения мешей ковша с плоскостью на заданном уровне -> выпуклая оболочка
    private static List<Vector2> SliceHull(Transform bucket, int axis, int h1, int h2, float level)
    {
        var points = new List<Vector2>();

        foreach (Renderer r in bucket.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = null;

            if (r is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }

            if (mesh == null)
            {
                continue;
            }

            Vector3[] vertices = mesh.vertices;

            if (vertices.Length == 0)
            {
                continue;
            }

            var local = new Vector3[vertices.Length];

            for (int i = 0; i < vertices.Length; i++)
            {
                local[i] = bucket.InverseTransformPoint(r.transform.TransformPoint(vertices[i]));
            }

            for (int sub = 0; sub < mesh.subMeshCount; sub++)
            {
                int[] indices = mesh.GetTriangles(sub);

                for (int t = 0; t + 2 < indices.Length; t += 3)
                {
                    Vector3 a = local[indices[t]];
                    Vector3 c = local[indices[t + 1]];
                    Vector3 d = local[indices[t + 2]];

                    AddCrossing(points, a, c, axis, h1, h2, level);
                    AddCrossing(points, c, d, axis, h1, h2, level);
                    AddCrossing(points, d, a, axis, h1, h2, level);
                }
            }
        }

        return ConvexHull(points);
    }

    private static void AddCrossing(List<Vector2> points, Vector3 p, Vector3 q, int axis, int h1, int h2, float level)
    {
        float vp = p[axis] - level;
        float vq = q[axis] - level;

        if (vp * vq >= 0f)
        {
            return;
        }

        Vector3 hit = p + (q - p) * (vp / (vp - vq));
        points.Add(new Vector2(hit[h1], hit[h2]));
    }

    private static List<Vector2> ConvexHull(List<Vector2> points)
    {
        points.Sort((x, y) => x.x != y.x ? x.x.CompareTo(y.x) : x.y.CompareTo(y.y));

        if (points.Count < 3)
        {
            return points;
        }

        var hull = new List<Vector2>();

        for (int pass = 0; pass < 2; pass++)
        {
            int start = hull.Count;

            for (int i = 0; i < points.Count; i++)
            {
                int idx = pass == 0 ? i : points.Count - 1 - i;

                while (hull.Count >= start + 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], points[idx]) <= 0f)
                {
                    hull.RemoveAt(hull.Count - 1);
                }

                hull.Add(points[idx]);
            }

            hull.RemoveAt(hull.Count - 1);
        }

        return hull;
    }

    private static float Cross(Vector2 o, Vector2 a, Vector2 b)
    {
        return (a.x - o.x) * (b.y - o.y) - (a.y - o.y) * (b.x - o.x);
    }

    // Призма: контур hull, снизу (u = 0) вверх на height по оси axis. Координаты в осях ковша.
    private static Mesh BuildPrismMesh(List<Vector2> hull, Vector2 centroid, int axis, int h1, int h2, float sign, float height)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();

        Vector3 Point(Vector2 p, float u)
        {
            Vector3 v = Vector3.zero;
            v[h1] = p.x - centroid.x;
            v[h2] = p.y - centroid.y;
            v[axis] = sign * u;
            return v;
        }

        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            if (Vector3.Dot(Vector3.Cross(b - a, c - a), outward) < 0f)
            {
                Vector3 swap = b;
                b = c;
                c = swap;
            }

            int i = vertices.Count;
            vertices.Add(a);
            vertices.Add(b);
            vertices.Add(c);
            triangles.Add(i);
            triangles.Add(i + 1);
            triangles.Add(i + 2);
        }

        Vector3 upVector = Vector3.zero;
        upVector[axis] = sign;

        Vector3 topCenter = Point(centroid, height);

        for (int i = 0; i < hull.Count; i++)
        {
            Vector2 p0 = hull[i];
            Vector2 p1 = hull[(i + 1) % hull.Count];

            Vector3 b0 = Point(p0, 0f);
            Vector3 b1 = Point(p1, 0f);
            Vector3 t0 = Point(p0, height);
            Vector3 t1 = Point(p1, height);

            Tri(topCenter, t0, t1, upVector);

            Vector3 outward = Point((p0 + p1) * 0.5f, 0f);
            outward[axis] = 0f;
            Tri(b0, b1, t1, outward);
            Tri(b0, t1, t0, outward);
        }

        var mesh = new Mesh { name = "SandPrism" };
        mesh.SetVertices(vertices);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private static bool TryBucketLocalBounds(Transform bucket, out Bounds result)
    {
        result = default;
        bool first = true;

        foreach (Renderer r in bucket.GetComponentsInChildren<Renderer>())
        {
            Mesh mesh = null;

            if (r is SkinnedMeshRenderer skinned)
            {
                mesh = skinned.sharedMesh;
            }
            else
            {
                MeshFilter filter = r.GetComponent<MeshFilter>();
                if (filter != null) mesh = filter.sharedMesh;
            }

            if (mesh == null)
            {
                continue;
            }

            Bounds mb = mesh.bounds;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3(
                    (i & 1) == 0 ? -1 : 1,
                    (i & 2) == 0 ? -1 : 1,
                    (i & 4) == 0 ? -1 : 1));

                Vector3 p = bucket.InverseTransformPoint(r.transform.TransformPoint(corner));

                if (first)
                {
                    result = new Bounds(p, Vector3.zero);
                    first = false;
                }
                else
                {
                    result.Encapsulate(p);
                }
            }
        }

        return !first;
    }

    [MenuItem("Tools/Rebuild Excavator Sand")]
    private static void RebuildSand()
    {
        ExcavatorController controller = Selection.activeGameObject != null
            ? Selection.activeGameObject.GetComponentInParent<ExcavatorController>()
            : null;

        if (controller == null)
        {
            controller = UnityEngine.Object.FindFirstObjectByType<ExcavatorController>();
        }

        if (controller == null)
        {
            EditorUtility.DisplayDialog("Excavator", "В сцене нет настроенного экскаватора.", "OK");
            return;
        }

        var so = new SerializedObject(controller);
        var bucket = so.FindProperty("_bucket").objectReferenceValue as Transform;

        if (bucket == null)
        {
            EditorUtility.DisplayDialog("Excavator", "У экскаватора не назначен ковш.", "OK");
            return;
        }

        // Убираем прежний «шарик»/песок
        var old = so.FindProperty("_sandVisual").objectReferenceValue as Transform;

        if (old != null)
        {
            Undo.DestroyObjectImmediate(old.gameObject);
        }

        Transform pivot = BuildSand(bucket, so.FindProperty("_bucketTip").objectReferenceValue as Transform, out Vector3 growAxis);

        so.Update();
        so.FindProperty("_sandVisual").objectReferenceValue = pivot;
        so.FindProperty("_sandGrowAxis").vector3Value = growAxis;
        so.ApplyModifiedProperties();

        EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
    }

    private static ParticleSystem CreateSandParticles(Transform tip, Bounds bucketBounds, Vector3 front)
    {
        var go = new GameObject("SandParticles");
        go.transform.SetParent(tip, false);

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var main = ps.main;
        main.playOnAwake = false;
        main.loop = true;
        main.duration = 1f;
        main.startLifetime = 1.5f;
        main.startSpeed = 0.4f;
        main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.22f);
        main.startColor = new Color(0.76f, 0.65f, 0.42f);
        main.gravityModifier = 1.6f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 3000;

        var emission = ps.emission;
        emission.rateOverTime = 300f;

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Box;
        shape.scale = new Vector3(
            Mathf.Max(bucketBounds.size.x, bucketBounds.size.z) * 0.6f,
            0.1f,
            0.3f);

        var collision = ps.collision;
        collision.enabled = true;
        collision.type = ParticleSystemCollisionType.World;
        collision.dampen = 0.9f;
        collision.bounce = 0f;
        collision.lifetimeLoss = 0.4f;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = GetMaterial("SandParticle", ParticleShader(), new Color(0.76f, 0.65f, 0.42f));

        return ps;
    }

    private static Material GetMaterial(string name, Shader shader, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);

        if (existing != null)
        {
            return existing;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }

        if (shader == null)
        {
            shader = Shader.Find("Sprites/Default");
        }

        var material = new Material(shader);

        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color")) material.SetColor("_Color", color);

        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static string PipelineName()
    {
        RenderPipelineAsset rp = GraphicsSettings.currentRenderPipeline;
        return rp != null ? rp.GetType().Name : "";
    }

    private static Shader LitShader()
    {
        string pipeline = PipelineName();

        if (pipeline.Contains("HD")) return Shader.Find("HDRP/Lit");
        if (pipeline.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Lit");
        return Shader.Find("Standard");
    }

    private static Shader ParticleShader()
    {
        string pipeline = PipelineName();

        if (pipeline.Contains("HD")) return Shader.Find("HDRP/Unlit");
        if (pipeline.Contains("Universal")) return Shader.Find("Universal Render Pipeline/Particles/Unlit");
        return Shader.Find("Particles/Standard Unlit");
    }
}
