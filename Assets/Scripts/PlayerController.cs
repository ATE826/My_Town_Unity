using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : MonoBehaviour
{
    [SerializeField] private float _walkSpeed = 7;
    [SerializeField] private float _runSpeed = 15;
    [SerializeField] private float _rotateSpeed = 75;
    [SerializeField] private float _jumpForce = 5;
    [SerializeField] private float _gravity = -9.81f;

    private CharacterController _characterController;
    private Camera _playerCamera;

    private Vector3 _velocity;
    private Vector2 _rotation;
    private Vector2 _direction;

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
    }
}