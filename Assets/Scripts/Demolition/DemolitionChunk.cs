using UnityEngine;

// Кусок разрушаемого здания. Первый контакт с техникой запускает обрушение всего здания.
public class DemolitionChunk : MonoBehaviour
{
    private HouseDemolition _house;

    private void Awake()
    {
        _house = GetComponentInParent<HouseDemolition>();
    }

    private void OnCollisionEnter(Collision collision)
    {
        HouseDemolition house = _house;

        if (house == null || house.IsDemolished)
        {
            return;
        }

        Collider other = collision.collider;

        bool isVehicle =
            other.GetComponentInParent<BulldozerController>() != null ||
            other.GetComponentInParent<ExcavatorController>() != null;

        if (!isVehicle)
        {
            return;
        }

        Vector3 point = collision.contactCount > 0
            ? collision.GetContact(0).point
            : other.transform.position;

        Vector3 velocity = collision.rigidbody != null
            ? collision.rigidbody.linearVelocity
            : Vector3.zero;

        house.Demolish(point, velocity);
    }
}
