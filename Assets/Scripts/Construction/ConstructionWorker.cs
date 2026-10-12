using System.Collections;
using UnityEngine;
using UnityEngine.AI;

// Рабочий: берёт кирпич у Concrete_Block_Set, несёт его к стене нового дома и кладёт на место.
// Цикл повторяется, пока не кончатся места под кирпичи.
// Если перед рабочим транспорт — он останавливается, если транспорт едет на него — отходит.
public class ConstructionWorker : MonoBehaviour
{
    private enum Threat { None, Retreat }

    private const float CarryScale = 0.6f;

    private const float NearDistance = 4f;          // ближе этого едущий на нас транспорт считаем угрозой
    private const float RetreatDistance = 9f;       // отсюда начинаем следить за едущим транспортом
    private const float RetreatStep = 4f;           // на сколько метров отходим
    private const float MovingSpeed = 0.3f;         // с такой скоростью транспорт считается едущим
    private const float ContactTime = 3.5f;         // едущий транспорт, который доедет за столько секунд, — угроза

    private ConstructionCrew _crew;
    private NavMeshAgent _agent;
    private Animator _animator;
    private Transform _hand;

    public void Init(ConstructionCrew crew, Animator animator, float speed, int index)
    {
        _crew = crew;
        _animator = animator;

        _agent = GetComponent<NavMeshAgent>();
        _agent.speed = speed;
        _agent.acceleration = 8f;
        _agent.angularSpeed = 360f;
        _agent.radius = 0.3f;
        _agent.height = 1.8f;
        _agent.stoppingDistance = 0.15f;
        _agent.obstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        _agent.avoidancePriority = 30 + index * 5;

        _hand = animator != null ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

        if (_hand == null)
        {
            _hand = transform;
        }

        StartCoroutine(Work());
    }

    private void Update()
    {
        if (_animator != null && _agent != null && _agent.isOnNavMesh)
        {
            _animator.SetFloat("Speed", _agent.velocity.magnitude);
        }
    }

    // =========================================================
    // РАБОЧИЙ ЦИКЛ
    // =========================================================

    private IEnumerator Work()
    {
        // Ждём, пока агент встанет на NavMesh
        float waited = 0f;

        while (!_agent.isOnNavMesh && waited < 5f)
        {
            waited += Time.deltaTime;
            yield return null;
        }

        while (true)
        {
            // 1. К куче кирпичей: берём кирпич, только подойдя к ней на метр
            for (int attempt = 0; attempt < 3; attempt++)
            {
                yield return GoTo(_crew.GetBlockApproachPoint(transform.position));

                if (_crew.DistanceToBlocks(transform.position) <= 1.35f)
                {
                    break;
                }
            }

            yield return FaceAndWait(_crew.BlockCenter, 0.5f);

            // 2. Ждём, пока в стене появится место для следующего кирпича (ряд нельзя начинать,
            //    пока не выложен предыдущий)
            BrickSlot slot;
            ConstructionCrew.ReserveResult result;

            while ((result = _crew.TryReserveSlot(out slot)) == ConstructionCrew.ReserveResult.Wait)
            {
                yield return new WaitForSeconds(0.4f);
            }

            if (result == ConstructionCrew.ReserveResult.Done)
            {
                _agent.isStopped = true;
                yield break;
            }

            // 3. Берём красный кирпич в руки
            Vector3 carrySize = slot.Size * CarryScale;
            GameObject brick = _crew.CreateBrick(carrySize);
            brick.transform.SetParent(_hand, false);
            brick.transform.localPosition = Vector3.zero;
            brick.transform.localRotation = Quaternion.identity;

            // Мировой размер кирпича не зависит от масштаба рабочего и кости руки
            Vector3 handScale = _hand.lossyScale;
            brick.transform.localScale = new Vector3(
                carrySize.x / Mathf.Max(Mathf.Abs(handScale.x), 0.0001f),
                carrySize.y / Mathf.Max(Mathf.Abs(handScale.y), 0.0001f),
                carrySize.z / Mathf.Max(Mathf.Abs(handScale.z), 0.0001f));

            // 4. К месту в стене (в обход техники и стен)
            yield return GoTo(slot.StandPoint);
            yield return FaceAndWait(slot.Position, 0.4f);

            // Если пришлось отойти от техники — возвращаемся к стене
            if ((transform.position - slot.StandPoint).sqrMagnitude > 1f)
            {
                yield return GoTo(slot.StandPoint);
            }

            // 5. Кладём
            yield return PlaceBrick(brick.transform, slot);
            _crew.CompleteSlot(slot, brick.transform);
        }
    }

    // =========================================================
    // ДВИЖЕНИЕ С УЧЁТОМ ТРАНСПОРТА
    // =========================================================

    private IEnumerator GoTo(Vector3 target)
    {
        if (!_agent.isOnNavMesh)
        {
            yield break;
        }

        _agent.isStopped = false;
        _agent.SetDestination(target);

        float stuckTime = 0f;
        float total = 0f;
        Vector3 lastPosition = transform.position;

        while (total < 120f)
        {
            yield return null;
            total += Time.deltaTime;

            // Транспорт: остановиться или отойти
            Threat threat = EvaluateThreat(out Vector3 retreatPoint);

            if (threat != Threat.None)
            {
                yield return Avoid(threat, retreatPoint);
                _agent.isStopped = false;
                _agent.SetDestination(target);
                stuckTime = 0f;
                lastPosition = transform.position;
                continue;
            }

            if (_agent.pathPending)
            {
                continue;
            }

            if (_agent.remainingDistance <= _agent.stoppingDistance + 0.15f)
            {
                break;
            }

            // Застряли: перепроверяем путь
            if ((transform.position - lastPosition).sqrMagnitude < 0.0004f)
            {
                stuckTime += Time.deltaTime;

                if (stuckTime > 3f)
                {
                    stuckTime = 0f;
                    _agent.SetDestination(target);
                }
            }
            else
            {
                stuckTime = 0f;
                lastPosition = transform.position;
            }
        }

        _agent.isStopped = true;
    }

    // Пока транспорт едет на нас — отходим в сторону. Не стоим на месте никогда
    private IEnumerator Avoid(Threat threat, Vector3 retreatPoint)
    {
        while (threat != Threat.None)
        {
            _agent.isStopped = false;
            _agent.SetDestination(retreatPoint);

            yield return null;
            threat = EvaluateThreat(out retreatPoint);
        }
    }

    private Threat EvaluateThreat(out Vector3 retreatPoint)
    {
        retreatPoint = transform.position;

        if (_crew.Vehicles == null)
        {
            return Threat.None;
        }

        Vector3 position = transform.position;

        foreach (Rigidbody vehicle in _crew.Vehicles)
        {
            // Транспорт без водителя для рабочих — просто препятствие (его обходят по NavMesh)
            if (vehicle == null || !_crew.IsDriven(vehicle))
            {
                continue;
            }

            var box = vehicle.GetComponent<BoxCollider>();
            Vector3 closest = box != null ? box.ClosestPoint(position) : vehicle.position;

            Vector3 toWorker = position - closest;
            toWorker.y = 0f;
            float distance = toWorker.magnitude;

            if (distance > RetreatDistance)
            {
                continue;
            }

            toWorker = distance > 0.01f ? toWorker / distance : -vehicle.transform.forward;

            Vector3 velocity = vehicle.linearVelocity;
            velocity.y = 0f;
            float speed = velocity.magnitude;
            bool moving = speed > MovingSpeed;

            // Транспорт едет на рабочего (или вот-вот доедет)
            if (moving)
            {
                Vector3 direction = velocity / speed;
                bool toward = Vector3.Dot(direction, toWorker) > 0.45f;
                bool soon = distance / speed < ContactTime || distance < NearDistance;

                if (toward && soon)
                {
                    // Отходим в сторону от траектории и назад
                    Vector3 side = Vector3.Cross(Vector3.up, direction);

                    if (Vector3.Dot(side, toWorker) < 0f)
                    {
                        side = -side;
                    }

                    Vector3 away = (toWorker * 0.6f + side * 0.8f).normalized;
                    Vector3 point = position + away * RetreatStep;

                    if (NavMesh.SamplePosition(point, out NavMeshHit hit, 3f, NavMesh.AllAreas))
                    {
                        point = hit.position;
                    }

                    retreatPoint = point;
                    return Threat.Retreat;
                }
            }

        }

        return Threat.None;
    }

    // =========================================================
    // РАБОТА НА МЕСТЕ
    // =========================================================

    private IEnumerator FaceAndWait(Vector3 point, float seconds)
    {
        Vector3 direction = point - transform.position;
        direction.y = 0f;

        Quaternion target = direction.sqrMagnitude > 0.001f ? Quaternion.LookRotation(direction) : transform.rotation;

        float time = 0f;

        while (time < seconds)
        {
            time += Time.deltaTime;
            transform.rotation = Quaternion.RotateTowards(transform.rotation, target, 360f * Time.deltaTime);

            // Транспорт едет на нас — отходим, не дожидаясь конца паузы
            Threat threat = EvaluateThreat(out Vector3 retreatPoint);

            if (threat == Threat.Retreat)
            {
                yield return Avoid(threat, retreatPoint);
                _agent.isStopped = true;
            }

            yield return null;
        }
    }

    private IEnumerator PlaceBrick(Transform brick, BrickSlot slot)
    {
        brick.SetParent(null, true);

        Vector3 startPosition = brick.position;
        Quaternion startRotation = brick.rotation;
        Vector3 startScale = brick.localScale;

        const float duration = 0.5f;
        float time = 0f;

        while (time < duration)
        {
            time += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, time / duration);

            brick.position = Vector3.Lerp(startPosition, slot.Position, t);
            brick.rotation = Quaternion.Slerp(startRotation, slot.Rotation, t);
            brick.localScale = Vector3.Lerp(startScale, slot.Size, t);

            yield return null;
        }

        brick.SetPositionAndRotation(slot.Position, slot.Rotation);
        brick.localScale = slot.Size;

        _crew.AddBrickCollider(brick.gameObject);
    }
}
