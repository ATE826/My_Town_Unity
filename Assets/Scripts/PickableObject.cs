using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class PickableObject : MonoBehaviour
{
    [SerializeField] private float _followSpeed = 12f; // насколько резво предмет тянется к точке удержания
    [SerializeField] private float _maxSpeed = 15f;    // ограничение скорости, чтобы предмет не "выстреливал"
    [SerializeField] private float _throwMultiplier = 1.5f; // усиление инерции при отпускании
    [SerializeField] private float _maxThrowSpeed = 25f;    // ограничение скорости броска

    private Rigidbody _rb;
    private Collider[] _colliders;
    private CollisionDetectionMode _savedMode;

    public Vector3 Position => _rb.position;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _colliders = GetComponentsInChildren<Collider>();
    }

    // Вызывается игроком, когда он схватил предмет
    public void Grab(Collider playerCollider)
    {
        _savedMode = _rb.collisionDetectionMode;
        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic; // не проваливается сквозь стены
        _rb.useGravity = false;

        SetIgnorePlayer(playerCollider, true); // предмет не толкает игрока
    }

    // Вызывается каждый FixedUpdate, пока предмет удерживается
    public void MoveTo(Vector3 target)
    {
        Vector3 velocity = (target - _rb.position) * _followSpeed;
        SetVelocity(Vector3.ClampMagnitude(velocity, _maxSpeed));
        _rb.angularVelocity = Vector3.zero; // предмет не крутится в руках
    }

    // Вызывается, когда ПКМ отпущена: предмет сохраняет скорость и дальше летит по законам физики
    public void Release(Collider playerCollider)
    {
        Vector3 throwVelocity = Vector3.ClampMagnitude(GetVelocity() * _throwMultiplier, _maxThrowSpeed);

        _rb.useGravity = true;
        _rb.collisionDetectionMode = _savedMode;
        SetVelocity(throwVelocity);
        _rb.WakeUp();

        SetIgnorePlayer(playerCollider, false);
    }

    private void SetIgnorePlayer(Collider playerCollider, bool ignore)
    {
        if (playerCollider == null) return;

        foreach (Collider col in _colliders)
        {
            Physics.IgnoreCollision(col, playerCollider, ignore);
        }
    }

    private Vector3 GetVelocity()
    {
#if UNITY_6000_0_OR_NEWER
        return _rb.linearVelocity;
#else
        return _rb.velocity;
#endif
    }

    private void SetVelocity(Vector3 value)
    {
#if UNITY_6000_0_OR_NEWER
        _rb.linearVelocity = value;
#else
        _rb.velocity = value;
#endif
    }
}