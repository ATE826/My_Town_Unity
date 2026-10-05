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

    [Header("Crouch")]
    [SerializeField] private float _crouchHeight = 1f;
    [SerializeField] private float _crouchSpeed = 3.5f;
    [SerializeField] private float _crouchTransitionSpeed = 6f;
    [SerializeField] private LayerMask _ceilingMask = ~0;

    [Header("Grab")]
    [SerializeField] private float _grabDistance = 4f;
    [SerializeField] private float _minHoldDistance = 1.5f;
    [SerializeField] private float _maxHoldDistance = 6f;
    [SerializeField] private float _scrollStep = 0.5f;
    [SerializeField] private float _breakDistance = 3f;
    [SerializeField] private LayerMask _grabMask = ~0;

    [Header("Interact")]
    [SerializeField] private float _interactDistance = 3f;
    [SerializeField] private LayerMask _interactMask = ~0;

    private CharacterController _characterController;
    private Camera _playerCamera;

    private Vector3 _velocity;
    private Vector2 _rotation;
    private Vector2 _direction;

    private PickableObject _heldObject;
    private float _holdDistance;

    // ---------- Crouch ----------
    private float _standingHeight;
    private Vector3 _standingCenter;
    private float _standingCameraY;
    private bool _isCrouching;

    void Start()
    {
        _characterController = GetComponent<CharacterController>();
        _playerCamera = GetComponentInChildren<Camera>();

        // Запоминаем исходные параметры Character Controller и камеры
        _standingHeight = _characterController.height;
        _standingCenter = _characterController.center;
        _standingCameraY = _playerCamera.transform.localPosition.y;

        Cursor.lockState = CursorLockMode.Locked;
    }

    void Update()
    {
        if (Mouse.current == null || Keyboard.current == null)
        {
            return;
        }

        // =====================================================
        // ДВИЖЕНИЕ
        // =====================================================

        float horizontal = 0f;
        float vertical = 0f;

        if (Keyboard.current.dKey.isPressed)
            horizontal = 1f;
        else if (Keyboard.current.aKey.isPressed)
            horizontal = -1f;

        if (Keyboard.current.wKey.isPressed)
            vertical = 1f;
        else if (Keyboard.current.sKey.isPressed)
            vertical = -1f;

        _direction = new Vector2(horizontal, vertical);

        // =====================================================
        // ПРИСЕДАНИЕ
        // =====================================================

        UpdateCrouch(Keyboard.current.ctrlKey.isPressed);

        // =====================================================
        // ПРЫЖОК И ГРАВИТАЦИЯ
        // =====================================================

        if (_characterController.isGrounded)
        {
            // Не даём персонажу накапливать отрицательную скорость
            if (_velocity.y < 0)
            {
                _velocity.y = -2f;
            }

            // Прыжок (в приседе не прыгаем)
            if (!_isCrouching &&
                Keyboard.current.spaceKey.wasPressedThisFrame)
            {
                _velocity.y = _jumpForce;
            }
        }
        else
        {
            // Гравитация
            _velocity.y += _gravity * Time.deltaTime;
        }

        // =====================================================
        // ПОВОРОТ КАМЕРЫ
        // =====================================================

        Vector2 mouseDelta = Mouse.current.delta.ReadValue();

        mouseDelta *= _rotateSpeed * Time.deltaTime * 0.1f;

        _rotation.y += mouseDelta.x;
        _rotation.x = Mathf.Clamp(
            _rotation.x - mouseDelta.y,
            -90,
            90
        );

        _playerCamera.transform.localEulerAngles = _rotation;

        // =====================================================
        // СКОРОСТЬ
        // =====================================================

        float speed;

        if (_isCrouching)
        {
            speed = _crouchSpeed;
        }
        else
        {
            speed = Keyboard.current.leftShiftKey.isPressed
                ? _runSpeed
                : _walkSpeed;
        }

        Vector2 speedDirection = _direction * speed;

        Vector3 move =
            Quaternion.Euler(
                0,
                _playerCamera.transform.eulerAngles.y,
                0
            ) *
            new Vector3(
                speedDirection.x,
                0,
                speedDirection.y
            );

        // Сохраняем вертикальную скорость прыжка/гравитации
        _velocity.x = move.x;
        _velocity.z = move.z;

        // =====================================================
        // ПЕРЕМЕЩЕНИЕ ПЕРСОНАЖА
        // =====================================================

        _characterController.Move(
            _velocity * Time.deltaTime
        );

        // =====================================================
        // ВЗАИМОДЕЙСТВИЕ
        // =====================================================

        HandleGrab();
        HandleInteract();

        // Движение предмета в руках
        MoveHeldObject();
    }

    // =========================================================
    // ПРИСЕДАНИЕ
    // =========================================================

    private void UpdateCrouch(bool wantsCrouch)
    {
        if (wantsCrouch)
        {
            _isCrouching = true;
        }
        else if (_isCrouching && CanStandUp())
        {
            // Встаём, только если над головой есть место
            _isCrouching = false;
        }

        float targetHeight = _isCrouching ? _crouchHeight : _standingHeight;

        float height = Mathf.MoveTowards(
            _characterController.height,
            targetHeight,
            _crouchTransitionSpeed * Time.deltaTime
        );

        if (Mathf.Approximately(height, _characterController.height))
        {
            return;
        }

        // На сколько капсула ниже, чем в полный рост
        float diff = _standingHeight - height;

        _characterController.height = height;

        // Низ капсулы остаётся на месте, опускается только верх
        _characterController.center = new Vector3(
            _standingCenter.x,
            _standingCenter.y - diff / 2f,
            _standingCenter.z
        );

        // Камера опускается вместе с верхом капсулы
        Vector3 camPos = _playerCamera.transform.localPosition;
        camPos.y = _standingCameraY - diff;
        _playerCamera.transform.localPosition = camPos;
    }

    private bool CanStandUp()
    {
        float radius = _characterController.radius;
        float currentHeight = _characterController.height;

        // Низ капсулы в мировых координатах
        Vector3 bottom =
            transform.TransformPoint(_characterController.center) -
            Vector3.up * (currentHeight / 2f);

        // Центр верхней сферы текущей капсулы
        Vector3 origin = bottom + Vector3.up * (currentHeight - radius);

        // Сколько нужно свободного места, чтобы выпрямиться
        float distance = _standingHeight - currentHeight;

        if (distance <= 0.001f)
        {
            return true;
        }

        return !Physics.SphereCast(
            origin,
            radius * 0.95f,
            Vector3.up,
            out _,
            distance,
            _ceilingMask,
            QueryTriggerInteraction.Ignore
        );
    }

    // =========================================================
    // ВЗАИМОДЕЙСТВИЕ (E)
    // =========================================================

    private void HandleInteract()
    {
        // С предметом в руках не взаимодействуем
        if (_heldObject != null ||
            !Keyboard.current.eKey.wasPressedThisFrame)
        {
            return;
        }

        Ray ray = new Ray(
            _playerCamera.transform.position,
            _playerCamera.transform.forward
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            _interactDistance,
            _interactMask,
            QueryTriggerInteraction.Ignore))
        {
            IInteractable interactable =
                hit.collider.GetComponentInParent<IInteractable>();

            interactable?.Interact();
        }
    }

    // =========================================================
    // ЗАХВАТ ПРЕДМЕТОВ
    // =========================================================

    private void HandleGrab()
    {
        // ПКМ нажата — пробуем схватить
        if (Mouse.current.rightButton.wasPressedThisFrame &&
            _heldObject == null)
        {
            TryGrab();
        }

        // ПКМ отпущена — отпускаем
        if (_heldObject != null &&
            !Mouse.current.rightButton.isPressed)
        {
            ReleaseObject();
            return;
        }

        // Колёсико — приближаем / отдаляем
        if (_heldObject != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;

            if (scroll != 0f)
            {
                _holdDistance +=
                    Mathf.Sign(scroll) * _scrollStep;

                _holdDistance = Mathf.Clamp(
                    _holdDistance,
                    _minHoldDistance,
                    _maxHoldDistance
                );
            }
        }
    }

    private void TryGrab()
    {
        Ray ray = new Ray(
            _playerCamera.transform.position,
            _playerCamera.transform.forward
        );

        if (Physics.Raycast(
            ray,
            out RaycastHit hit,
            _grabDistance,
            _grabMask,
            QueryTriggerInteraction.Ignore))
        {
            PickableObject pickable =
                hit.collider.GetComponentInParent<PickableObject>();

            if (pickable != null)
            {
                _heldObject = pickable;

                _holdDistance = Mathf.Max(
                    hit.distance,
                    _minHoldDistance
                );

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

        Vector3 holdPoint =
            _playerCamera.transform.position +
            _playerCamera.transform.forward *
            _holdDistance;

        // Предмет застрял / слишком далеко
        if (Vector3.Distance(
            _heldObject.Position,
            holdPoint) > _breakDistance)
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