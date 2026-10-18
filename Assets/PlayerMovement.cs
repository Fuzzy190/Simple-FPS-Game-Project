using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class PlayerMovement : MonoBehaviour
{
    public enum MovementState
    {
        Walking,
        Sprinting,
        Crouching,
        Proning,
        Sliding,
        Air
    }

    [Header("State & References (Auto-Filled on Play!)")]
    public MovementState state;
    public Transform orientation;
    public Transform cameraHolder;
    public Camera playerCamera;

    [Header("Look Sensitivity (MnK & PS4)")]
    public float mouseSensitivity = 0.15f;
    public float controllerSensitivity = 140f;
    private float xRotation;
    private float yRotation;

    [Header("Movement Speeds")]
    public float walkSpeed = 8f;
    public float sprintSpeed = 13f;
    public float crouchSpeed = 4.5f;
    public float proneSpeed = 2.2f;
    public float slideSpeed = 19f;
    private float moveSpeed;
    private float desiredMoveSpeed;
    private float lastDesiredMoveSpeed;

    [Header("Knife Speed Multiplier")]
    [HideInInspector] public float weaponSpeedMultiplier = 1f;

    [Header("Karlson / Titanfall Physics & Drag")]
    public float groundDrag = 6f;
    public float airMultiplier = 0.45f;
    public float speedIncreaseMultiplier = 1.8f;
    public float slopeIncreaseMultiplier = 2.5f;
    public float maxSlopeAngle = 45f;

    [Header("Jumping")]
    public float jumpForce = 11f;
    public float jumpCooldown = 0.2f;
    private bool readyToJump = true;

    [Header("Stance Heights (Stand / Crouch / Prone)")]
    public float standHeight = 2.0f;
    public float crouchHeight = 1.1f;
    public float proneHeight = 0.55f;
    public float standCamY = 0.7f;
    public float crouchCamY = 0.25f;
    public float proneCamY = -0.35f;
    public float stanceTransitionSpeed = 12f;

    [Header("Sliding")]
    public float maxSlideTime = 1.1f;
    public float slideForce = 220f;
    private float slideTimer;
    private bool isSliding;

    [Header("Flashy Camera Tilt & FOV")]
    public float baseFov = 85f;
    public float sprintFov = 95f;
    public float slideFov = 105f;
    public float maxStrafeTilt = 3.5f;
    public float slideTilt = 7f;
    private float currentTilt;

    [Header("Ground Detection")]
    public LayerMask groundMask = ~0;
    public bool grounded;
    private RaycastHit slopeHit;
    private bool exitingSlope;

    private Rigidbody rb;
    private CapsuleCollider col;

    private Vector2 moveInput;
    private Vector2 lookInput;
    private bool isSprintingInput;
    private bool isCrouching;
    private bool isProning;
    private float targetColliderHeight;
    private float targetCamY;

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        col = GetComponent<CapsuleCollider>();
        rb.freezeRotation = true;

        // AUTO-FIND Orientation, CameraHolder, and Main Camera so you don't have to drag them!
        if (orientation == null)
        {
            Transform foundOrientation = transform.Find("Orientation");
            if (foundOrientation != null)
            {
                orientation = foundOrientation;
            }
            else
            {
                GameObject newOri = new GameObject("Orientation");
                newOri.transform.SetParent(transform);
                newOri.transform.localPosition = Vector3.zero;
                orientation = newOri.transform;
            }
        }

        if (cameraHolder == null)
        {
            Transform foundCamHolder = transform.Find("CameraHolder");
            if (foundCamHolder != null)
            {
                cameraHolder = foundCamHolder;
            }
            else if (Camera.main != null)
            {
                cameraHolder = Camera.main.transform.parent != null ? Camera.main.transform.parent : Camera.main.transform;
            }
        }

        if (playerCamera == null)
        {
            playerCamera = GetComponentInChildren<Camera>();
            if (playerCamera == null) playerCamera = Camera.main;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        targetColliderHeight = standHeight;
        targetCamY = standCamY;
        moveSpeed = walkSpeed;
    }

    private void Update()
    {
        float currentHalfHeight = col.height * 0.5f;
        grounded = Physics.SphereCast(transform.position, 0.35f, Vector3.down, out _, currentHalfHeight + 0.15f, groundMask);

        ReadDualInput();
        HandleLookAndFlashyCamera();
        StateHandler();
        SpeedControl();
        SmoothStanceTransitions();

        rb.linearDamping = (grounded && !isSliding) ? groundDrag : 0f;
    }

    private void FixedUpdate()
    {
        MovePlayer();
        if (isSliding)
        {
            SlidingMovement();
        }
    }

    private void ReadDualInput()
    {
        moveInput = Vector2.zero;
        lookInput = Vector2.zero;
        bool jumpPressed = false;
        bool crouchSlidePressed = false;
        bool crouchSlideReleased = false;
        bool pronePressed = false;

        if (Keyboard.current != null && Mouse.current != null)
        {
            float kx = (Keyboard.current.dKey.isPressed ? 1f : 0f) - (Keyboard.current.aKey.isPressed ? 1f : 0f);
            float ky = (Keyboard.current.wKey.isPressed ? 1f : 0f) - (Keyboard.current.sKey.isPressed ? 1f : 0f);
            moveInput = new Vector2(kx, ky);
            if (moveInput.sqrMagnitude > 1f) moveInput.Normalize();

            lookInput = Mouse.current.delta.ReadValue() * mouseSensitivity;

            if (Keyboard.current.leftShiftKey.isPressed) isSprintingInput = true;
            else if (Gamepad.current == null || !Gamepad.current.leftStickButton.isPressed) isSprintingInput = false;

            jumpPressed |= Keyboard.current.spaceKey.wasPressedThisFrame;
            crouchSlidePressed |= Keyboard.current.leftCtrlKey.wasPressedThisFrame || Keyboard.current.cKey.wasPressedThisFrame;
            crouchSlideReleased |= Keyboard.current.leftCtrlKey.wasReleasedThisFrame || Keyboard.current.cKey.wasReleasedThisFrame;
            pronePressed |= Keyboard.current.zKey.wasPressedThisFrame;
        }

        if (Gamepad.current != null)
        {
            Vector2 stickMove = Gamepad.current.leftStick.ReadValue();
            if (stickMove.sqrMagnitude > 0.04f)
                moveInput = stickMove;

            Vector2 stickLook = Gamepad.current.rightStick.ReadValue();
            if (stickLook.sqrMagnitude > 0.04f)
                lookInput += stickLook * controllerSensitivity * Time.deltaTime;

            if (Gamepad.current.leftStickButton.wasPressedThisFrame)
                isSprintingInput = !isSprintingInput;
            if (moveInput.sqrMagnitude < 0.05f)
                isSprintingInput = false;

            jumpPressed |= Gamepad.current.buttonSouth.wasPressedThisFrame;
            crouchSlidePressed |= Gamepad.current.buttonEast.wasPressedThisFrame;
            crouchSlideReleased |= Gamepad.current.buttonEast.wasReleasedThisFrame;
            pronePressed |= Gamepad.current.dpad.down.wasPressedThisFrame;
        }

        if (jumpPressed && readyToJump && grounded)
        {
            if (isProning || isCrouching)
            {
                SetStance(false, false);
            }
            else
            {
                readyToJump = false;
                Jump();
                Invoke(nameof(ResetJump), jumpCooldown);
            }
        }

        if (crouchSlidePressed)
        {
            isProning = false;
            bool hasForwardMomentum = moveInput.sqrMagnitude > 0.1f && (isSprintingInput || rb.linearVelocity.magnitude > walkSpeed + 1f);

            if (hasForwardMomentum && grounded)
            {
                StartSlide();
            }
            else
            {
                SetStance(!isCrouching, false);
            }
        }

        if (crouchSlideReleased && isSliding)
        {
            StopSlide();
        }

        if (pronePressed)
        {
            if (isSliding) StopSlide();
            SetStance(false, !isProning);
        }
    }

    private void SetStance(bool crouch, bool prone)
    {
        if (!crouch && !prone && (isCrouching || isProning))
        {
            if (Physics.Raycast(transform.position, Vector3.up, standHeight * 0.6f, groundMask))
            {
                crouch = true;
            }
        }

        isCrouching = crouch;
        isProning = prone;

        if (isProning)
        {
            targetColliderHeight = proneHeight;
            targetCamY = proneCamY;
        }
        else if (isCrouching)
        {
            targetColliderHeight = crouchHeight;
            targetCamY = crouchCamY;
        }
        else
        {
            targetColliderHeight = standHeight;
            targetCamY = standCamY;
        }
    }

    private void SmoothStanceTransitions()
    {
        col.height = Mathf.Lerp(col.height, targetColliderHeight, Time.deltaTime * stanceTransitionSpeed);

        if (cameraHolder != null)
        {
            Vector3 camPos = cameraHolder.localPosition;
            camPos.y = Mathf.Lerp(camPos.y, targetCamY, Time.deltaTime * stanceTransitionSpeed);
            cameraHolder.localPosition = camPos;
        }
    }

    private void HandleLookAndFlashyCamera()
    {
        yRotation += lookInput.x;
        xRotation -= lookInput.y;
        xRotation = Mathf.Clamp(xRotation, -88f, 88f);

        float targetTilt = -moveInput.x * maxStrafeTilt;
        if (isSliding) targetTilt += slideTilt;
        currentTilt = Mathf.Lerp(currentTilt, targetTilt, Time.deltaTime * 10f);

        if (cameraHolder != null)
            cameraHolder.localRotation = Quaternion.Euler(xRotation, yRotation, currentTilt);

        if (orientation != null)
            orientation.rotation = Quaternion.Euler(0f, yRotation, 0f);

        if (playerCamera != null)
        {
            float targetFov = baseFov;
            if (isSliding) targetFov = slideFov;
            else if (state == MovementState.Sprinting || weaponSpeedMultiplier > 1.05f) targetFov = sprintFov;

            playerCamera.fieldOfView = Mathf.Lerp(playerCamera.fieldOfView, targetFov, Time.deltaTime * 8f);
        }
    }

    private void StateHandler()
    {
        if (isSliding)
        {
            state = MovementState.Sliding;
            desiredMoveSpeed = OnSlope() && rb.linearVelocity.y < 0.1f ? slideSpeed * 1.4f : sprintSpeed;
        }
        else if (isProning)
        {
            state = MovementState.Proning;
            desiredMoveSpeed = proneSpeed;
        }
        else if (isCrouching)
        {
            state = MovementState.Crouching;
            desiredMoveSpeed = crouchSpeed;
        }
        else if (grounded && isSprintingInput)
        {
            state = MovementState.Sprinting;
            desiredMoveSpeed = sprintSpeed;
        }
        else if (grounded)
        {
            state = MovementState.Walking;
            desiredMoveSpeed = walkSpeed;
        }
        else
        {
            state = MovementState.Air;
        }

        float finalDesired = desiredMoveSpeed * weaponSpeedMultiplier;

        if (Mathf.Abs(finalDesired - lastDesiredMoveSpeed) > 4f && moveSpeed != 0f)
        {
            moveSpeed = Mathf.Lerp(moveSpeed, finalDesired, Time.deltaTime * speedIncreaseMultiplier);
        }
        else
        {
            moveSpeed = finalDesired;
        }

        lastDesiredMoveSpeed = finalDesired;
    }

    private void MovePlayer()
    {
        if (isSliding) return;

        Vector3 moveDirection = orientation.forward * moveInput.y + orientation.right * moveInput.x;

        if (OnSlope() && !exitingSlope)
        {
            rb.AddForce(GetSlopeMoveDirection(moveDirection) * (moveSpeed * 20f), ForceMode.Force);
            if (rb.linearVelocity.y > 0f)
                rb.AddForce(Vector3.down * 60f, ForceMode.Force);
        }
        else if (grounded)
        {
            rb.AddForce(moveDirection.normalized * (moveSpeed * 10f), ForceMode.Force);
        }
        else
        {
            rb.AddForce(moveDirection.normalized * (moveSpeed * 10f * airMultiplier), ForceMode.Force);
        }

        rb.useGravity = !OnSlope();
    }

    private void StartSlide()
    {
        isSliding = true;
        isCrouching = true;
        targetColliderHeight = crouchHeight;
        targetCamY = crouchCamY;
        slideTimer = maxSlideTime;

        rb.AddForce(Vector3.down * 8f, ForceMode.Impulse);
    }

    private void SlidingMovement()
    {
        Vector3 inputDirection = orientation.forward * moveInput.y + orientation.right * moveInput.x;
        if (inputDirection.sqrMagnitude < 0.05f) inputDirection = orientation.forward;

        if (!OnSlope() || rb.linearVelocity.y > -0.1f)
        {
            rb.AddForce(inputDirection.normalized * slideForce, ForceMode.Force);
            slideTimer -= Time.fixedDeltaTime;
        }
        else
        {
            rb.AddForce(GetSlopeMoveDirection(inputDirection) * (slideForce * slopeIncreaseMultiplier), ForceMode.Force);
        }

        if (slideTimer <= 0f)
        {
            StopSlide();
        }
    }

    private void StopSlide()
    {
        isSliding = false;
        SetStance(false, false);
    }

    private void SpeedControl()
    {
        if (OnSlope() && !exitingSlope)
        {
            if (rb.linearVelocity.magnitude > moveSpeed)
                rb.linearVelocity = rb.linearVelocity.normalized * moveSpeed;
        }
        else
        {
            Vector3 flatVel = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
            if (flatVel.magnitude > moveSpeed)
            {
                Vector3 limitedVel = flatVel.normalized * moveSpeed;
                rb.linearVelocity = new Vector3(limitedVel.x, rb.linearVelocity.y, limitedVel.z);
            }
        }
    }

    private void Jump()
    {
        exitingSlope = true;
        if (isSliding) StopSlide();

        rb.linearVelocity = new Vector3(rb.linearVelocity.x, 0f, rb.linearVelocity.z);
        rb.AddForce(transform.up * jumpForce, ForceMode.Impulse);
    }

    private void ResetJump()
    {
        readyToJump = true;
        exitingSlope = false;
    }

    public bool OnSlope()
    {
        float halfHeight = col.height * 0.5f;
        if (Physics.Raycast(transform.position, Vector3.down, out slopeHit, halfHeight + 0.35f, groundMask))
        {
            float angle = Vector3.Angle(Vector3.up, slopeHit.normal);
            return angle < maxSlopeAngle && angle != 0f;
        }
        return false;
    }

    public Vector3 GetSlopeMoveDirection(Vector3 direction)
    {
        return Vector3.ProjectOnPlane(direction, slopeHit.normal).normalized;
    }
}