using System.Collections.Generic;
using UnityEngine;

public class CeilingFan : MonoBehaviour
{
    [Header("Вращение")]
    [SerializeField] private Transform _blades;                      // вращающаяся часть (лопасти); если пусто - сам объект
    [SerializeField] private Vector3 _axis = Vector3.up;             // локальная ось вращения
    [SerializeField] private float _rotationSpeed = 360f;            // скорость, градусов в секунду
    [SerializeField] private float _spinUpTime = 1.5f;               // время разгона/остановки, сек (0 - мгновенно)

    [Header("Звук")]
    [SerializeField] private AudioSource _audioSource;               // источник звука (зацикленный)
    [SerializeField] private AudioClip _loopClip;                    // звук работы вентилятора
    [SerializeField, Range(0f, 1f)] private float _volume = 0.5f;    // громкость
    [SerializeField, Range(0.1f, 3f)] private float _pitch = 1f;     // высота тона при полной скорости
    [SerializeField] private bool _pitchFollowsSpeed = true;         // звук разгоняется/затихает вместе с лопастями

    [Header("Обдув")]
    [SerializeField] private bool _blowObjects = true;               // сдувать лёгкие предметы
    [SerializeField] private float _maxMass = 0.05f;                 // сдуваются предметы с массой не больше этого значения (Rigidbody.mass)
    [SerializeField] private float _blowRadius = 2f;                 // радиус зоны обдува вокруг оси вентилятора, м
    [SerializeField] private float _blowHeight = 3f;                 // на какую глубину вниз от вентилятора действует поток, м
    [SerializeField] private float _blowForce = 6f;                  // ускорение вдоль поверхности, м/с² (не зависит от массы)
    [SerializeField] private float _liftForce = 1.5f;                // подъёмная сила, чтобы лёгкие предметы чуть подпрыгивали
    [SerializeField] private float _turbulence = 0.5f;               // случайное «трепыхание» предметов
    [SerializeField] private LayerMask _blowMask = ~0;               // какие слои обдуваются

    private const int MaxBlown = 64;
    private readonly Collider[] _hits = new Collider[MaxBlown];
    private readonly HashSet<Rigidbody> _processed = new HashSet<Rigidbody>();

    private bool _isOn;
    private float _speedFactor; // 0..1 - текущая доля от полной скорости

    private void Awake()
    {
        if (_blades == null) _blades = transform;

        if (_audioSource == null) _audioSource = GetComponent<AudioSource>();
        if (_audioSource == null) _audioSource = gameObject.AddComponent<AudioSource>();

        if (_loopClip == null) _loopClip = _audioSource.clip; // клип мог быть назначен прямо в AudioSource
        _audioSource.clip = _loopClip;
        _audioSource.loop = true;
        _audioSource.playOnAwake = false;
        _audioSource.spatialBlend = 1f; // 3D-звук
    }

    // Вызывается выключателем
    public void SetActive(bool on)
    {
        _isOn = on;
        if (_spinUpTime <= 0f) _speedFactor = on ? 1f : 0f;
    }

    private void Update()
    {
        float target = _isOn ? 1f : 0f;

        if (_spinUpTime > 0f)
        {
            _speedFactor = Mathf.MoveTowards(_speedFactor, target, Time.deltaTime / _spinUpTime);
        }

        if (_speedFactor > 0f)
        {
            _blades.Rotate(_axis, _rotationSpeed * _speedFactor * Time.deltaTime, Space.Self);
        }

        UpdateAudio();
    }

    private void FixedUpdate()
    {
        if (_blowObjects && _speedFactor > 0.01f) BlowObjects();
    }

    // Сдувает лёгкие предметы под вентилятором: толкает их от оси наружу, с небольшим подъёмом
    private void BlowObjects()
    {
        Vector3 top = _blades.position;
        Vector3 bottom = top + Vector3.down * _blowHeight;

        int count = Physics.OverlapCapsuleNonAlloc(top, bottom, _blowRadius, _hits, _blowMask, QueryTriggerInteraction.Ignore);
        _processed.Clear();

        for (int i = 0; i < count; i++)
        {
            Rigidbody rb = _hits[i].attachedRigidbody;
            if (rb == null || rb.isKinematic || !rb.useGravity) continue; // useGravity выключен у предмета в руках игрока
            if (rb.mass > _maxMass || !_processed.Add(rb)) continue;

            Vector3 offset = rb.worldCenterOfMass - top;
            offset.y = 0f;
            float distance = offset.magnitude;
            if (distance > _blowRadius) continue;

            // Сильнее у оси вентилятора, слабее к краю зоны
            float falloff = 1f - Mathf.Clamp01(distance / _blowRadius);
            Vector3 outward = distance > 0.01f ? offset / distance : new Vector3(Random.value - 0.5f, 0f, Random.value - 0.5f).normalized;

            Vector3 acceleration = outward * _blowForce + Vector3.up * _liftForce;
            acceleration += Random.insideUnitSphere * _turbulence;
            acceleration *= _speedFactor * Mathf.Lerp(0.3f, 1f, falloff);

            rb.AddForce(acceleration, ForceMode.Acceleration);
            rb.AddTorque(Random.insideUnitSphere * _turbulence * _speedFactor, ForceMode.Acceleration);
        }
    }

    private void UpdateAudio()
    {
        if (_loopClip == null) return;

        if (_speedFactor > 0.001f)
        {
            _audioSource.volume = _volume * (_pitchFollowsSpeed ? _speedFactor : 1f);
            _audioSource.pitch = _pitchFollowsSpeed ? Mathf.Lerp(0.5f, 1f, _speedFactor) * _pitch : _pitch;
            if (!_audioSource.isPlaying) _audioSource.Play();
        }
        else if (_audioSource.isPlaying)
        {
            _audioSource.Stop();
        }
    }
}
