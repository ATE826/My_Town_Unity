using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Rendering;

public class BrickSlot
{
    public Vector3 Position;
    public Quaternion Rotation;
    public Vector3 Size;
    public Vector3 StandPoint;
    public int Layer;
    public bool Reserved;
    public bool Filled;
}

// Бригада: после обрушения старого дома выпускает рабочих, которые по кирпичу строят новый дом
// на том же месте и того же размера (дверь спереди, окна по бокам и сзади).
public class ConstructionCrew : MonoBehaviour
{
    public enum FrontSide { TowardSpawnPoint, PositiveZ, NegativeZ, PositiveX, NegativeX }

    [Header("Ссылки")]
    [SerializeField] private HouseDemolition _house;
    [SerializeField] private GameObject _workerPrefab;             // запасной вариант, если список пуст
    [SerializeField] private GameObject[] _workerPrefabs;          // разные персонажи: рабочие берут их по кругу
    [SerializeField] private RuntimeAnimatorController _workerController;
    [SerializeField] private Transform _blockSource;           // Concrete_Block_Set, отсюда берут кирпичи

    [Header("Рабочие")]
    [SerializeField] private Vector3 _spawnPosition = new Vector3(-41.75f, 0.1999988f, 116.67f);
    [SerializeField] private int _workerCount = 5;
    [SerializeField] private float _firstSpawnDelay = 2f;
    [SerializeField] private float _spawnInterval = 5f;        // пауза между появлением рабочих
    [SerializeField] private float _workerSpeed = 1.8f;
    [SerializeField] private float _workerScale = 1.3f;             // размер рабочих по всем осям

    [Header("Новый дом")]
    [SerializeField] private FrontSide _front = FrontSide.TowardSpawnPoint;   // где дверь
    [SerializeField] private float _targetBrickHeight = 0.5f;
    [SerializeField] private float _doorWidth = 1.6f;
    [SerializeField] private float _windowWidth = 1.6f;

    [Header("Проёмы по рядам (ряды считаются снизу, с 1)")]
    [SerializeField] private int _doorTopRow = 8;          // дверь открыта в рядах 1–8, верх проёма (перемычка) — 9-й ряд
    [SerializeField] private int _windowSillRow = 4;       // 4-й ряд — низ оконного проёма (подоконник), окно открыто выше него
    [SerializeField] private int _windowTopRow = 8;        // окно открыто до 8-го ряда включительно, 9-й ряд — верх проёма
    [SerializeField] private Color _brickColor = new Color(0.72f, 0.16f, 0.1f);
    [SerializeField] private bool _bricksBlockVehicles = false;   // false: кирпичи не толкают технику (она проезжает сквозь стены)

    private readonly List<BrickSlot> _slots = new List<BrickSlot>();
    private int _nextSlot;
    private Material _brickMaterial;
    private Transform _bricksRoot;

    private const float PickupDistance = 1f;       // кирпич берут, подойдя к куче на метр

    private Vector3 _blockCenter;
    private Bounds _blockBounds;
    private bool _hasBlockBounds;
    private bool _started;

    public enum ReserveResult { Reserved, Wait, Done }

    public IReadOnlyList<Rigidbody> Vehicles => _vehicles;

    private readonly List<Rigidbody> _vehicles = new List<Rigidbody>();
    private int[] _layerTotal;
    private int[] _layerFilled;
    private int _currentLayer;

    // Реагируем только на технику, в которой сидит водитель (игрок)
    public bool IsDriven(Rigidbody vehicle)
    {
        if (vehicle.TryGetComponent(out BulldozerController bulldozer))
        {
            return bulldozer.IsDriven;
        }

        if (vehicle.TryGetComponent(out ExcavatorController excavator))
        {
            return excavator.IsDriven;
        }

        return false;
    }

    public Transform BlockSource => _blockSource;
    public Vector3 BlockCenter => _blockCenter;
    public Vector3 SpawnPosition => _spawnPosition;

    // =========================================================
    // ЗАПУСК
    // =========================================================

    private void Start()
    {
        if (_house == null)
        {
            _house = FindFirstObjectByType<HouseDemolition>();
        }

        if (_blockSource == null)
        {
            GameObject found = GameObject.Find("Concrete_Block_Set");
            _blockSource = found != null ? found.transform : null;
        }

        if (_house == null || PrefabFor(0) == null)
        {
            Debug.LogWarning("ConstructionCrew: не назначен дом или префаб рабочего.", this);
            return;
        }

        if (_blockSource != null && TryGetBounds(_blockSource, out Bounds blockBounds))
        {
            _blockCenter = blockBounds.center;
            _blockBounds = blockBounds;
            _hasBlockBounds = true;
        }
        else
        {
            Debug.LogWarning("ConstructionCrew: не найден объект Concrete_Block_Set.", this);
            _blockCenter = _spawnPosition;
        }

        AddVehicleObstacles();
        BuildNavMesh();

        _house.Demolished += OnHouseDemolished;
    }

    private void OnDestroy()
    {
        if (_house != null)
        {
            _house.Demolished -= OnHouseDemolished;
        }
    }

    private void OnHouseDemolished(Bounds houseBounds)
    {
        if (_started)
        {
            return;
        }

        _started = true;
        BuildPlan(houseBounds);
        StartCoroutine(SpawnWorkers());
    }

    private IEnumerator SpawnWorkers()
    {
        yield return new WaitForSeconds(_firstSpawnDelay);

        for (int i = 0; i < _workerCount; i++)
        {
            SpawnWorker(i);
            yield return new WaitForSeconds(_spawnInterval);
        }
    }

    private void SpawnWorker(int index)
    {
        Vector3 position = _spawnPosition;

        if (NavMesh.SamplePosition(position, out NavMeshHit hit, 6f, NavMesh.AllAreas))
        {
            position = hit.position;
        }

        GameObject worker = Instantiate(PrefabFor(index), position, Quaternion.identity);
        worker.name = "Worker_" + (index + 1);
        worker.transform.localScale *= _workerScale;

        Animator animator = worker.GetComponentInChildren<Animator>(true);

        if (animator != null && _workerController != null)
        {
            animator.runtimeAnimatorController = _workerController;
            animator.applyRootMotion = false;
        }

        // У рабочего нет физического тела: он никогда не толкает технику
        foreach (Collider collider in worker.GetComponentsInChildren<Collider>(true))
        {
            Destroy(collider);
        }

        foreach (Rigidbody body in worker.GetComponentsInChildren<Rigidbody>(true))
        {
            Destroy(body);
        }

        var agent = worker.GetComponent<NavMeshAgent>();

        if (agent == null)
        {
            agent = worker.AddComponent<NavMeshAgent>();
        }

        agent.Warp(position);

        var component = worker.AddComponent<ConstructionWorker>();
        component.Init(this, animator, _workerSpeed * _workerScale, index);
    }

    // Каждому следующему рабочему — следующий персонаж из списка
    private GameObject PrefabFor(int index)
    {
        if (_workerPrefabs != null && _workerPrefabs.Length > 0)
        {
            GameObject prefab = _workerPrefabs[index % _workerPrefabs.Length];

            if (prefab != null)
            {
                return prefab;
            }
        }

        return _workerPrefab;
    }

    // =========================================================
    // NAVMESH И ПРЕПЯТСТВИЯ
    // =========================================================

    // Техника — подвижные препятствия: рабочие обходят её, пока она стоит
    private void AddVehicleObstacles()
    {
        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            bool vehicle = body.GetComponent<BulldozerController>() != null || body.GetComponent<ExcavatorController>() != null;

            if (!vehicle)
            {
                continue;
            }

            if (body.GetComponent<NavMeshObstacle>() != null)
            {
                if (!_vehicles.Contains(body))
                {
                    _vehicles.Add(body);
                }

                continue;
            }

            var box = body.GetComponent<BoxCollider>();

            if (box == null)
            {
                continue;
            }

            if (!_vehicles.Contains(body))
            {
                _vehicles.Add(body);
            }

            var obstacle = body.gameObject.AddComponent<NavMeshObstacle>();
            obstacle.shape = NavMeshObstacleShape.Box;
            obstacle.center = box.center;
            obstacle.size = box.size;
            obstacle.carving = true;
            obstacle.carveOnlyStationary = true;
            obstacle.carvingMoveThreshold = 0.3f;
            obstacle.carvingTimeToStationary = 0.5f;
        }
    }

    private void BuildNavMesh()
    {
        Physics.SyncTransforms();

        // Область запекания: от старого дома до кирпичей и техники + запас
        var area = new Bounds(_spawnPosition, Vector3.zero);
        area.Encapsulate(_house.OriginalBounds);
        area.Encapsulate(_blockCenter);

        var ignore = new List<NavMeshBuildMarkup>();
        AddIgnore(ignore, _house.transform);

        foreach (Rigidbody body in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (body.GetComponent<BulldozerController>() != null || body.GetComponent<ExcavatorController>() != null)
            {
                AddIgnore(ignore, body.transform);

                if (body.TryGetComponent(out BoxCollider box))
                {
                    area.Encapsulate(box.bounds);
                }
            }
        }

        area.Expand(new Vector3(40f, 0f, 40f));
        area.min = new Vector3(area.min.x, area.min.y - 6f, area.min.z);
        area.max = new Vector3(area.max.x, area.max.y + 10f, area.max.z);

        NavMeshBuildSettings settings = NavMesh.GetSettingsByID(0);
        settings.agentRadius = 0.35f;
        settings.agentHeight = 1.8f;
        settings.agentClimb = 0.4f;
        settings.agentSlope = 40f;

        // Сначала по физическим коллайдерам (Terrain, земля), если не вышло — по мешам
        NavMeshCollectGeometry[] attempts = { NavMeshCollectGeometry.PhysicsColliders, NavMeshCollectGeometry.RenderMeshes };

        foreach (NavMeshCollectGeometry geometry in attempts)
        {
            var sources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(area, ~0, geometry, 0, ignore, sources);

            NavMeshData data = NavMeshBuilder.BuildNavMeshData(settings, sources, area, Vector3.zero, Quaternion.identity);
            NavMeshDataInstance instance = NavMesh.AddNavMeshData(data);

            if (NavMesh.SamplePosition(_spawnPosition, out _, 6f, NavMesh.AllAreas))
            {
                return;
            }

            instance.Remove();
        }

        Debug.LogWarning("ConstructionCrew: не удалось построить NavMesh у точки спавна " + _spawnPosition + ". Под рабочими нет земли с коллайдером?", this);
    }

    private static void AddIgnore(List<NavMeshBuildMarkup> markups, Transform root)
    {
        markups.Add(new NavMeshBuildMarkup
        {
            root = root,
            ignoreFromBuild = true,
            applyToChildren = true
        });
    }

    // =========================================================
    // ПЛАН НОВОГО ДОМА
    // =========================================================

    private struct Wall
    {
        public Vector3 Center, Along, Out;
        public float Length, OpenWidth;
        public int OpenFromLayer, OpenToLayer;      // ряды (с 0), в которых проём открыт
    }

    private void BuildPlan(Bounds bounds)
    {
        _slots.Clear();
        _nextSlot = 0;

        Vector3 center = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);

        Vector3 frontDirection = FrontDirection(center);
        Quaternion frame = Quaternion.LookRotation(frontDirection, Vector3.up);
        bool alongX = Mathf.Abs(frontDirection.z) > 0.5f;

        float width = alongX ? bounds.size.x : bounds.size.z;     // вдоль локальной оси X
        float depth = alongX ? bounds.size.z : bounds.size.x;
        float height = Mathf.Max(bounds.size.y, 2.5f);

        // Рядов должно хватать, чтобы над проёмами был хотя бы один ряд (перемычка)
        int minLayers = Mathf.Max(_doorTopRow, _windowTopRow) + 2;
        int layers = Mathf.Clamp(Mathf.RoundToInt(height / _targetBrickHeight), minLayers, 24);
        float brickHeight = height / layers;
        float thickness = Mathf.Min(brickHeight, Mathf.Min(width, depth) * 0.15f);

        float doorWidth = Mathf.Min(_doorWidth, width * 0.35f);

        float sideLength = depth - 2f * thickness;

        // Ряды с 0: дверь — 1…_doorTopRow, окно — выше подоконника до _windowTopRow
        int doorFrom = 0;
        int doorTo = _doorTopRow - 1;
        int windowFrom = _windowSillRow;
        int windowTo = _windowTopRow - 1;

        Wall[] walls =
        {
            new Wall { Center = new Vector3(0, 0, depth / 2f - thickness / 2f), Along = Vector3.right, Out = Vector3.forward,
                       Length = width, OpenWidth = doorWidth, OpenFromLayer = doorFrom, OpenToLayer = doorTo },
            new Wall { Center = new Vector3(width / 2f - thickness / 2f, 0, 0), Along = Vector3.forward, Out = Vector3.right,
                       Length = sideLength, OpenWidth = Mathf.Min(_windowWidth, sideLength * 0.35f), OpenFromLayer = windowFrom, OpenToLayer = windowTo },
            new Wall { Center = new Vector3(0, 0, -(depth / 2f - thickness / 2f)), Along = Vector3.right, Out = Vector3.back,
                       Length = width, OpenWidth = Mathf.Min(_windowWidth, width * 0.3f), OpenFromLayer = windowFrom, OpenToLayer = windowTo },
            new Wall { Center = new Vector3(-(width / 2f - thickness / 2f), 0, 0), Along = Vector3.forward, Out = Vector3.left,
                       Length = sideLength, OpenWidth = Mathf.Min(_windowWidth, sideLength * 0.35f), OpenFromLayer = windowFrom, OpenToLayer = windowTo }
        };

        for (int layer = 0; layer < layers; layer++)
        {
            float y = layer * brickHeight + brickHeight / 2f;

            foreach (Wall wall in walls)
            {
                AddWallLayer(wall, layer, y, brickHeight, thickness, center, frame);
            }
        }

        _layerTotal = new int[layers];
        _layerFilled = new int[layers];
        _currentLayer = 0;

        foreach (BrickSlot slot in _slots)
        {
            _layerTotal[slot.Layer]++;
        }

        // Новый дом — препятствие для NavMesh: рабочие обходят его снаружи
        var obstacleObject = new GameObject("NewHouseObstacle");
        obstacleObject.transform.SetParent(transform, false);
        obstacleObject.transform.SetPositionAndRotation(center + Vector3.up * (height / 2f), frame);

        var obstacle = obstacleObject.AddComponent<NavMeshObstacle>();
        obstacle.shape = NavMeshObstacleShape.Box;
        obstacle.size = new Vector3(width + 0.3f, height, depth + 0.3f);
        obstacle.carving = true;
        obstacle.carveOnlyStationary = false;

        _bricksRoot = new GameObject("NewHouseBricks").transform;
        _bricksRoot.SetParent(transform, false);

        Debug.Log("ConstructionCrew: план дома — " + _slots.Count + " кирпичей, " + layers + " рядов, " + width.ToString("F1") + " x " + depth.ToString("F1") + " x " + height.ToString("F1") + " м.");
    }

    private void AddWallLayer(Wall wall, int layer, float y, float brickHeight, float thickness, Vector3 origin, Quaternion frame)
    {
        int count = Mathf.Max(2, Mathf.RoundToInt(wall.Length / (brickHeight * 2f)));
        float step = wall.Length / count;

        // Чередование рядов: со сдвигом на полкирпича (на концах — половинки)
        var pieces = new List<KeyValuePair<float, float>>();   // центр вдоль стены, длина кирпича

        if (layer % 2 == 0)
        {
            for (int i = 0; i < count; i++)
            {
                pieces.Add(new KeyValuePair<float, float>((i + 0.5f) * step, step));
            }
        }
        else
        {
            pieces.Add(new KeyValuePair<float, float>(step / 4f, step / 2f));

            for (int i = 0; i < count - 1; i++)
            {
                pieces.Add(new KeyValuePair<float, float>(step + i * step, step));
            }

            pieces.Add(new KeyValuePair<float, float>(wall.Length - step / 4f, step / 2f));
        }

        Quaternion brickRotation = frame * Quaternion.LookRotation(wall.Out, Vector3.up);

        // Проём одной и той же ширины во всех рядах: границы фиксированы, а кирпичи,
        // которых касается проём, подрезаются по его краю (как при реальной кладке)
        bool hasOpening = wall.OpenWidth > 0f && layer >= wall.OpenFromLayer && layer <= wall.OpenToLayer;
        float openStart = wall.Length / 2f - wall.OpenWidth / 2f;
        float openEnd = wall.Length / 2f + wall.OpenWidth / 2f;
        const float minPiece = 0.08f;

        foreach (var piece in pieces)
        {
            float from = piece.Key - piece.Value / 2f;
            float to = piece.Key + piece.Value / 2f;

            if (!hasOpening)
            {
                AddBrick(wall, layer, y, from, to, brickHeight, thickness, origin, frame, brickRotation);
                continue;
            }

            // Часть кирпича до проёма и часть после него
            if (Mathf.Min(to, openStart) - from > minPiece)
            {
                AddBrick(wall, layer, y, from, Mathf.Min(to, openStart), brickHeight, thickness, origin, frame, brickRotation);
            }

            if (to - Mathf.Max(from, openEnd) > minPiece)
            {
                AddBrick(wall, layer, y, Mathf.Max(from, openEnd), to, brickHeight, thickness, origin, frame, brickRotation);
            }
        }
    }

    private void AddBrick(Wall wall, int layer, float y, float from, float to, float brickHeight, float thickness,
                          Vector3 origin, Quaternion frame, Quaternion brickRotation)
    {
        float along = (from + to) / 2f - wall.Length / 2f;

        Vector3 local = wall.Center + wall.Along * along;
        Vector3 position = origin + frame * local + Vector3.up * y;
        Vector3 outward = frame * wall.Out;

        _slots.Add(new BrickSlot
        {
            Position = position,
            Rotation = brickRotation,
            Size = new Vector3(to - from, brickHeight, thickness),
            StandPoint = new Vector3(position.x, origin.y, position.z) + outward * (thickness / 2f + 0.9f),
            Layer = layer
        });
    }

    private Vector3 FrontDirection(Vector3 center)
    {
        switch (_front)
        {
            case FrontSide.PositiveZ: return Vector3.forward;
            case FrontSide.NegativeZ: return Vector3.back;
            case FrontSide.PositiveX: return Vector3.right;
            case FrontSide.NegativeX: return Vector3.left;
        }

        Vector3 toSpawn = _spawnPosition - center;
        toSpawn.y = 0f;

        if (Mathf.Abs(toSpawn.x) > Mathf.Abs(toSpawn.z))
        {
            return toSpawn.x > 0f ? Vector3.right : Vector3.left;
        }

        return toSpawn.z > 0f ? Vector3.forward : Vector3.back;
    }

    // =========================================================
    // СЕРВИС ДЛЯ РАБОЧИХ
    // =========================================================

    // Ряд нельзя начинать, пока не выложен предыдущий: тогда рабочий ждёт у кирпичей
    public ReserveResult TryReserveSlot(out BrickSlot slot)
    {
        slot = null;

        if (_nextSlot >= _slots.Count)
        {
            return ReserveResult.Done;
        }

        BrickSlot candidate = _slots[_nextSlot];

        if (candidate.Layer > _currentLayer)
        {
            return ReserveResult.Wait;
        }

        _nextSlot++;
        candidate.Reserved = true;
        slot = candidate;
        return ReserveResult.Reserved;
    }

    // Коллайдер для уложенного кирпича; технику он не толкает (если не включено _bricksBlockVehicles)
    public void AddBrickCollider(GameObject brick)
    {
        var brickCollider = brick.AddComponent<BoxCollider>();

        if (_bricksBlockVehicles)
        {
            return;
        }

        foreach (Rigidbody vehicle in _vehicles)
        {
            if (vehicle == null)
            {
                continue;
            }

            foreach (Collider vehicleCollider in vehicle.GetComponentsInChildren<Collider>())
            {
                Physics.IgnoreCollision(brickCollider, vehicleCollider, true);
            }
        }
    }

    public void CompleteSlot(BrickSlot slot, Transform brick)
    {
        slot.Filled = true;
        _layerFilled[slot.Layer]++;

        while (_currentLayer < _layerTotal.Length && _layerFilled[_currentLayer] >= _layerTotal[_currentLayer])
        {
            _currentLayer++;
        }

        if (brick != null && _bricksRoot != null)
        {
            brick.SetParent(_bricksRoot, true);
        }
    }

    // Точка ровно в метре от ближайшей стороны кучи кирпичей (со стороны, откуда идёт рабочий)
    public Vector3 GetBlockApproachPoint(Vector3 from)
    {
        if (!_hasBlockBounds)
        {
            return _blockCenter;
        }

        // Небольшой разброс вдоль кучи, чтобы рабочие не сходились в одну точку
        Vector3 toward = from - _blockCenter;
        toward.y = 0f;
        toward = toward.sqrMagnitude > 0.01f ? toward.normalized : Vector3.forward;
        Vector3 tangent = Vector3.Cross(Vector3.up, toward);

        Vector3 query = from + tangent * Random.Range(-1.5f, 1.5f);
        query.y = _blockBounds.center.y;

        Vector3 closest = _blockBounds.ClosestPoint(query);
        Vector3 direction = query - closest;
        direction.y = 0f;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : toward;

        Vector3 point = closest + direction * PickupDistance;
        point.y = from.y;

        if (NavMesh.SamplePosition(point, out NavMeshHit hit, 1.5f, NavMesh.AllAreas))
        {
            return hit.position;
        }

        return point;
    }

    // Расстояние по земле от точки до ближайшей стороны кучи кирпичей
    public float DistanceToBlocks(Vector3 position)
    {
        if (!_hasBlockBounds)
        {
            return 0f;
        }

        Vector3 flat = new Vector3(position.x, _blockBounds.center.y, position.z);
        Vector3 closest = _blockBounds.ClosestPoint(flat);

        return Vector3.Distance(flat, closest);
    }

    public GameObject CreateBrick(Vector3 size)
    {
        if (_brickMaterial == null)
        {
            Material template = GraphicsSettings.currentRenderPipeline != null
                ? GraphicsSettings.currentRenderPipeline.defaultMaterial
                : null;

            _brickMaterial = template != null ? new Material(template) : new Material(Shader.Find("Standard"));

            if (_brickMaterial.HasProperty("_BaseColor")) _brickMaterial.SetColor("_BaseColor", _brickColor);
            if (_brickMaterial.HasProperty("_Color")) _brickMaterial.SetColor("_Color", _brickColor);
        }

        GameObject brick = GameObject.CreatePrimitive(PrimitiveType.Cube);
        brick.name = "Brick";
        DestroyImmediate(brick.GetComponent<Collider>());
        brick.GetComponent<Renderer>().sharedMaterial = _brickMaterial;
        brick.transform.localScale = size;

        return brick;
    }

    private static bool TryGetBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>();
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
}
