using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Режет выделенное здание на куски заранее (в редакторе): каждый кусок — отдельный объект
// с мешем, коллайдером и Rigidbody. Исходные меши отключаются.
public static class HouseFractureSetup
{
    private const float ChunkSize = 2f;       // желаемый размер куска, м
    private const int MaxCells = 600;         // верхняя граница числа ячеек на здание
    private const int MaxTrianglesPerMesh = 300000;

    private struct Vertex
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 Uv;
    }

    private struct Triangle
    {
        public Vertex A, B, C;
        public int Material;
    }

    // true — вызов из авто-настройки: вместо окон пишем в Console
    public static bool Silent;

    [MenuItem("Tools/Prepare House For Demolition From Selection")]
    private static void RunFromSelection()
    {
        Silent = false;
        Run(Selection.activeGameObject);
    }

    private static void Notify(string message)
    {
        if (Silent)
        {
            Debug.Log("[Demolition] " + message);
        }
        else
        {
            EditorUtility.DisplayDialog("Demolition", message, "OK");
        }
    }

    public static bool Run(GameObject root)
    {
        if (root == null)
        {
            Notify("Выделите дом в Hierarchy и запустите снова.");
            return false;
        }

        if (root.GetComponentInChildren<DemolitionChunk>(true) != null || root.GetComponent<HouseDemolition>() != null)
        {
            Notify("Этот дом уже подготовлен.");
            return false;
        }

        var sources = new List<MeshRenderer>();

        foreach (MeshRenderer r in root.GetComponentsInChildren<MeshRenderer>())
        {
            if (r.GetComponent<MeshFilter>() != null && r.GetComponent<MeshFilter>().sharedMesh != null)
            {
                sources.Add(r);
            }
        }

        if (sources.Count == 0)
        {
            Notify("В выделенном объекте нет мешей.");
            return false;
        }

        // Габариты здания и размер ячейки
        Bounds bounds = sources[0].bounds;
        foreach (MeshRenderer r in sources) bounds.Encapsulate(r.bounds);

        float cell = ChunkSize;
        while (Cells(bounds, cell) > MaxCells)
        {
            cell *= 1.25f;
        }

        // Читаем меши (если отключено чтение, предлагаем включить)
        if (!EnsureReadable(sources))
        {
            return false;
        }

        if (!AssetDatabase.IsValidFolder("Assets/Generated"))
        {
            AssetDatabase.CreateFolder("Assets", "Generated");
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath("Assets/Generated/" + root.name + "_Chunks.asset");
        bool assetCreated = false;

        var container = new GameObject("Chunks");
        Undo.RegisterCreatedObjectUndo(container, "Prepare House");
        container.transform.SetParent(root.transform, false);

        int chunkCount = 0;
        float progress = 0f;

        try
        {
            foreach (MeshRenderer renderer in sources)
            {
                EditorUtility.DisplayProgressBar("Разрезаю дом", renderer.name, progress / sources.Count);
                progress++;

                List<Triangle> triangles = ReadTriangles(renderer, cell);
                var groups = new Dictionary<long, List<Triangle>>();

                foreach (Triangle t in triangles)
                {
                    Vector3 centroid = (t.A.Position + t.B.Position + t.C.Position) / 3f;
                    long key = CellKey(centroid, bounds.min, cell);

                    if (!groups.TryGetValue(key, out List<Triangle> list))
                    {
                        list = new List<Triangle>();
                        groups.Add(key, list);
                    }

                    list.Add(t);
                }

                foreach (var pair in groups)
                {
                    Mesh mesh = BuildChunk(pair.Value, renderer.sharedMaterials, out Vector3 center, out Material[] materials);

                    if (mesh == null)
                    {
                        continue;
                    }

                    if (!assetCreated)
                    {
                        AssetDatabase.CreateAsset(mesh, assetPath);
                        assetCreated = true;
                    }
                    else
                    {
                        AssetDatabase.AddObjectToAsset(mesh, assetPath);
                    }

                    CreateChunkObject(container.transform, mesh, materials, center, ++chunkCount);
                }

                renderer.enabled = false;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
        }

        // Прежние коллайдеры дома мешали бы технике и невидимо стояли на месте
        foreach (Collider c in root.GetComponentsInChildren<Collider>())
        {
            if (c.GetComponentInParent<DemolitionChunk>() == null)
            {
                c.enabled = false;
            }
        }

        Undo.AddComponent<HouseDemolition>(root);

        AssetDatabase.SaveAssets();
        EditorSceneManager.MarkSceneDirty(root.scene);

        Notify("Готово: дом разрезан на " + chunkCount + " кусков (объект Chunks внутри дома).\n\n" +
            "Теперь наедьте на дом бульдозером.");
        return true;
    }

    // ---------------------------------------------------------

    private static int Cells(Bounds b, float cell)
    {
        return Mathf.CeilToInt(b.size.x / cell + 1) * Mathf.CeilToInt(b.size.y / cell + 1) * Mathf.CeilToInt(b.size.z / cell + 1);
    }

    private static long CellKey(Vector3 p, Vector3 origin, float cell)
    {
        long x = Mathf.FloorToInt((p.x - origin.x) / cell);
        long y = Mathf.FloorToInt((p.y - origin.y) / cell);
        long z = Mathf.FloorToInt((p.z - origin.z) / cell);
        return (x * 4096 + y) * 4096 + z;
    }

    private static bool EnsureReadable(List<MeshRenderer> sources)
    {
        var toEnable = new HashSet<string>();

        foreach (MeshRenderer r in sources)
        {
            Mesh mesh = r.GetComponent<MeshFilter>().sharedMesh;

            if (mesh.vertexCount > 0 && mesh.vertices.Length == 0)
            {
                string path = AssetDatabase.GetAssetPath(mesh);

                if (AssetImporter.GetAtPath(path) is ModelImporter)
                {
                    toEnable.Add(path);
                }
            }
        }

        if (toEnable.Count == 0)
        {
            return true;
        }

        if (!Silent && !EditorUtility.DisplayDialog(
            "Demolition",
            "Меши дома закрыты для чтения. Включить Read/Write в настройках импорта модели и переимпортировать её?",
            "Включить",
            "Отмена"))
        {
            return false;
        }

        foreach (string path in toEnable)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = true;
            importer.SaveAndReimport();
        }

        return true;
    }

    // Треугольники меша в мировых координатах; длинные рёбра делятся пополам
    private static List<Triangle> ReadTriangles(MeshRenderer renderer, float maxEdge)
    {
        Mesh mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
        Matrix4x4 matrix = renderer.transform.localToWorldMatrix;

        Vector3[] positions = mesh.vertices;
        Vector3[] normals = mesh.normals;
        Vector2[] uvs = mesh.uv;

        var vertices = new Vertex[positions.Length];

        for (int i = 0; i < positions.Length; i++)
        {
            vertices[i] = new Vertex
            {
                Position = matrix.MultiplyPoint3x4(positions[i]),
                Normal = normals.Length > i ? matrix.MultiplyVector(normals[i]).normalized : Vector3.zero,
                Uv = uvs.Length > i ? uvs[i] : Vector2.zero
            };
        }

        var result = new List<Triangle>();
        var stack = new Stack<Triangle>();

        for (int sub = 0; sub < mesh.subMeshCount; sub++)
        {
            int[] indices = mesh.GetTriangles(sub);

            for (int t = 0; t + 2 < indices.Length; t += 3)
            {
                stack.Push(new Triangle
                {
                    A = vertices[indices[t]],
                    B = vertices[indices[t + 1]],
                    C = vertices[indices[t + 2]],
                    Material = sub
                });

                while (stack.Count > 0)
                {
                    Triangle tri = stack.Pop();

                    float ab = (tri.A.Position - tri.B.Position).magnitude;
                    float bc = (tri.B.Position - tri.C.Position).magnitude;
                    float ca = (tri.C.Position - tri.A.Position).magnitude;
                    float longest = Mathf.Max(ab, Mathf.Max(bc, ca));

                    if (longest <= maxEdge || result.Count > MaxTrianglesPerMesh)
                    {
                        result.Add(tri);
                        continue;
                    }

                    // Делим по самому длинному ребру
                    if (longest == ab)
                    {
                        Vertex m = Mid(tri.A, tri.B);
                        stack.Push(new Triangle { A = tri.A, B = m, C = tri.C, Material = tri.Material });
                        stack.Push(new Triangle { A = m, B = tri.B, C = tri.C, Material = tri.Material });
                    }
                    else if (longest == bc)
                    {
                        Vertex m = Mid(tri.B, tri.C);
                        stack.Push(new Triangle { A = tri.A, B = tri.B, C = m, Material = tri.Material });
                        stack.Push(new Triangle { A = tri.A, B = m, C = tri.C, Material = tri.Material });
                    }
                    else
                    {
                        Vertex m = Mid(tri.C, tri.A);
                        stack.Push(new Triangle { A = tri.A, B = tri.B, C = m, Material = tri.Material });
                        stack.Push(new Triangle { A = m, B = tri.B, C = tri.C, Material = tri.Material });
                    }
                }
            }
        }

        return result;
    }

    private static Vertex Mid(Vertex a, Vertex b)
    {
        return new Vertex
        {
            Position = (a.Position + b.Position) * 0.5f,
            Normal = (a.Normal + b.Normal).normalized,
            Uv = (a.Uv + b.Uv) * 0.5f
        };
    }

    // Меш одного куска: вершины относительно центра куска, подмеши по материалам
    private static Mesh BuildChunk(List<Triangle> triangles, Material[] sourceMaterials, out Vector3 center, out Material[] materials)
    {
        center = Vector3.zero;
        materials = null;

        if (triangles.Count == 0)
        {
            return null;
        }

        Bounds bounds = new Bounds(triangles[0].A.Position, Vector3.zero);

        foreach (Triangle t in triangles)
        {
            bounds.Encapsulate(t.A.Position);
            bounds.Encapsulate(t.B.Position);
            bounds.Encapsulate(t.C.Position);
        }

        center = bounds.center;

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var perMaterial = new SortedDictionary<int, List<int>>();
        bool hasNormals = true;

        foreach (Triangle t in triangles)
        {
            if (!perMaterial.TryGetValue(t.Material, out List<int> indices))
            {
                indices = new List<int>();
                perMaterial.Add(t.Material, indices);
            }

            foreach (Vertex v in new[] { t.A, t.B, t.C })
            {
                indices.Add(vertices.Count);
                vertices.Add(v.Position - center);
                normals.Add(v.Normal);
                uvs.Add(v.Uv);

                if (v.Normal == Vector3.zero)
                {
                    hasNormals = false;
                }
            }
        }

        var mesh = new Mesh { name = "Chunk" };

        if (vertices.Count > 65000)
        {
            mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.subMeshCount = perMaterial.Count;

        var materialList = new List<Material>();
        int submesh = 0;

        foreach (var pair in perMaterial)
        {
            mesh.SetTriangles(pair.Value, submesh++);
            materialList.Add(pair.Key < sourceMaterials.Length ? sourceMaterials[pair.Key] : null);
        }

        if (hasNormals)
        {
            mesh.SetNormals(normals);
        }
        else
        {
            mesh.RecalculateNormals();
        }

        mesh.RecalculateBounds();
        materials = materialList.ToArray();
        return mesh;
    }

    private static void CreateChunkObject(Transform parent, Mesh mesh, Material[] materials, Vector3 center, int index)
    {
        var go = new GameObject("Chunk_" + index);
        go.transform.SetParent(parent, false);

        // Мировая позиция, без поворота и с единичным мировым масштабом
        go.transform.position = center;
        go.transform.rotation = Quaternion.identity;
        Vector3 lossy = parent.lossyScale;
        go.transform.localScale = new Vector3(
            1f / Mathf.Max(lossy.x, 0.0001f),
            1f / Mathf.Max(lossy.y, 0.0001f),
            1f / Mathf.Max(lossy.z, 0.0001f));

        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterials = materials;

        var box = go.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = Vector3.Max(mesh.bounds.size, Vector3.one * 0.08f);

        var body = go.AddComponent<Rigidbody>();
        float volume = box.size.x * box.size.y * box.size.z;
        body.mass = Mathf.Clamp(volume * 300f, 5f, 2500f);
        body.isKinematic = true;

        go.AddComponent<DemolitionChunk>();
    }
}
