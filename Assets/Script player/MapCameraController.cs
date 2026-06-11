using UnityEngine;
using UnityEngine.InputSystem;

public class MapCameraController : MonoBehaviour
{
    [SerializeField] private bool useAzerty = true;
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 30f;
    [SerializeField] private float rotationSpeed = 90f;

    [Header("Zoom")]
    [SerializeField] private Camera cam;
    [SerializeField] private float zoomSpeed = 20f;
    [SerializeField] private float minZoom = 15f;
    [SerializeField] private float maxZoom = 80f;

    [Header("Edge Scroll")]
    [SerializeField] private float edgeSize = 15f;

    private CameraControls controls;

    private Vector2 moveInput;
    private Vector2 mousePos;
    private float zoomInput;

    private bool dragging;
  private Vector2 lastMousePos;


private float yaw;

    private void Awake()
    {
        controls = new CameraControls();
        Debug.Log("Using group: " + (useAzerty ? "azerty" : "qwerty"));
        Debug.Log("Binding mask: " + controls.bindingMask);
        if (useAzerty)
        {
          controls.bindingMask = InputBinding.MaskByGroup("azerty");
        }
        else
        {
          controls.bindingMask =InputBinding.MaskByGroup("qwerty");
        }
        controls.Camera.Move.performed += ctx => moveInput = ctx.ReadValue<Vector2>();
        controls.Camera.Move.canceled += ctx => moveInput = Vector2.zero;

        controls.Camera.MousePosition.performed += ctx =>
            mousePos = ctx.ReadValue<Vector2>();

        controls.Camera.Zoom.performed += ctx =>
            zoomInput = ctx.ReadValue<Vector2>().y;
            Debug.Log("Binding mask: " + controls.bindingMask);
    }

    private void OnEnable() => controls.Enable();
    private void OnDisable() => controls.Disable();

    private void Update()
    {
        HandleKeyboardMovement();
        HandleEdgeScroll();
        HandleZoom();
        HandleDrag();
        HandleRotate();
    }
    private void HandleDrag()
{
    if (Mouse.current.middleButton.wasPressedThisFrame)
    {
        dragging = true;
        lastMousePos = Mouse.current.position.ReadValue();
    }

    if (Mouse.current.middleButton.wasReleasedThisFrame)
    {
        dragging = false;
    }

    if (!dragging)
        return;

    Vector2 current = Mouse.current.position.ReadValue();
    Vector2 delta = current - lastMousePos;

    Vector3 move =
        (-transform.right * delta.x) +
        (-transform.forward * delta.y);

    move.y = 0;

    transform.position += move * 0.05f;

    lastMousePos = current;
}
    private void HandleKeyboardMovement()
    {
        Vector3 move =
            transform.right * moveInput.x +
            transform.forward * moveInput.y;

        move.y = 0;

        transform.position += move.normalized *
                              moveSpeed *
                              Time.deltaTime;
    }

    private void HandleEdgeScroll()
    {
        Vector3 move = Vector3.zero;

        if (mousePos.x < edgeSize)
            move -= transform.right;

        if (mousePos.x > Screen.width - edgeSize)
            move += transform.right;

        if (mousePos.y < edgeSize)
            move -= transform.forward;

        if (mousePos.y > Screen.height - edgeSize)
            move += transform.forward;

        move.y = 0;

        transform.position += move.normalized *
                              moveSpeed *
                              Time.deltaTime;
    }

    private void HandleZoom()
    {
        if (Mathf.Abs(zoomInput) < 0.01f)
            return;

        cam.fieldOfView -= zoomInput * zoomSpeed * Time.deltaTime;
        cam.fieldOfView = Mathf.Clamp(cam.fieldOfView, minZoom, maxZoom);

        zoomInput = 0;
    }

private void HandleRotate()
{
    float rotateInput = controls.Camera.Rotate.ReadValue<float>();

    if (Mathf.Abs(rotateInput) < 0.01f)
        return;

    Ray ray = cam.ViewportPointToRay(new Vector3(0.5f, 0.5f));

    if (Physics.Raycast(ray, out RaycastHit hit))
    {
        float angle = rotateInput * rotationSpeed * Time.deltaTime;

        transform.RotateAround(
            hit.point,
            Vector3.up,
            angle
        );
    }
}

}