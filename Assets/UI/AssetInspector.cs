using UnityEngine;
using UnityEngine.InputSystem;

public class AssetInspector : MonoBehaviour
{
    [SerializeField] private LayerMask clickMask;

    void Update()
    {
        if (Mouse.current == null) return;
        if (Mouse.current.leftButton.wasPressedThisFrame)
        {
            // Shift+Click is for wiring – don't open the inspector
            if (Keyboard.current != null && Keyboard.current.shiftKey.isPressed)
                return;

            RaycastHit hit = GetHitUnderMouse();
            if (hit.collider == null) return;

            var cp = hit.collider.GetComponent<ConnectionPoint>();
            if (cp != null)
            {
                OpenAssetPanel(cp.owner);
                return;
            }

            var node = hit.collider.GetComponent<ElectricalNode>();
            if (node != null)
            {
                OpenAssetPanel(node);
                return;
            }

            var line = hit.collider.GetComponent<PowerLine>();
            if (line != null)
            {
                OpenAssetPanel(line);
                return;
            }
        }
    }

    RaycastHit GetHitUnderMouse()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Physics.Raycast(ray, out RaycastHit hit, 500f, clickMask);
        return hit;
    }

    void OpenAssetPanel(object asset)
    {
        AssetPanel.Open(asset, Mouse.current.position.ReadValue());
    }
}