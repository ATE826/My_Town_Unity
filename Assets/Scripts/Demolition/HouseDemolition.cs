using System.Collections.Generic;
using UnityEngine;

// Здание, заранее разрезанное на куски (см. Tools → Prepare House For Demolition).
// Когда техника задевает любой кусок, дом рассыпается; через время обломки уменьшаются и исчезают.
public class HouseDemolition : MonoBehaviour
{
    [Header("Разлёт")]
    [SerializeField] private float _force = 6f;             // скорость осколков у точки удара (м/с)
    [SerializeField] private float _upward = 0.4f;          // подброс вверх
    [SerializeField] private float _waveSpeed = 12f;        // скорость «волны» обрушения по зданию (м/с)
    [SerializeField] private float _spin = 3f;              // случайное вращение
    [SerializeField] private float _vehicleInfluence = 0.5f; // сколько скорости техники передаётся обломкам

    [Header("Исчезновение")]
    [SerializeField] private float _lifetime = 8f;
    [SerializeField] private float _lifetimeSpread = 3f;
    [SerializeField] private float _shrinkTime = 1.5f;

    [Header("Звук обрушения (необязательно)")]
    [SerializeField] private AudioClip _collapseClip;        // достаточно перетащить сюда клип
    [SerializeField] private float _collapseVolume = 1f;
    [SerializeField] private AudioSource _collapseSound;     // либо свой AudioSource (3D, с настройками)

    private Rigidbody[] _bodies;
    private Vector3[] _scales;
    private float[] _releaseAt;
    private float[] _dieAt;
    private bool[] _released;
    private int _alive;

    private Vector3 _impactPoint;
    private Vector3 _vehicleVelocity;

    public bool IsDemolished { get; private set; }

    private void Awake()
    {
        _bodies = GetComponentsInChildren<Rigidbody>();
        _scales = new Vector3[_bodies.Length];
        _releaseAt = new float[_bodies.Length];
        _dieAt = new float[_bodies.Length];
        _released = new bool[_bodies.Length];
        _alive = _bodies.Length;

        for (int i = 0; i < _bodies.Length; i++)
        {
            _bodies[i].isKinematic = true;
            _scales[i] = _bodies[i].transform.localScale;
        }

        enabled = false;
    }

    public void Demolish(Vector3 impactPoint, Vector3 vehicleVelocity)
    {
        if (IsDemolished)
        {
            return;
        }

        IsDemolished = true;
        _impactPoint = impactPoint;
        _vehicleVelocity = new Vector3(vehicleVelocity.x, 0f, vehicleVelocity.z);

        float now = Time.time;

        for (int i = 0; i < _bodies.Length; i++)
        {
            float distance = Vector3.Distance(_bodies[i].worldCenterOfMass, impactPoint);
            _releaseAt[i] = now + distance / Mathf.Max(_waveSpeed, 0.01f);
        }

        if (_collapseSound != null)
        {
            _collapseSound.Play();
        }
        else if (_collapseClip != null)
        {
            AudioSource.PlayClipAtPoint(_collapseClip, impactPoint, _collapseVolume);
        }

        enabled = true;
    }

    private void Update()
    {
        float now = Time.time;

        for (int i = 0; i < _bodies.Length; i++)
        {
            Rigidbody body = _bodies[i];

            if (body == null)
            {
                continue;
            }

            if (!_released[i])
            {
                if (now >= _releaseAt[i])
                {
                    Release(i, now);
                }

                continue;
            }

            float left = _dieAt[i] - now;

            if (left <= 0f)
            {
                Destroy(body.gameObject);
                _bodies[i] = null;
                _alive--;
            }
            else if (left < _shrinkTime)
            {
                body.transform.localScale = _scales[i] * (left / _shrinkTime);
            }
        }

        if (_alive <= 0)
        {
            enabled = false;
        }
    }

    private void Release(int i, float now)
    {
        Rigidbody body = _bodies[i];
        body.isKinematic = false;

        Vector3 direction = body.worldCenterOfMass - _impactPoint;
        float distance = direction.magnitude;
        direction = distance > 0.01f ? direction / distance : Vector3.up;

        // Чем дальше от удара, тем слабее разлёт
        float falloff = 1f / (1f + distance * 0.15f);

        Vector3 push = direction + Vector3.up * _upward + Random.insideUnitSphere * 0.3f;

        body.AddForce(push * (_force * falloff) + _vehicleVelocity * _vehicleInfluence, ForceMode.VelocityChange);
        body.AddTorque(Random.insideUnitSphere * _spin, ForceMode.VelocityChange);

        _released[i] = true;
        _dieAt[i] = now + _lifetime + Random.Range(-_lifetimeSpread, _lifetimeSpread);
    }
}
