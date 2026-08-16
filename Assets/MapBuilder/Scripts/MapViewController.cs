using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;

namespace MapBuilder
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public sealed class MapViewController : MonoBehaviour
    {
        [Header("Map")]
        [SerializeField] private MapGenerationController generationController;
        [SerializeField] private MapTilemapRenderer tilemapRenderer;

        [Header("Zoom")]
        [SerializeField, Min(0.0001f)] private float zoomSensitivity = 0.00625f;
        [SerializeField, Range(0f, 0.5f)] private float fitPadding = 0.05f;
        [SerializeField, Range(0.01f, 1f)] private float minimumZoomFactor = 0.08f;
        [SerializeField, Min(1f)] private float maximumZoomFactor = 2f;

        [Header("Movement limits")]
        [Tooltip("Minimum part of the map that remains visible at every edge.")]
        [SerializeField, Range(0.01f, 0.5f)] private float minimumVisibleMapFraction = 0.1f;

        private Camera viewCamera;
        private MapLayout observedLayout;
        private Bounds mapBounds;
        private float minimumOrthographicSize;
        private float maximumOrthographicSize;
        private bool hasMapBounds;
        private bool isDragging;
        private Vector2 previousPointerPosition;

        private void Awake()
        {
            viewCamera = GetComponent<Camera>();
            ResolveReferences();

            if (!viewCamera.orthographic)
            {
                Debug.LogError("MapViewController requires an orthographic camera.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            ResolveReferences();
            DetectNewMap();

            if (!hasMapBounds)
                return;

            HandleMouse();
            ConstrainPosition();
        }

        public void Configure(
            MapGenerationController controller,
            MapTilemapRenderer renderer)
        {
            generationController = controller;
            tilemapRenderer = renderer;
        }

        [ContextMenu("Focus Generated Map")]
        public void FocusGeneratedMap()
        {
            if (generationController == null || generationController.LastLayout == null)
                return;

            FocusLayout(generationController.LastLayout);
        }

        private void ResolveReferences()
        {
            if (generationController == null)
                generationController = FindAnyObjectByType<MapGenerationController>();

            if (tilemapRenderer == null && generationController != null)
                tilemapRenderer = generationController.GetComponent<MapTilemapRenderer>();
        }

        private void DetectNewMap()
        {
            if (generationController == null ||
                generationController.LastLayout == null ||
                ReferenceEquals(observedLayout, generationController.LastLayout))
            {
                return;
            }

            FocusLayout(generationController.LastLayout);
        }

        private void FocusLayout(MapLayout layout)
        {
            if (!TryCalculateMapBounds(layout, out mapBounds))
                return;

            observedLayout = layout;
            hasMapBounds = true;

            float aspect = Mathf.Max(0.01f, viewCamera.aspect);
            float verticalSize = mapBounds.size.y * 0.5f;
            float horizontalSize = mapBounds.size.x * 0.5f / aspect;
            float fitSize = Mathf.Max(verticalSize, horizontalSize) * (1f + fitPadding);

            minimumOrthographicSize = Mathf.Max(0.1f, fitSize * minimumZoomFactor);
            maximumOrthographicSize = Mathf.Max(minimumOrthographicSize, fitSize * maximumZoomFactor);
            viewCamera.orthographicSize = Mathf.Clamp(
                fitSize, minimumOrthographicSize, maximumOrthographicSize);

            Vector3 position = transform.position;
            position.x = mapBounds.center.x;
            position.y = mapBounds.center.y;
            transform.position = position;
            ConstrainPosition();
        }

        private bool TryCalculateMapBounds(MapLayout layout, out Bounds bounds)
        {
            bounds = default;
            if (layout == null || tilemapRenderer == null ||
                tilemapRenderer.GroundTilemap == null)
            {
                return false;
            }

            Tilemap tilemap = tilemapRenderer.GroundTilemap;
            Vector3 bottomLeft = tilemap.CellToWorld(Vector3Int.zero);
            Vector3 bottomRight = tilemap.CellToWorld(new Vector3Int(layout.Width, 0, 0));
            Vector3 topLeft = tilemap.CellToWorld(new Vector3Int(0, layout.Height, 0));
            Vector3 topRight = tilemap.CellToWorld(new Vector3Int(layout.Width, layout.Height, 0));

            bounds = new Bounds(bottomLeft, Vector3.zero);
            bounds.Encapsulate(bottomRight);
            bounds.Encapsulate(topLeft);
            bounds.Encapsulate(topRight);
            return bounds.size.x > Mathf.Epsilon && bounds.size.y > Mathf.Epsilon;
        }

        private void HandleMouse()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
                return;

            Vector2 pointerPosition = mouse.position.ReadValue();
            bool pointerOverUi = EventSystem.current != null &&
                EventSystem.current.IsPointerOverGameObject();

            if (!pointerOverUi)
                HandleZoom(mouse.scroll.ReadValue().y, pointerPosition);

            bool leftPressed = mouse.leftButton.isPressed;
            bool rightPressed = mouse.rightButton.isPressed;
            bool dragPressedThisFrame = mouse.leftButton.wasPressedThisFrame ||
                mouse.rightButton.wasPressedThisFrame;

            if (dragPressedThisFrame && !pointerOverUi)
            {
                isDragging = true;
                previousPointerPosition = pointerPosition;
            }

            if (isDragging && (leftPressed || rightPressed))
            {
                Pan(previousPointerPosition, pointerPosition);
                previousPointerPosition = pointerPosition;
            }
            else if (!leftPressed && !rightPressed)
            {
                isDragging = false;
            }
        }

        private void HandleZoom(float scrollDelta, Vector2 pointerPosition)
        {
            if (Mathf.Approximately(scrollDelta, 0f))
                return;

            Vector3 worldBeforeZoom;
            bool hasWorldPoint = TryGetPointerWorldPosition(pointerPosition, out worldBeforeZoom);

            float zoomMultiplier = Mathf.Exp(-scrollDelta * zoomSensitivity);
            viewCamera.orthographicSize = Mathf.Clamp(
                viewCamera.orthographicSize * zoomMultiplier,
                minimumOrthographicSize,
                maximumOrthographicSize);

            Vector3 worldAfterZoom;
            if (hasWorldPoint && TryGetPointerWorldPosition(pointerPosition, out worldAfterZoom))
            {
                Vector3 position = transform.position + (worldBeforeZoom - worldAfterZoom);
                position.z = transform.position.z;
                transform.position = position;
            }
        }

        private void Pan(Vector2 previousScreenPosition, Vector2 currentScreenPosition)
        {
            Vector3 previousWorldPosition;
            Vector3 currentWorldPosition;
            if (!TryGetPointerWorldPosition(previousScreenPosition, out previousWorldPosition) ||
                !TryGetPointerWorldPosition(currentScreenPosition, out currentWorldPosition))
            {
                return;
            }

            Vector3 position = transform.position +
                (previousWorldPosition - currentWorldPosition);
            position.z = transform.position.z;
            transform.position = position;
        }

        private bool TryGetPointerWorldPosition(Vector2 screenPosition, out Vector3 worldPosition)
        {
            Plane mapPlane = new Plane(Vector3.forward, mapBounds.center);
            Ray ray = viewCamera.ScreenPointToRay(screenPosition);
            float distance;
            if (mapPlane.Raycast(ray, out distance))
            {
                worldPosition = ray.GetPoint(distance);
                return true;
            }

            worldPosition = default;
            return false;
        }

        private void ConstrainPosition()
        {
            if (!hasMapBounds)
                return;

            float halfHeight = viewCamera.orthographicSize;
            float halfWidth = halfHeight * Mathf.Max(0.01f, viewCamera.aspect);
            float visibleX = Mathf.Min(
                mapBounds.size.x * minimumVisibleMapFraction,
                halfWidth);
            float visibleY = Mathf.Min(
                mapBounds.size.y * minimumVisibleMapFraction,
                halfHeight);

            Vector3 position = transform.position;
            position.x = Mathf.Clamp(
                position.x,
                mapBounds.min.x - halfWidth + visibleX,
                mapBounds.max.x + halfWidth - visibleX);
            position.y = Mathf.Clamp(
                position.y,
                mapBounds.min.y - halfHeight + visibleY,
                mapBounds.max.y + halfHeight - visibleY);
            transform.position = position;
        }
    }
}
