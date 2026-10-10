using UnityEngine;

// Вешается на объект blinds (жалюзи).
// По E (см. PlayerController) жалюзи плавно поднимаются вверх, повторное нажатие опускает их обратно.
public class Blinds : MonoBehaviour, IInteractable
{
    public enum Mode
    {
        Slide,   // жалюзи целиком едут вверх
        Collapse // жалюзи «складываются» к верхнему краю (сжимаются по вертикали)
    }

    [Header("Движение")]
    [SerializeField] private Mode _mode = Mode.Slide;
    [SerializeField] private float _liftDistance = 0f;      // Slide: на сколько метров поднять; 0 = высота жалюзи
    [SerializeField, Range(0.02f, 1f)] private float _collapsedScale = 0.1f; // Collapse: во сколько раз сжать по высоте
    [SerializeField] private float _speed = 1f;             // скорость, метров в секунду (Slide) или долей высоты (Collapse)
    [SerializeField] private bool _isOpen;

    [Header("Необязательно")]
    [SerializeField] private AudioSource _sound;            // звук подъёма/опускания

    private Vector3 _closedPosition;
    private Vector3 _closedScale;
    private float _height;      // высота жалюзи в мировых координатах
    private float _closedTop;   // мировая высота верхнего края в закрытом состоянии
    private int _verticalAxis;  // локальная ось (0=X, 1=Y, 2=Z), ближе всего направленная вверх в мире
    private Renderer[] _renderers;
    private float _progress;    // 0 = опущены, 1 = подняты
    private float _target;

    private void Awake()
    {
        _closedPosition = transform.position;
        _closedScale = transform.localScale;

        _renderers = GetComponentsInChildren<Renderer>();
        if (_renderers.Length > 0)
        {
            Bounds b = WorldBounds();
            _height = b.size.y;
            _closedTop = b.max.y;
        }
        else
        {
            _height = 1f;
            _closedTop = _closedPosition.y;
        }

        _verticalAxis = FindVerticalAxis();

        EnsureCollider();

        _target = _isOpen ? 1f : 0f;
        _progress = _target;
        Apply();
    }

    // Нужен коллайдер, чтобы луч игрока попадал в жалюзи
    private void EnsureCollider()
    {
        if (GetComponentInChildren<Collider>() != null) return;

        foreach (MeshFilter meshFilter in GetComponentsInChildren<MeshFilter>())
        {
            if (meshFilter.sharedMesh == null) continue;
            MeshCollider meshCollider = meshFilter.gameObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;
        }
    }

    // Вызывается игроком по нажатию E
    public void Interact()
    {
        _isOpen = !_isOpen;
        _target = _isOpen ? 1f : 0f;

        if (_sound != null) _sound.Play();
    }

    private void Update()
    {
        if (Mathf.Approximately(_progress, _target)) return;

        float travel = _mode == Mode.Slide ? (_liftDistance > 0f ? _liftDistance : _height) : 1f;
        float step = _speed / Mathf.Max(travel, 0.01f);

        _progress = Mathf.MoveTowards(_progress, _target, step * Time.deltaTime);
        Apply();
    }

    private void Apply()
    {
        // Плавное начало и конец движения
        float t = Mathf.SmoothStep(0f, 1f, _progress);

        if (_mode == Mode.Slide)
        {
            float distance = _liftDistance > 0f ? _liftDistance : _height;
            transform.position = _closedPosition + Vector3.up * (distance * t);
        }
        else
        {
            // Сжимаем по оси, которая в мире смотрит вверх, и возвращаем верхний край на прежнюю высоту
            float factor = Mathf.Lerp(1f, _collapsedScale, t);
            Vector3 scale = _closedScale;
            scale[_verticalAxis] *= factor;
            transform.localScale = scale;

            transform.position = _closedPosition;
            if (_renderers.Length > 0)
            {
                float shift = _closedTop - WorldBounds().max.y;
                transform.position = _closedPosition + Vector3.up * shift;
            }
        }
    }

    // Локальная ось, чьё мировое направление ближе всего к вертикали
    private int FindVerticalAxis()
    {
        int best = 1;
        float bestDot = -1f;
        Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };

        for (int i = 0; i < 3; i++)
        {
            float dot = Mathf.Abs(Vector3.Dot(transform.TransformDirection(axes[i]).normalized, Vector3.up));
            if (dot > bestDot) { bestDot = dot; best = i; }
        }

        return best;
    }

    private Bounds WorldBounds()
    {
        Bounds b = _renderers[0].bounds;
        foreach (Renderer r in _renderers) b.Encapsulate(r.bounds);
        return b;
    }
}
