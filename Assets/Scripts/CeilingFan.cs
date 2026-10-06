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
