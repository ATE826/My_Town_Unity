using UnityEngine;

// Хрупкий предмет (бокал, тарелка): при сильном ударе исчезает и проигрывает звук разбивания.
[RequireComponent(typeof(Rigidbody))]
public class BreakableObject : MonoBehaviour
{
    [Header("Разбивание")]
    [SerializeField] private float _breakSpeed = 4f;      // относительная скорость удара (м/с), с которой предмет ломается
    [SerializeField] private float _graceTime = 0.5f;     // после старта сцены не ломается, пока физика "устаканится"

    [Header("Звук")]
    [SerializeField] private AudioClip[] _breakSounds;    // варианты звука разбивания (выбирается случайный)
    [SerializeField, Range(0f, 1f)] private float _volume = 1f;

    private bool _broken;

    private void OnCollisionEnter(Collision collision)
    {
        if (_broken || Time.timeSinceLevelLoad < _graceTime) return;

        if (collision.relativeVelocity.magnitude >= _breakSpeed)
        {
            Break();
        }
    }

    private void Break()
    {
        _broken = true;

        if (_breakSounds != null && _breakSounds.Length > 0)
        {
            AudioClip clip = _breakSounds[Random.Range(0, _breakSounds.Length)];
            if (clip != null)
            {
                // Звук играет на временном объекте, потому что сам предмет сейчас исчезнет
                AudioSource.PlayClipAtPoint(clip, transform.position, _volume);
            }
        }

        Destroy(gameObject);
    }
}
