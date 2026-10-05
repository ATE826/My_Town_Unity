using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float _walkSpeed = 7;
    [SerializeField] private float _runSpeed = 15;
    [SerializeField] private float _rotateSpeed = 75;
    [SerializeField] private float _jumpForce = 5;
    [SerializeField] private float _gravity = -9.81f;

    [Header("Grab")]
    [SerializeField] private float _grabDistance = 4f;     // на каком расстоянии можно схватить предмет
    [SerializeField] private float _minHoldDistance = 1.5f; // ближе этого расстояния предмет не держим
    [SerializeField] private float _maxHoldDistance = 6f;  // дальше этого расстояния предмет не отодвигаем
    [SerializeField] private float _scrollStep = 0.5f;     // на сколько меняется дистанция за один "клик" колёсика
    [SerializeField] private float _breakDistance = 3f;    // если предмет застрял и отстал дальше - отпускаем
    [SerializeField] private LayerMask _grabMask = ~0;     // по каким слоям пускаем луч

    [Header("Interact")]
    [SerializeField] private float _interactDistance = 3f; // на каком расстоянии работает клавиша E
    [SerializeField] private LayerMask _interactMask = ~0;

    private CharacterController _characterController;
    private Camera _playerCamera;

    private Vector3 _velocity;
    private Vector2 _rotation;
    private Vector2 _direction;

    private PickableObject _heldObject;
    private float _holdDistance;

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _playerCamera = GetComponentInChildren<Camera>();
        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null)
        {
            return;
        }

        _characterController.Move(_velocity * Time.deltaTime);

        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.dKey.isPressed) horizontal = 1f;
        else if (Keyboard.current.aKey.isPressed) horizontal = -1f;

        if (Keyboard.current.wKey.isPressed) vertical = 1f;
        else if (Keyboard.current.sKey.isPressed) vertical = -1f;

        _direction = new Vector2(horizontal, vertical);

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        if (_characterController.isGrounded)
        {
            _velocity.y = Keyboard.current.spaceKey.wasPressedThisFrame ? _jumpForce : -0.1f;
        }
        else
        {
            _velocity.y += _gravity * Time.deltaTime;
        }

        mouseDelta *= _rotateSpeed * Time.deltaTime * 0.1f;

        _rotation.y += mouseDelta.x;
        _rotation.x = Mathf.Clamp(_rotation.x - mouseDelta.y, -90, 90);

        _playerCamera.transform.localEulerAngles = _rotation;

        HandleGrab();
        HandleInteract();
    }

    private void FixedUpdate()
    {
        if (Keyboard.current == null)
        {
            return;
        }

        float speed = Keyboard.current.leftShiftKey.isPressed ? _runSpeed : _walkSpeed;
        Vector2 speedDirection = _direction * speed;

        Vector3 move = Quaternion.Euler(0, _playerCamera.transform.eulerAngles.y, 0) * new Vector3(speedDirection.x, 0, speedDirection.y);
        _velocity = new Vector3(move.x, _velocity.y, move.z);

        MoveHeldObject();
    }

    // ---------- Взаимодействие (клавиша E) ----------

    private void HandleInteract()
    {
        // с предметом в руках не взаимодействуем: он перекрывает луч
        if (_heldObject != null || !Keyboard.current.eKey.wasPressedThisFrame)
        {
            return;
        }

        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, _interactDistance, _interactMask, QueryTriggerInteraction.Ignore))
        {
            IInteractable interactable = hit.collider.GetComponentInParent<IInteractable>();
            interactable?.Interact();
        }
    }

    // ---------- Захват предметов ----------

    private void HandleGrab()
    {
        // ПКМ нажата - пробуем схватить
        if (Mouse.current.rightButton.wasPressedThisFrame && _heldObject == null)
        {
            TryGrab();
        }

        // ПКМ не зажата - отпускаем (предмет полетит по инерции и упадёт)
        if (_heldObject != null && !Mouse.current.rightButton.isPressed)
        {
            ReleaseObject();
            return;
        }

        // Колёсико мыши - приближаем / отдаляем предмет
        if (_heldObject != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (scroll != 0f)
            {
                _holdDistance += Mathf.Sign(scroll) * _scrollStep;
                _holdDistance = Mathf.Clamp(_holdDistance, _minHoldDistance, _maxHoldDistance);
            }
        }
    }

    private void TryGrab()
    {
        Ray ray = new Ray(_playerCamera.transform.position, _playerCamera.transform.forward);

        if (Physics.Raycast(ray, out RaycastHit hit, _grabDistance, _grabMask, QueryTriggerInteraction.Ignore))
        {
            PickableObject pickable = hit.collider.GetComponentInParent<PickableObject>();

            if (pickable != null)
            {
                _heldObject = pickable;
                _holdDistance = Mathf.Max(hit.distance, _minHoldDistance);
                _heldObject.Grab(_characterController);
            }
        }
    }

    private void MoveHeldObject()
    {
        if (_heldObject == null)
        {
            return;
        }

        Vector3 holdPoint = _playerCamera.transform.position + _playerCamera.transform.forward * _holdDistance;

        // предмет упёрся в стену / застрял - отпускаем
        if (Vector3.Distance(_heldObject.Position, holdPoint) > _breakDistance)
        {
            ReleaseObject();
            return;
        }

        _heldObject.MoveTo(holdPoint);
    }

    private void ReleaseObject()
    {
        _heldObject.Release(_characterController);
        _heldObject = null;
    }
}