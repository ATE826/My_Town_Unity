using UnityEngine;

public class AirplaneMovement : MonoBehaviour
{
    [System.Serializable]
    public class FlightPoint
    {
        public Transform point;

        [Header("Скорость на этой точке")]
        public float speed = 10f;
    }

    [Header("Маршрут")]
    public FlightPoint[] points;

    [Header("Настройки движения")]
    public float acceleration = 5f;

    [Header("Поворот")]
    public float rotationSpeed = 3f;

    [Header("Расстояние до точки")]
    public float pointReachDistance = 2f;

    private int currentPoint = 0;

    private float currentSpeed = 0f;

    private void Update()
    {
        if (points == null || points.Length == 0)
            return;

        if (points[currentPoint].point == null)
            return;

        Transform targetPoint = points[currentPoint].point;

        // Направление к точке
        Vector3 direction =
            targetPoint.position - transform.position;

        // Если достигли точки
        if (direction.magnitude <= pointReachDistance)
        {
            currentPoint++;

            // Если достигли конца маршрута
            if (currentPoint >= points.Length)
            {
                currentPoint = 0;
            }

            return;
        }

        // Скорость, которую хотим получить
        float targetSpeed = points[currentPoint].speed;

        // Плавно изменяем текущую скорость
        currentSpeed = Mathf.MoveTowards(
            currentSpeed,
            targetSpeed,
            acceleration * Time.deltaTime
        );

        // Поворачиваем самолёт
        Quaternion targetRotation =
            Quaternion.LookRotation(direction);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            targetRotation,
            rotationSpeed * Time.deltaTime
        );

        // Двигаем самолёт
        transform.position +=
            transform.forward *
            currentSpeed *
            Time.deltaTime;
    }
}