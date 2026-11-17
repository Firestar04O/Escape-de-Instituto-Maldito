using UnityEngine;
using UnityEngine.Events;

namespace SchoolEscape.Rooms
{
    [DisallowMultipleComponent]
    public sealed class RoomExitGate : MonoBehaviour
    {
        [SerializeField] private bool unlocked;
        [SerializeField] private bool opening;
        [SerializeField] private Renderer doorRenderer;
        [SerializeField] private int materialSlot = 1;
        [SerializeField] private Material closedMaterial;
        [SerializeField] private Material openMaterial;
        [SerializeField] private Collider blockingCollider;
        [SerializeField] private UnityEvent onUnlocked = new UnityEvent();

        public bool IsUnlocked => unlocked;
        public bool IsOpening => opening;

        private void Awake()
        {
            if (doorRenderer == null) doorRenderer = GetComponent<Renderer>();
            if (blockingCollider == null) blockingCollider = GetComponent<Collider>();
        }

        public void ConfigureVisuals(
            Renderer targetRenderer,
            int targetMaterialSlot,
            Material closed,
            Material open,
            Collider blocker)
        {
            doorRenderer = targetRenderer;
            materialSlot = Mathf.Max(0, targetMaterialSlot);
            closedMaterial = closed;
            openMaterial = open;
            blockingCollider = blocker;
            ResetGate();
        }

        public void ResetGate()
        {
            unlocked = false;
            opening = false;
            if (blockingCollider != null) blockingCollider.enabled = true;
            ApplyMaterial(closedMaterial);
        }

        public void BeginOpening()
        {
            if (unlocked) return;
            opening = true;
        }

        public void Unlock()
        {
            if (unlocked) return;
            opening = false;
            unlocked = true;
            ApplyMaterial(openMaterial);
            if (blockingCollider != null) blockingCollider.enabled = false;
            onUnlocked.Invoke();
        }

        private void ApplyMaterial(Material material)
        {
            if (doorRenderer == null || material == null) return;
            Material[] materials = doorRenderer.sharedMaterials;
            if (materialSlot >= materials.Length) return;
            materials[materialSlot] = material;
            doorRenderer.sharedMaterials = materials;
        }
    }
}
