using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class ExcavatorController : MonoBehaviour
{
    private enum Phase
    {
        Idle,
        LowerArm,
        Digging,
        RaiseCarry,
        RaiseForDump,
        Tilting,
        Emptying,
        ResetArm
    }

    [Header("Части модели")]
    [SerializeField] private Transform _boom;          // стрела
    [SerializeField] private Transform _bucket;        // ковш
    [SerializeField] private Transform _bucketTip;     // точка, где ковш касается земли
    [SerializeField] private Transform _sandVisual;    // «песок» в ковше (необязательно)
    [SerializeField] private Vector3 _sandGrowAxis = Vector3.up;  // локальная ось ковша, вдоль которой растёт песок
    [SerializeField] private Transform[] _wheels;      // колёса (если есть)
    [SerializeField] private Vector3[] _wheelAxes;     // ось вращения каждого колеса (локальная)
    [SerializeField] private float _wheelRadius = 0.8f;
    [SerializeField] private bool _rotateWheels = false;

    [Header("Гусеницы")]
    [SerializeField] private Renderer[] _tracks;       // меши гусениц (перетащите или ПКМ на скрипте → «Найти гусеницы»)
    [SerializeField] private float[] _trackSides;      // -1 левая, +1 правая
    [SerializeField] private Vector2 _trackScrollDirection = new Vector2(0f, 1f);
    [SerializeField] private float _trackScrollScale = 0.25f;  // сдвиг текстуры на метр пути

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
    [SerializeField] private float _moveSpeed = 4f;
    [SerializeField] private float _reverseSpeed = 2.5f;
    [SerializeField] private float _turnSpeed = 45f;

    [Header("Стрела (углы вокруг оси; + = вниз)")]
    [SerializeField] private Vector3 _boomAxis = Vector3.right;
    [SerializeField] private float _boomCarryAngle = 0f;
    [SerializeField] private float _boomLowAngle = 15f;
    [SerializeField] private float _boomHighAngle = -35f;
    [SerializeField] private float _boomSpeed = 35f;

    [Header("Ковш (+ = вываливает вперёд)")]
    [SerializeField] private Vector3 _bucketAxis = Vector3.right;
    [SerializeField] private float _bucketOpenAngle = 0f;
    [SerializeField] private float _bucketCurlAngle = -50f;
    [SerializeField] private float _bucketCarryCurlAngle = -75f;   // угол ковша с песком: загнут сильнее, чтобы не высыпался
    [SerializeField] private float _bucketDumpAngle = 55f;
    [SerializeField] private float _bucketSpeed = 60f;
    [SerializeField] private float _digCurlSpeed = 18f;

    [Header("Копание")]
    [SerializeField] private float _digRadius = 1.5f;
    [SerializeField] private float _digDepthPerSecond = 1.2f;
    [SerializeField] private float _capacity = 2f;
    [SerializeField] private float _pourHeightPerSecond = 1.5f;
    [SerializeField] private float _pourRadiusScale = 1.3f;

    [Header("Частицы")]
    [SerializeField] private ParticleSystem _sandParticles;

    [Header("Звуки (клипы назначаются в самих AudioSource)")]
    [SerializeField] private AudioSource _driveSound;    // езда, зациклен
    [SerializeField] private AudioSource _scoopSound;    // зачерпывание
    [SerializeField] private AudioSource _dumpSound;     // высыпание

    private Rigidbody _rb;
    private PlayerController _player;

    private bool _driving;
    private float _throttle;
    private float _steer;
    private float _forwardSpeed;

    private float _lookYaw;
    private float _lookPitch;

    private Quaternion _boomRest;
    private Quaternion _bucketRest;
    private float _boomAngle;
    private float _bucketAngle;
    private float _boomTarget;
    private float _bucketTarget;
    private float _bucketSpeedNow;

    private Phase _phase = Phase.Idle;
    private float _load;
    private float _pourTime;
    private bool _warnedNoTerrain;
    private Vector3 _sandBaseScale = Vector3.one;
    private AudioListener _cabListener;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
        _rb.interpolation = RigidbodyInterpolation.Interpolate;

        if (_boom != null)
        {
            _boomRest = _boom.localRotation;
        }

        if (_bucket != null)
        {
            _bucketRest = _bucket.localRotation;
        }

        _boomAngle = _boomTarget = _boomCarryAngle;
        _bucketAngle = _bucketTarget = _bucketOpenAngle;
        _bucketSpeedNow = _bucketSpeed;

        if (_sandVisual != null)
        {
            _sandBaseScale = _sandVisual.localScale;
        }

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

        TerrainSculptor.EnsureRuntimeCopies(transform.position);
        ApplyArm();
        UpdateSandVisual();
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

            if (Keyboard.current.eKey.wasPressedThisFrame)
            {
                StartAction();
            }

            UpdateLook();
        }

        UpdateArm(Time.deltaTime);
        UpdateWheels(Time.deltaTime);
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

        _forwardSpeed = Vector3.Dot(horizontal, transform.forward);
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
    // СТРЕЛА И КОВШ (E)
    // =========================================================

    [ContextMenu("Тест: копать / высыпать (E)")]
    private void StartAction()
    {
        if (_phase != Phase.Idle || _boom == null || _bucket == null)
        {
            return;
        }

        if (_load > 0.01f)
        {
            _pourTime = 0f;
            _phase = Phase.RaiseForDump;
        }
        else
        {
            _phase = Phase.LowerArm;
            Play(_scoopSound);
        }
    }

    private bool HasLoad => _load > 0.01f;

    // Знак загиба берём у Bucket Carry Curl Angle: загиб и высыпание всегда в противоположные стороны,
    // а Bucket Curl Angle и Bucket Dump Angle задают только величину наклона.
    private float CurlSign => _bucketCarryCurlAngle >= 0f ? 1f : -1f;
    private float CurlAngle => CurlSign * Mathf.Abs(_bucketCurlAngle);
    private float DumpAngle => -CurlSign * Mathf.Abs(_bucketDumpAngle);

    private void UpdateArm(float dt)
    {
        switch (_phase)
        {
            case Phase.Idle:
                // С песком ковш загнут сильнее, без песка — обычное положение
                _boomTarget = _boomCarryAngle;
                _bucketTarget = HasLoad ? _bucketCarryCurlAngle : _bucketOpenAngle;
                _bucketSpeedNow = _bucketSpeed;
                break;

            case Phase.LowerArm:
                _boomTarget = _boomLowAngle;
                _bucketTarget = _bucketOpenAngle;
                _bucketSpeedNow = _bucketSpeed;

                if (Mathf.Approximately(_boomAngle, _boomTarget) &&
                    Mathf.Approximately(_bucketAngle, _bucketTarget))
                {
                    _phase = Phase.Digging;
                }
                break;

            case Phase.Digging:
                _bucketTarget = CurlAngle;
                _bucketSpeedNow = _digCurlSpeed;
                Dig(dt);

                if (Mathf.Approximately(_bucketAngle, CurlAngle) ||
                    _load >= _capacity - 0.01f)
                {
                    _phase = Phase.RaiseCarry;
                }
                break;

            case Phase.RaiseCarry:
                _boomTarget = _boomCarryAngle;
                _bucketTarget = HasLoad ? _bucketCarryCurlAngle : _bucketOpenAngle;
                _bucketSpeedNow = _bucketSpeed;

                if (Mathf.Approximately(_boomAngle, _boomTarget) &&
                    Mathf.Approximately(_bucketAngle, _bucketTarget))
                {
                    _phase = Phase.Idle;
                }
                break;

            case Phase.RaiseForDump:
                _boomTarget = _boomHighAngle;
                _bucketSpeedNow = _bucketSpeed;

                if (Mathf.Approximately(_boomAngle, _boomTarget))
                {
                    _phase = Phase.Tilting;
                    Play(_dumpSound);
                }
                break;

            case Phase.Tilting:
                _bucketTarget = DumpAngle;
                _bucketSpeedNow = _bucketSpeed * 0.5f;

                // Песок начинает сыпаться, когда ковш наклонён больше чем наполовину
                float progress = Mathf.InverseLerp(_bucketCarryCurlAngle, DumpAngle, _bucketAngle);

                if (progress > 0.5f)
                {
                    Pour(dt);
                }

                if (Mathf.Approximately(_bucketAngle, DumpAngle))
                {
                    _phase = Phase.Emptying;
                }
                break;

            case Phase.Emptying:
                Pour(dt);

                if (_load <= 0.001f)
                {
                    _load = 0f;
                    SetParticles(false);
                    _phase = Phase.ResetArm;
                }
                break;

            case Phase.ResetArm:
                _boomTarget = _boomCarryAngle;
                _bucketTarget = _bucketOpenAngle;
                _bucketSpeedNow = _bucketSpeed;

                if (Mathf.Approximately(_boomAngle, _boomTarget) &&
                    Mathf.Approximately(_bucketAngle, _bucketTarget))
                {
                    _phase = Phase.Idle;
                }
                break;
        }

        _boomAngle = Mathf.MoveTowards(_boomAngle, _boomTarget, _boomSpeed * dt);
        _bucketAngle = Mathf.MoveTowards(_bucketAngle, _bucketTarget, _bucketSpeedNow * dt);

        ApplyArm();
        UpdateSandVisual();
    }

    private void ApplyArm()
    {
        if (_boom != null)
        {
            _boom.localRotation = _boomRest * Quaternion.AngleAxis(_boomAngle, _boomAxis);
        }

        if (_bucket != null)
        {
            _bucket.localRotation = _bucketRest * Quaternion.AngleAxis(_bucketAngle, _bucketAxis);
        }
    }

    private void Dig(float dt)
    {
        if (_bucketTip == null)
        {
            return;
        }

        float volume = TerrainSculptor.Modify(
            _bucketTip.position,
            _digRadius,
            -_digDepthPerSecond * dt,
            _capacity - _load
        );

        _load += -volume;

        if (volume == 0f && !_warnedNoTerrain && TerrainSculptor.FindTerrain(_bucketTip.position) == null)
        {
            _warnedNoTerrain = true;
            Debug.LogWarning("Под ковшом нет Terrain: точка " + _bucketTip.position + ". Копать нечего.", this);
        }
    }

    private void Pour(float dt)
    {
        _pourTime += dt;
        SetParticles(true);

        if (_bucketTip == null)
        {
            _load = 0f;
            return;
        }

        float volume = TerrainSculptor.Modify(
            _bucketTip.position,
            _digRadius * _pourRadiusScale,
            _pourHeightPerSecond * dt,
            _load
        );

        _load -= volume;

        // Если рельеф не принимает песок (нет Terrain под ковшом), не зависаем
        if (_pourTime > 10f)
        {
            _load = 0f;
        }
    }

    private void SetParticles(bool on)
    {
        if (_sandParticles == null)
        {
            return;
        }

        if (on && !_sandParticles.isPlaying)
        {
            _sandParticles.Play();
        }
        else if (!on && _sandParticles.isPlaying)
        {
            _sandParticles.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
    }

    private void UpdateSandVisual()
    {
        if (_sandVisual == null)
        {
            return;
        }

        float fill = Mathf.Clamp01(_load / Mathf.Max(_capacity, 0.01f));

        _sandVisual.gameObject.SetActive(fill > 0.02f);

        // Песок растёт вверх от дна ковша, ширина не меняется
        Vector3 axis = new Vector3(
            Mathf.Abs(_sandGrowAxis.x),
            Mathf.Abs(_sandGrowAxis.y),
            Mathf.Abs(_sandGrowAxis.z));

        float height = Mathf.Lerp(0.08f, 1f, fill);

        _sandVisual.localScale = Vector3.Scale(_sandBaseScale, Vector3.one + axis * (height - 1f));
    }

    // =========================================================
    // КОЛЁСА И ЗВУК
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

        // Габариты машины в её локальных осях
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
            if (r is ParticleSystemRenderer ||
                (_bucket != null && r.transform.IsChildOf(_bucket)) ||
                (_boom != null && r.transform.IsChildOf(_boom)))
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

    private void UpdateWheels(float dt)
    {
        if (!_rotateWheels || _wheels == null || _wheelRadius <= 0f)
        {
            return;
        }

        float degrees = _forwardSpeed * dt / _wheelRadius * Mathf.Rad2Deg;

        // При развороте на месте колёса тоже немного крутятся
        degrees += _steer * dt * 120f;

        for (int i = 0; i < _wheels.Length; i++)
        {
            if (_wheels[i] == null)
            {
                continue;
            }

            Vector3 axis = (_wheelAxes != null && i < _wheelAxes.Length && _wheelAxes[i] != Vector3.zero)
                ? _wheelAxes[i]
                : Vector3.right;

            _wheels[i].Rotate(axis, degrees, Space.Self);
        }
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

    private static void Play(AudioSource source)
    {
        if (source != null)
        {
            source.Play();
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
            text = "WASD — ехать    E — копать / высыпать    F — выйти";
        }
        else if (CanEnter())
        {
            text = "F — сесть в экскаватор";
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

    private void OnDrawGizmosSelected()
    {
        if (_bucketTip != null)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(_bucketTip.position, _digRadius);
        }
    }
}
