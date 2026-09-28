using UnityEngine;

public class PortalTeleport : MonoBehaviour
{
    [SerializeField] private Transform portalRoot;        // этот портал
    [SerializeField] private Transform exitPoint;         // ExitPoint этого портала
    [SerializeField] private PortalTeleport otherPortal;  // TriggerZone парного портала
    [SerializeField] private Transform player;

    private bool canTeleport = true;

    private void OnTriggerEnter(Collider other)
    {
        if (!canTeleport) return;
        if (other.transform != player && !other.transform.IsChildOf(player)) return;

        Teleport();
    }

    private void Teleport()
    {
        // 1. позиция игрока в системе координат ЭТОГО портала
        Vector3 local = portalRoot.InverseTransformPoint(player.position);

        // 2. выходим у ПАРНОГО портала: X зеркалим, Y сохраняем, вперёд берём от его ExitPoint
        Vector3 newPos = otherPortal.exitPoint.TransformPoint(new Vector3(-local.x, local.y, 0f));

        // 3. поворот: разворачиваем на 180° относительно парного портала
        Quaternion newRot =
            otherPortal.portalRoot.rotation *
            Quaternion.Euler(0f, 180f, 0f) *
            Quaternion.Inverse(portalRoot.rotation) *
            player.rotation;

        // 4. переносим (CharacterController на время выключаем)
        var cc = player.GetComponent<CharacterController>();
        if (cc) cc.enabled = false;

        player.SetPositionAndRotation(newPos, newRot);

        if (cc) cc.enabled = true;

        // 5. блокируем обратный телепорт на короткое время
        otherPortal.Lock();
        Lock();
    }

    public void Lock()
    {
        canTeleport = false;
        CancelInvoke(nameof(Unlock));
        Invoke(nameof(Unlock), 0.5f);
    }

    private void Unlock() => canTeleport = true;
}