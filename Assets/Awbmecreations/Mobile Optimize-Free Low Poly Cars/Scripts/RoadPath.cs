using UnityEngine;

public class RoadPath : MonoBehaviour
{
    [Header("Точки маршрута")]
    public Transform[] points;

    [Header("Зациклить маршрут")]
    public bool loop = true;

    private void OnDrawGizmos()
    {
        if (points == null || points.Length < 2)
            return;

        for (int i = 0; i < points.Length - 1; i++)
        {
            if (points[i] == null || points[i + 1] == null)
                continue;

            Gizmos.DrawLine(
                points[i].position,
                points[i + 1].position
            );
        }

        if (loop && points[0] != null && points[^1] != null)
        {
            Gizmos.DrawLine(
                points[^1].position,
                points[0].position
            );
        }
    }
}