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
        [SerializeField] private bool disappearWhenUnlocked;
        [SerializeField] private GameObject[] connectionPieces = new GameObject[0];
        [SerializeField] private UnityEvent onUnlocked = new UnityEvent();

        public bool IsUnlocked => unlocked;
        public bool IsOpening => opening;
        public Vector3 ConnectionPoint => doorRenderer != null
            ? doorRenderer.bounds.center
            : blockingCollider != null ? blockingCollider.bounds.center : transform.position;

        private void Awake()
        {
            FindLocalReferences();
            ResetGate();
        }

        private void Reset() => FindLocalReferences();

        private void OnValidate() => FindLocalReferences();

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

        public void ConfigureConnectionPieces(GameObject[] pieces, bool disappearOnUnlock)
        {
            connectionPieces = pieces ?? new GameObject[0];
            disappearWhenUnlocked = disappearOnUnlock;
            ResetGate();
        }

        public void ResetGate()
        {
            unlocked = false;
            opening = false;
            SetConnectionGroupVisible(true);
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
            if (disappearWhenUnlocked)
                SetConnectionGroupVisible(false);
            else
            {
                ApplyMaterial(openMaterial);
                if (blockingCollider != null) blockingCollider.enabled = false;
            }
            onUnlocked.Invoke();
        }

        public void SetConnectionGroupVisible(bool visible)
        {
            SetPieceVisible(gameObject, visible);
            foreach (GameObject piece in connectionPieces)
                SetPieceVisible(piece, visible);
        }

        private static void SetPieceVisible(GameObject piece, bool visible)
        {
            if (piece == null) return;
            foreach (Renderer renderer in piece.GetComponentsInChildren<Renderer>(true))
                renderer.enabled = visible;
            foreach (Collider collider in piece.GetComponentsInChildren<Collider>(true))
                if (!collider.isTrigger) collider.enabled = visible;
        }

        private void ApplyMaterial(Material material)
        {
            if (doorRenderer == null || material == null) return;
            Material[] materials = doorRenderer.sharedMaterials;
            if (materialSlot >= materials.Length) return;
            materials[materialSlot] = material;
            doorRenderer.sharedMaterials = materials;
        }

        private void FindLocalReferences()
        {
            if (doorRenderer == null) doorRenderer = GetComponent<Renderer>();
            if (blockingCollider == null) blockingCollider = GetComponent<Collider>();
        }
    }
}
