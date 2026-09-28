using UnityEngine;

public class NpcMovement : MonoBehaviour
{
    [Header("Маршрут")]
    public RoadPath path;

    [Header("Движение")]
    public float speed = 3f;

    [Header("Поворот")]
    public float rotationSpeed = 5f;

    [Header("Расстояние до точки")]
    public float pointReachDistance = 1f;

    private int currentPoint;

    private void Start()
    {
        // Находим ближайшую точку маршрута
        currentPoint = FindNearestPoint();
    }

    private void Update()
    {
        if (path == null)
            return;

        if (path.points == null || path.points.Length == 0)
            return;

        Transform targetPoint = path.points[currentPoint];

        if (targetPoint == null)
            return;

        // NPC не проверяет других NPC, машины или пешеходов —
        // он просто идёт по маршруту, не останавливаясь

        // Направление к следующей точке
        Vector3 direction =
            targetPoint.position - transform.position;

        direction.y = 0f;

        // Если точка ещё далеко
        if (direction.magnitude > pointReachDistance)
        {
            // Поворачиваем NPC
            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            // Двигаем NPC вперёд
            transform.position +=
                transform.forward *
                speed *
                Time.deltaTime;
        }
        else
        {
            // Переходим к следующей точке
            currentPoint++;

            if (currentPoint >= path.points.Length)
            {
                if (path.loop)
                {
                    currentPoint = 0;
                }
                else
                {
                    currentPoint = path.points.Length - 1;
                }
            }
        }
    }

    private int FindNearestPoint()
    {
        int nearestPoint = 0;

        float nearestDistance = Mathf.Infinity;

        for (int i = 0; i < path.points.Length; i++)
        {
            if (path.points[i] == null)
                continue;

            float distance = Vector3.Distance(
                transform.position,
                path.points[i].position
            );

            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestPoint = i;
            }
        }

        return nearestPoint;
    }
}