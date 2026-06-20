using UnityEngine.EventSystems;

public interface IDraggablePanel
{
  void BeginDrag(PointerEventData e);
  void Drag(PointerEventData e);
}