using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace Inspection.AR
{
    /// <summary>
    /// 平面边界线可视化：ARPlane 边界（平面本地 XZ 二维多边形）变化时刷新 LineRenderer。
    /// 与 ARPlaneMeshVisualizer（表面网格）配合构成 PlaneVisualizer.prefab。
    /// </summary>
    [RequireComponent(typeof(ARPlane))]
    public class PlaneBoundaryVisualizer : MonoBehaviour
    {
        [SerializeField] private LineRenderer lineRenderer;

        private ARPlane plane;
        private static readonly List<Vector3> Vertices = new List<Vector3>();

        private void OnEnable()
        {
            plane = GetComponent<ARPlane>();
            if (plane != null)
            {
                plane.boundaryChanged += OnBoundaryChanged;
            }
            Refresh();
        }

        private void OnDisable()
        {
            if (plane != null)
            {
                plane.boundaryChanged -= OnBoundaryChanged;
            }
        }

        private void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs _)
        {
            Refresh();
        }

        private void Refresh()
        {
            if (plane == null || lineRenderer == null)
            {
                return;
            }
            var boundary = plane.boundary; // NativeArray<Vector2>（ARF 6），平面本地 XZ 二维轮廓
            if (boundary.Length < 3)
            {
                lineRenderer.positionCount = 0;
                return;
            }
            Vertices.Clear();
            for (var i = 0; i < boundary.Length; i++)
            {
                var point = boundary[i];
                Vertices.Add(new Vector3(point.x, 0f, point.y));
            }
            lineRenderer.positionCount = Vertices.Count;
            lineRenderer.SetPositions(Vertices.ToArray());
        }
    }
}
