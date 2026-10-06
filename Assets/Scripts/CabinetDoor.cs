using UnityEngine;

// Вешается на каждую существующую дверцу (левую и правую).
// По E (см. PlayerController) дверца поворачивается вокруг вертикальной петли в сторону игрока.
// Все расчёты идут в локальных координатах родителя, поэтому масштаб и зеркальность шкафа не мешают.
public class CabinetDoor : MonoBehaviour, IInteractable
{
    public enum HingeMode
    {
        OuterEdge, // край, дальше всего от центра родительского объекта (подходит для пары дверей)
        MinEdge,   // край с меньшей координатой по оси ширины (локальный X или Z родителя)
        MaxEdge    // край с большей координатой по оси ширины
    }

    [Header("Петля")]
    [SerializeField] private HingeMode _hingeMode = HingeMode.OuterEdge;
    [SerializeField] private Transform _hinge;            // необязательно: свой объект-петля вместо автоподбора

    [Header("Открывание")]
    [SerializeField] private float _openAngle = 100f;     // угол открытия, градусов
    [SerializeField] private float _speed = 180f;         // скорость, градусов в секунду
    [SerializeField] private bool _isOpen;
    [SerializeField] private bool _openSiblings = true;   // по E открываются все двери из того же родителя

    [Header("Необязательно")]
    [SerializeField] private AudioSource _sound;          // звук открывания/закрывания

    private Vector3 _closedLocalPosition;
    private Quaternion _closedLocalRotation;
    private Vector3 _hingeLocal;        // петля в локальных координатах родителя
    private Vector3 _freeEdgeDirLocal;  // горизонтальное направление от петли к центру дверцы (в локальных координатах родителя)
    private float _angle;
    private float _targetAngle;

    private void Awake()
    {
        _closedLocalPosition = transform.localPosition;
        _closedLocalRotation = transform.localRotation;

        Renderer[] doorRenderers = GetComponentsInChildren<Renderer>();
        Bounds door = doorRenderers.Length > 0
            ? LocalBounds(doorRenderers)
            : new Bounds(_closedLocalPosition, Vector3.one);

        EnsureCollider();
        CalculateHinge(door);
    }

    private void CalculateHinge(Bounds door)
    {
        if (_hinge != null)
        {
            _hingeLocal = ToParentLocal(_hinge.position);
        }
        else
        {
            bool widthAlongX = door.size.x >= door.size.z;

            float doorCenter = widthAlongX ? door.center.x : door.center.z;
            float min = widthAlongX ? door.min.x : door.min.z;
            float max = widthAlongX ? door.max.x : door.max.z;

            bool useMax;
            switch (_hingeMode)
            {
                case HingeMode.MinEdge: useMax = false; break;
                case HingeMode.MaxEdge: useMax = true; break;
                default: useMax = doorCenter >= GetGroupCenter(widthAlongX); break;
            }

            float edge = useMax ? max : min;
            _hingeLocal = door.center;
            if (widthAlongX) _hingeLocal.x = edge; else _hingeLocal.z = edge;
        }

        _freeEdgeDirLocal = door.center - _hingeLocal;
        _freeEdgeDirLocal.y = 0f;
        if (_freeEdgeDirLocal.sqrMagnitude < 0.0001f) _freeEdgeDirLocal = Vector3.right;
        _freeEdgeDirLocal.Normalize();
    }

    // Центр родителя (вместе со всеми дверцами внутри него) по оси ширины
    private float GetGroupCenter(bool alongX)
    {
        Transform group = transform.parent != null ? transform.parent : transform;
        Renderer[] all = group.GetComponentsInChildren<Renderer>();
        if (all.Length == 0) return 0f;

        Bounds b = LocalBounds(all);
        return alongX ? b.center.x : b.center.z;
    }

    // Нужен коллайдер, чтобы луч игрока попадал в дверцу
    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null) return;

        MeshFilter meshFilter = GetComponentInChildren<MeshFilter>();
        if (meshFilter == null || meshFilter.sharedMesh == null) return;

        MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
    }

    // Вызывается игроком по нажатию E
    public void Interact()
    {
        bool open = !_isOpen;

        if (!_openSiblings || transform.parent == null)
        {
            SetOpen(open);
            return;
        }

        // Открываем/закрываем все двери, лежащие в том же родительском объекте
        foreach (CabinetDoor door in transform.parent.GetComponentsInChildren<CabinetDoor>())
        {
            if (door.transform.parent == transform.parent) door.SetOpen(open);
        }
    }

    public void SetOpen(bool open)
    {
        _isOpen = open;

        if (_isOpen)
        {
            // Положительный поворот двигает край дверцы в сторону Cross(up, edge)
            Vector3 normal = Vector3.Cross(Vector3.up, _freeEdgeDirLocal);
            Vector3 toPlayer = ToParentLocal(GetPlayerPosition()) - _hingeLocal;
            float side = Vector3.Dot(normal, toPlayer) >= 0f ? 1f : -1f;
            _targetAngle = _openAngle * side;
        }
        else
        {
            _targetAngle = 0f;
        }

        if (_sound != null) _sound.Play();
    }

    private void Update()
    {
        if (Mathf.Approximately(_angle, _targetAngle)) return;

        _angle = Mathf.MoveTowards(_angle, _targetAngle, _speed * Time.deltaTime);

        Quaternion rot = Quaternion.AngleAxis(_angle, Vector3.up);
        transform.localPosition = _hingeLocal + rot * (_closedLocalPosition - _hingeLocal);
        transform.localRotation = rot * _closedLocalRotation;
    }

    // ---------- Вспомогательное ----------

    private Vector3 ToParentLocal(Vector3 worldPoint)
    {
        return transform.parent != null ? transform.parent.InverseTransformPoint(worldPoint) : worldPoint;
    }

    // Габариты рендереров в локальных координатах родителя (по 8 углам каждого рендерера)
    private Bounds LocalBounds(Renderer[] renderers)
    {
        Matrix4x4 worldToParent = transform.parent != null
            ? transform.parent.worldToLocalMatrix
            : Matrix4x4.identity;

        bool first = true;
        Bounds result = new Bounds();

        foreach (Renderer r in renderers)
        {
            Bounds lb = r.localBounds;
            Matrix4x4 toParent = worldToParent * r.localToWorldMatrix;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = lb.center + Vector3.Scale(lb.extents, new Vector3(
                    (i & 1) == 0 ? -1f : 1f,
                    (i & 2) == 0 ? -1f : 1f,
                    (i & 4) == 0 ? -1f : 1f));

                Vector3 p = toParent.MultiplyPoint3x4(corner);

                if (first) { result = new Bounds(p, Vector3.zero); first = false; }
                else result.Encapsulate(p);
            }
        }

        return result;
    }

    private static Vector3 GetPlayerPosition()
    {
        Camera cam = Camera.main;
        return cam != null ? cam.transform.position : Vector3.zero;
    }
}
