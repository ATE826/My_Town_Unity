using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class BulldozerController : MonoBehaviour
{
    [Header("Кабина")]
    [SerializeField] private Camera _cabCamera;
    [SerializeField] private float _lookSensitivity = 0.12f;
    [SerializeField] private float _maxYaw = 110f;
    [SerializeField] private float _minPitch = -60f;
    [SerializeField] private float _maxPitch = 60f;

    [Header("Посадка (F)")]
    [SerializeField] private float _enterDistance = 6f;
    [SerializeField] private Vector3 _exitLocalOffset = new Vector3(-4f, 0f, 0f);

    [Header("Движение (WASD)")]
    [SerializeField] private float _moveSpeed = 3.5f;
    [SerializeField] private float _reverseSpeed = 2.5f;
    [SerializeField] private float _turnSpeed = 40f;

    [Header("Гусеницы")]
    [SerializeField] private Renderer[] _tracks;       // меши гусениц (перетащите или ПКМ на скрипте → «Найти гусеницы»)
    [SerializeField] private float[] _trackSides;      // -1 левая, +1 правая
    [SerializeField] private Vector2 _trackScrollDirection = new Vector2(0f, 1f);
    [SerializeField] private float _trackScrollScale = 0.25f;

    [Header("Звук (клип назначается в самом AudioSource)")]
    [SerializeField] private AudioSource _driveSound;

    private Rigidbody _rb;
    private PlayerController _player;
    private AudioListener _cabListener;

    private bool _driving;
    private float _throttle;
    private float _steer;
    private float _forwardSpeed;

    private float _lookYaw;
    private float _lookPitch;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (_cabCamera != null)
        {
            _cabListener = _cabCamera.GetComponent<AudioListener>();
            _cabCamera.enabled = false;

            if (_cabListener != null)
            {
                _cabListener.enabled = false;
            }
        }

        if (_driveSound != null)
        {
            _driveSound.loop = true;
            _driveSound.volume = 0f;
        }
    }

    private void Start()
    {
        _player = FindFirstObjectByType<PlayerController>();
    }

    private void Update()
    {
        if (Keyboard.current == null || Mouse.current == null)
        {
            return;
        }

        if (Keyboard.current.fKey.wasPressedThisFrame && VehicleToggle.Frame != Time.frameCount)
        {
            if (_driving)
            {
                VehicleToggle.Frame = Time.frameCount;
                Exit();
            }
            else if (CanEnter())
            {
                VehicleToggle.Frame = Time.frameCount;
                Enter();
            }
        }

        _throttle = 0f;
        _steer = 0f;

        if (_driving)
        {
            if (Keyboard.current.wKey.isPressed) _throttle += 1f;
            if (Keyboard.current.sKey.isPressed) _throttle -= 1f;
            if (Keyboard.current.dKey.isPressed) _steer += 1f;
            if (Keyboard.current.aKey.isPressed) _steer -= 1f;

            UpdateLook();
        }

        UpdateTracks(Time.deltaTime);
        UpdateSounds();
    }

    private void FixedUpdate()
    {
        Vector3 velocity = _rb.linearVelocity;
        float speed = _throttle >= 0f ? _throttle * _moveSpeed : _throttle * _reverseSpeed;
        Vector3 horizontal = transform.forward * speed;

        _rb.linearVelocity = new Vector3(horizontal.x, velocity.y, horizontal.z);
        _rb.angularVelocity = Vector3.up * (_steer * _turnSpeed * Mathf.Deg2Rad);

        _forwardSpeed = speed;
    }

    // =========================================================
    // ПОСАДКА / ВЫСАДКА
    // =========================================================

    private bool CanEnter()
    {
        if (_player == null)
        {
            _player = FindFirstObjectByType<PlayerController>();
        }

        if (_player == null || _cabCamera == null || !_player.gameObject.activeInHierarchy)
        {
            return false;
        }

        Vector3 delta = _player.transform.position - transform.position;
        delta.y = 0f;

        return delta.magnitude <= _enterDistance;
    }

    private void Enter()
    {
        _driving = true;
        _player.gameObject.SetActive(false);

        _cabCamera.enabled = true;

        if (_cabListener != null)
        {
            _cabListener.enabled = true;
        }

        _lookYaw = 0f;
        _lookPitch = 0f;
        _cabCamera.transform.localRotation = Quaternion.identity;
    }

    private void Exit()
    {
        _driving = false;

        _cabCamera.enabled = false;

        if (_cabListener != null)
        {
            _cabListener.enabled = false;
        }

        Vector3 exitPos = transform.TransformPoint(_exitLocalOffset);

        if (Physics.Raycast(
            exitPos + Vector3.up * 10f,
            Vector3.down,
            out RaycastHit hit,
            30f,
            ~0,
            QueryTriggerInteraction.Ignore))
        {
            exitPos.y = hit.point.y + 0.1f;
        }

        _player.transform.position = exitPos;
        _player.gameObject.SetActive(true);
    }

    private void UpdateLook()
    {
        if (_cabCamera == null)
        {
            return;
        }

        Vector2 delta = Mouse.current.delta.ReadValue() * _lookSensitivity;

        _lookYaw = Mathf.Clamp(_lookYaw + delta.x, -_maxYaw, _maxYaw);
        _lookPitch = Mathf.Clamp(_lookPitch - delta.y, _minPitch, _maxPitch);

        _cabCamera.transform.localRotation = Quaternion.Euler(_lookPitch, _lookYaw, 0f);
    }

    // =========================================================
    // ГУСЕНИЦЫ И ЗВУК
    // =========================================================

    private void UpdateTracks(float dt)
    {
        if (_tracks == null)
        {
            return;
        }

        for (int i = 0; i < _tracks.Length; i++)
        {
            if (_tracks[i] == null)
            {
                continue;
            }

            float side = (_trackSides != null && i < _trackSides.Length) ? _trackSides[i] : 0f;

            // Разворот на месте: левая гусеница едет назад, правая вперёд
            float speed = _forwardSpeed + side * _steer * _moveSpeed * 0.5f;

            Material material = _tracks[i].material;
            material.mainTextureOffset += _trackScrollDirection * (speed * dt * _trackScrollScale);
        }
    }

    [ContextMenu("Найти гусеницы автоматически")]
    public void AutoDetectTracks()
    {
        Renderer[] all = GetComponentsInChildren<Renderer>();
        var found = new System.Collections.Generic.List<Renderer>();
        var sides = new System.Collections.Generic.List<float>();

        Vector3 min = Vector3.one * float.MaxValue;
        Vector3 max = Vector3.one * float.MinValue;

        foreach (Renderer r in all)
        {
            Vector3 p = transform.InverseTransformPoint(r.bounds.center);
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }

        Vector3 span = max - min;

        foreach (Renderer r in all)
        {
            if (r is ParticleSystemRenderer)
            {
                continue;
            }

            Vector3 center = transform.InverseTransformPoint(r.bounds.center);
            Vector3 s = r.bounds.size;
            float length = Mathf.Abs(transform.forward.x) * s.x + Mathf.Abs(transform.forward.z) * s.z;

            bool low = center.y < min.y + span.y * 0.35f;
            bool aside = Mathf.Abs(center.x) > span.x * 0.2f;
            bool longEnough = length > span.z * 0.35f;

            if (low && aside && longEnough)
            {
                found.Add(r);
                sides.Add(Mathf.Sign(center.x));
            }
        }

        _tracks = found.ToArray();
        _trackSides = sides.ToArray();

        Debug.Log("Найдено гусениц: " + found.Count, this);
    }

    private void UpdateSounds()
    {
        if (_driveSound == null)
        {
            return;
        }

        bool moving = _driving && (Mathf.Abs(_throttle) > 0.01f || Mathf.Abs(_steer) > 0.01f);
        float targetVolume = moving ? 1f : (_driving ? 0.3f : 0f);

        _driveSound.volume = Mathf.MoveTowards(_driveSound.volume, targetVolume, Time.deltaTime * 2f);
        _driveSound.pitch = Mathf.Lerp(0.8f, 1.3f, Mathf.Abs(_forwardSpeed) / Mathf.Max(_moveSpeed, 0.01f));

        if (_driveSound.volume > 0.01f && !_driveSound.isPlaying)
        {
            _driveSound.Play();
        }
        else if (_driveSound.volume <= 0.01f && _driveSound.isPlaying)
        {
            _driveSound.Pause();
        }
    }

    // =========================================================
    // ПОДСКАЗКИ
    // =========================================================

    private void OnGUI()
    {
        string text = null;

        if (_driving)
        {
            text = "WASD — ехать    F — выйти";
        }
        else if (CanEnter())
        {
            text = "F — сесть в бульдозер";
        }

        if (text == null)
        {
            return;
        }

        GUIStyle style = new GUIStyle(GUI.skin.label)
        {
            fontSize = 20,
            alignment = TextAnchor.MiddleCenter
        };

        GUI.Label(new Rect(0, Screen.height - 70, Screen.width, 40), text, style);
    }
}
