using System;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// One-click setup: wraps the selected bulldozer model into a drivable "Bulldozer" object.
public static class BulldozerSetup
{
    // true — вызов из авто-настройки: вместо окон пишем в Console
    public static bool Silent;

    [MenuItem("Tools/Setup Bulldozer From Selection")]
    private static void SetupFromSelection()
    {
        Silent = false;
        Setup(Selection.activeGameObject);
    }

    private static void Notify(string message)
    {
        if (Silent)
        {
            Debug.Log("[Bulldozer] " + message);
        }
        else
        {
            EditorUtility.DisplayDialog("Bulldozer", message, "OK");
        }
    }

    public static bool Setup(GameObject model)
    {
        if (model == null)
        {
            Notify("Выделите бульдозер в Hierarchy и запустите снова.");
            return false;
        }

        if (model.GetComponentInParent<BulldozerController>() != null ||
            model.GetComponentInChildren<BulldozerController>() != null)
        {
            Notify("Этот бульдозер уже настроен.");
            return false;
        }

        if (!TryBounds(model.transform, out Bounds body))
        {
            Notify("В выделенном объекте нет мешей.");
            return false;
        }

        // «Вперёд»: кабина у бульдозера сзади, значит вперёд — от стёкол к центру корпуса
        Transform glass = Find(model.transform, "glass");
        Vector3 front = model.transform.forward;
        bool frontGuessed = false;

        if (glass != null && TryBounds(glass, out Bounds glassBounds))
        {
            Vector3 toFront = body.center - glassBounds.center;
            toFront.y = 0f;

            if (toFront.magnitude > 0.3f)
            {
                front = SnapToModelAxis(model.transform, toFront.normalized);
                frontGuessed = true;
            }
        }

        front.y = 0f;
        front.Normalize();

        Transform oldParent = model.transform.parent;

        var root = new GameObject("BulldozerDrive");
        Undo.RegisterCreatedObjectUndo(root, "Setup Bulldozer");
        root.transform.SetParent(oldParent, false);
        root.transform.SetPositionAndRotation(
            new Vector3(body.center.x, body.min.y, body.center.z),
            Quaternion.LookRotation(front, Vector3.up));
        Undo.SetTransformParent(model.transform, root.transform, "Setup Bulldozer");

        Bounds local = LocalBounds(root.transform, model.GetComponentsInChildren<Renderer>());

        var box = Undo.AddComponent<BoxCollider>(root);
        box.center = local.center;
        box.size = local.size;

        var rb = Undo.AddComponent<Rigidbody>(root);
        rb.mass = 12000f;
        rb.linearDamping = 0.5f;
        rb.angularDamping = 2f;

        // Сиденье: за стеклом кабины, иначе в верхней задней части
        var seat = new GameObject("CabSeat").transform;
        seat.SetParent(root.transform, false);

        if (glass != null && TryBounds(glass, out Bounds g))
        {
            seat.position = g.center;
        }
        else
        {
            seat.localPosition = new Vector3(local.center.x, local.min.y + local.size.y * 0.75f, local.center.z - local.size.z * 0.15f);
        }

        var camObject = new GameObject("CabCamera");
        camObject.transform.SetParent(seat, false);
        var cam = camObject.AddComponent<Camera>();
        cam.nearClipPlane = 0.1f;
        cam.enabled = false;
        var listener = camObject.AddComponent<AudioListener>();
        listener.enabled = false;

        var soundObject = new GameObject("Sound_Drive");
        soundObject.transform.SetParent(root.transform, false);
        var drive = soundObject.AddComponent<AudioSource>();
        drive.playOnAwake = false;
        drive.loop = true;
        drive.spatialBlend = 1f;
        drive.maxDistance = 60f;

        var controller = Undo.AddComponent<BulldozerController>(root);
        var so = new SerializedObject(controller);
        so.FindProperty("_cabCamera").objectReferenceValue = cam;
        so.FindProperty("_driveSound").objectReferenceValue = drive;
        so.ApplyModifiedProperties();

        controller.AutoDetectTracks();
        EditorUtility.SetDirty(controller);

        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);

        var message = new StringBuilder();
        message.AppendLine("Бульдозер настроен: объект «BulldozerDrive».");
        message.AppendLine();
        message.AppendLine("Гусениц найдено: " + new SerializedObject(controller).FindProperty("_tracks").arraySize + " (если 0 или не те, перетащите меши в поле Tracks).");
        message.AppendLine();
        message.AppendLine(frontGuessed
            ? "Направление «вперёд» определено по кабине. Если бульдозер поедет задом наперёд, поверните объект BulldozerDrive на 180° по Y."
            : "Направление «вперёд» не удалось определить. Если бульдозер поедет не туда, поверните объект BulldozerDrive по Y (на 180° или 90°).");

        Notify(message.ToString());
        return true;
    }

    // ---------------------------------------------------------

    private static Vector3 SnapToModelAxis(Transform model, Vector3 direction)
    {
        Vector3[] axes = { model.forward, -model.forward, model.right, -model.right };
        Vector3 best = axes[0];
        float bestDot = float.MinValue;

        foreach (Vector3 axis in axes)
        {
            Vector3 flat = new Vector3(axis.x, 0f, axis.z).normalized;
            float dot = Vector3.Dot(flat, direction);

            if (dot > bestDot)
            {
                bestDot = dot;
                best = flat;
            }
        }

        return best;
    }

    private static string ShortName(Transform t)
    {
        int colon = t.name.LastIndexOf(':');
        return colon >= 0 ? t.name.Substring(colon + 1) : t.name;
    }

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
}
