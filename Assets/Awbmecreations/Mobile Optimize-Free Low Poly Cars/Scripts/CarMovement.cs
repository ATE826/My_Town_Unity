using UnityEngine;

public class CarMovement : MonoBehaviour
{
    [Header("Маршрут")]
    public RoadPath path;

    [Header("Движение")]
    public float speed = 5f;

    [Header("Поворот")]
    public float rotationSpeed = 5f;

    [Header("Расстояние до точки")]
    public float pointReachDistance = 1f;

    [Header("Безопасная дистанция")]
    public float safeDistance = 5f;

    [Header("Дистанция обнаружения")]
    public float detectionDistance = 10f;

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

        // Проверяем машину впереди
        CarMovement carAhead = FindCarAhead();

        if (carAhead != null)
        {
            float distance = Vector3.Distance(
                transform.position,
                carAhead.transform.position
            );

            // Если машина слишком близко — останавливаемся
            if (distance < safeDistance)
            {
                return;
            }
        }

        // Проверяем пешехода (игрока) впереди
        Pedestrian pedestrianAhead = FindPedestrianAhead();

        if (pedestrianAhead != null)
        {
            float distance = Vector3.Distance(
                transform.position,
                pedestrianAhead.transform.position
            );

            // Если пешеход слишком близко — останавливаемся
            if (distance < safeDistance)
            {
                return;
            }
        }

        // Проверяем NPC-пешехода впереди
        NpcMovement npcAhead = FindNpcAhead();

        if (npcAhead != null)
        {
            float distance = Vector3.Distance(
                transform.position,
                npcAhead.transform.position
            );

            // Если NPC слишком близко — останавливаемся
            if (distance < safeDistance)
            {
                return;
            }
        }

        // Направление к следующей точке
        Vector3 direction =
            targetPoint.position - transform.position;

        direction.y = 0f;

        // Если точка ещё далеко
        if (direction.magnitude > pointReachDistance)
        {
            // Поворачиваем машину
            Quaternion targetRotation =
                Quaternion.LookRotation(direction);

            transform.rotation = Quaternion.Slerp(
                transform.rotation,
                targetRotation,
                rotationSpeed * Time.deltaTime
            );

            // Двигаем машину вперёд
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

    private CarMovement FindCarAhead()
    {
        CarMovement[] cars =
            FindObjectsByType<CarMovement>(
                FindObjectsSortMode.None
            );

        CarMovement closestCar = null;

        float closestDistance = detectionDistance;

        foreach (CarMovement car in cars)
        {
            // Не проверяем саму себя
            if (car == this)
                continue;

            Vector3 directionToCar =
                car.transform.position -
                transform.position;

            directionToCar.y = 0f;

            float distance = directionToCar.magnitude;

            // Машина слишком далеко
            if (distance > detectionDistance)
                continue;

            // Определяем, находится ли машина впереди
            float angle = Vector3.Angle(
                transform.forward,
                directionToCar
            );

            // Если машина сбоку или сзади — игнорируем
            if (angle > 45f)
                continue;

            // Запоминаем ближайшую машину впереди
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestCar = car;
            }
        }

        return closestCar;
    }

    private Pedestrian FindPedestrianAhead()
    {
        Pedestrian[] pedestrians =
            FindObjectsByType<Pedestrian>(
                FindObjectsSortMode.None
            );

        Pedestrian closestPedestrian = null;

        float closestDistance = detectionDistance;

        foreach (Pedestrian pedestrian in pedestrians)
        {
            Vector3 directionToPedestrian =
                pedestrian.transform.position -
                transform.position;

            directionToPedestrian.y = 0f;

            float distance = directionToPedestrian.magnitude;

            // Пешеход слишком далеко
            if (distance > detectionDistance)
                continue;

            // Определяем, находится ли пешеход впереди
            float angle = Vector3.Angle(
                transform.forward,
                directionToPedestrian
            );

            // Если пешеход сбоку или сзади — игнорируем
            if (angle > 45f)
                continue;

            // Запоминаем ближайшего пешехода
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestPedestrian = pedestrian;
            }
        }

        return closestPedestrian;
    }

    private NpcMovement FindNpcAhead()
    {
        NpcMovement[] npcs =
            FindObjectsByType<NpcMovement>(
                FindObjectsSortMode.None
            );

        NpcMovement closestNpc = null;

        float closestDistance = detectionDistance;

        foreach (NpcMovement npc in npcs)
        {
            Vector3 directionToNpc =
                npc.transform.position -
                transform.position;

            directionToNpc.y = 0f;

            float distance = directionToNpc.magnitude;

            // NPC слишком далеко
            if (distance > detectionDistance)
                continue;

            // Определяем, находится ли NPC впереди
            float angle = Vector3.Angle(
                transform.forward,
                directionToNpc
            );

            // Если NPC сбоку или сзади — игнорируем
            if (angle > 45f)
                continue;

            // Запоминаем ближайшего NPC впереди
            if (distance < closestDistance)
            {
                closestDistance = distance;
                closestNpc = npc;
            }
        }

        return closestNpc;
    }
}