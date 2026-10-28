using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace Inspection.AR
{
    /// <summary>
    /// 开发构建的放置诊断：成功后等帧末将屏幕存为 PNG，并把同一帧的点击、
    /// 相机、平面边界与标记投影存为 JSON。文件只存本机，不上传、不自动清理。
    /// 屏幕坐标统一采用 Unity 触摸约定：原点在左下角，单位像素。
    /// </summary>
    public class PlacementDiagnostics : MonoBehaviour
    {
        private const string DirectoryName = "PlacementDiagnostics";
        private static readonly TimeSpan BeijingOffset = TimeSpan.FromHours(8);
        private int sequence;

        public static string OutputDirectory => Path.Combine(Application.persistentDataPath, DirectoryName);

        public void RecordPlacement(Vector2 touchPosition, ARRaycastHit hit, ARAnchor anchor, Transform marker)
        {
#if DEVELOPMENT_BUILD
            // 计划 006 4.8：成功放置的实景截图与坐标采集同样受「完整诊断」运行时开关门控，
            // 默认关闭不新增任何诊断文件；已有历史文件不自动删除
            if (!DiagnosticsConfig.FullEnabled)
            {
                return;
            }
            if (marker == null)
            {
                return;
            }
            StartCoroutine(CaptureAtEndOfFrame(new PendingCapture
            {
                touchPosition = touchPosition,
                hitPosition = hit.pose.position,
                hitRotation = hit.pose.rotation,
                hitDistance = hit.distance,
                hitPlaneId = hit.trackableId.ToString(),
                anchor = anchor,
                marker = marker,
                placementFrame = Time.frameCount,
            }));
#endif
        }

#if DEVELOPMENT_BUILD
        private sealed class PendingCapture
        {
            public Vector2 touchPosition;
            public Vector3 hitPosition;
            public Quaternion hitRotation;
            public float hitDistance;
            public string hitPlaneId;
            public ARAnchor anchor;
            public Transform marker;
            public int placementFrame;
        }

        private IEnumerator CaptureAtEndOfFrame(PendingCapture pending)
        {
            // 同一帧渲染结束再读纹理与投影，避免截到旧方块或旧相机姿态。
            yield return new WaitForEndOfFrame();

            var camera = Camera.main;
            if (camera == null || pending.marker == null)
            {
                Debug.LogWarning("[PlacementDiagnostics] 截图取消：主相机或标记已不存在。");
                yield break;
            }

            Texture2D screenshot = null;
            var id = DateTimeOffset.UtcNow.ToOffset(BeijingOffset).ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture)
                + "-" + (++sequence).ToString("D3", CultureInfo.InvariantCulture);
            var baseName = "placement-" + id;
            try
            {
                Directory.CreateDirectory(OutputDirectory);
                screenshot = ScreenCapture.CaptureScreenshotAsTexture();
                var pngPath = Path.Combine(OutputDirectory, baseName + ".png");
                File.WriteAllBytes(pngPath, screenshot.EncodeToPNG());

                var record = BuildRecord(pending, camera, baseName + ".png");
                var jsonPath = Path.Combine(OutputDirectory, baseName + ".json");
                File.WriteAllText(jsonPath, JsonUtility.ToJson(record, true), new UTF8Encoding(false));
                Debug.Log($"[PlacementDiagnostics] 已保存 {baseName}.png/json：{OutputDirectory}");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[PlacementDiagnostics] 诊断保存失败（不影响放置）：{exception.Message}");
            }
            finally
            {
                if (screenshot != null)
                {
                    Destroy(screenshot);
                }
            }
        }

        private static PlacementDiagnosticRecord BuildRecord(PendingCapture pending, Camera camera, string screenshotFile)
        {
            var planeManager = FindFirstObjectByType<ARPlaneManager>();
            var cameraManager = camera.GetComponent<ARCameraManager>();
            var markerPosition = pending.marker.position;
            var markerScreen = ProjectToScreen(markerPosition, camera);
            var hitScreen = ProjectToScreen(pending.hitPosition, camera);
            var record = new PlacementDiagnosticRecord
            {
                schemaVersion = 1,
                capturedAt = DateTimeOffset.UtcNow.ToOffset(BeijingOffset).ToString("O"),
                placementFrame = pending.placementFrame,
                captureFrame = Time.frameCount,
                screenshotFile = screenshotFile,
                screen = new ScreenDiagnostic
                {
                    width = Screen.width,
                    height = Screen.height,
                    orientation = Screen.orientation.ToString(),
                    touchPixels = VectorData.From(pending.touchPosition),
                    touchNormalized = VectorData.From(new Vector2(
                        pending.touchPosition.x / Screen.width, pending.touchPosition.y / Screen.height)),
                },
                session = ARSession.state.ToString(),
                notTrackingReason = ARSession.notTrackingReason.ToString(),
                camera = BuildCamera(camera, cameraManager),
                hit = new HitDiagnostic
                {
                    planeId = pending.hitPlaneId,
                    worldPosition = VectorData.From(pending.hitPosition),
                    worldRotation = VectorData.From(pending.hitRotation),
                    distanceFromCamera = pending.hitDistance,
                    screenPixels = VectorData.From(hitScreen),
                    screenOffsetFromTouch = VectorData.From(hitScreen - (Vector3)pending.touchPosition),
                },
                marker = new MarkerDiagnostic
                {
                    worldPosition = VectorData.From(markerPosition),
                    worldRotation = VectorData.From(pending.marker.rotation),
                    worldScale = VectorData.From(pending.marker.lossyScale),
                    anchorId = pending.anchor != null ? pending.anchor.trackableId.ToString() : "",
                    screenPixels = VectorData.From(markerScreen),
                    screenOffsetFromTouch = VectorData.From(markerScreen - (Vector3)pending.touchPosition),
                    pixelDistanceFromTouch = Vector2.Distance(markerScreen, pending.touchPosition),
                    distanceFromCamera = Vector3.Distance(camera.transform.position, markerPosition),
                    onScreen = IsOnScreen(markerScreen),
                },
            };
            var renderer = pending.marker.GetComponentInChildren<Renderer>();
            record.marker.rendererVisible = renderer != null && renderer.isVisible;

            if (planeManager != null)
            {
                foreach (var plane in planeManager.trackables)
                {
                    record.planes.Add(BuildPlane(plane, pending.hitPlaneId, camera));
                }
            }
            return record;
        }

        private static CameraDiagnostic BuildCamera(Camera camera, ARCameraManager manager)
        {
            var result = new CameraDiagnostic
            {
                worldPosition = VectorData.From(camera.transform.position),
                worldRotation = VectorData.From(camera.transform.rotation),
                forward = VectorData.From(camera.transform.forward),
                fieldOfView = camera.fieldOfView,
                nearClipPlane = camera.nearClipPlane,
                farClipPlane = camera.farClipPlane,
                projectionMatrix = MatrixValues(camera.projectionMatrix),
                worldToCameraMatrix = MatrixValues(camera.worldToCameraMatrix),
                graphicsDevice = SystemInfo.graphicsDeviceType + " / " + SystemInfo.graphicsDeviceName,
                deviceModel = SystemInfo.deviceModel,
            };
            if (manager == null)
            {
                return result;
            }
            result.intrinsicsAvailable = manager.TryGetIntrinsics(out var intrinsics);
            if (result.intrinsicsAvailable)
            {
                result.focalLength = VectorData.From(intrinsics.focalLength);
                result.principalPoint = VectorData.From(intrinsics.principalPoint);
                result.imageWidth = intrinsics.resolution.x;
                result.imageHeight = intrinsics.resolution.y;
            }
            var configuration = manager.currentConfiguration;
            if (configuration.HasValue)
            {
                result.cameraImageWidth = configuration.Value.width;
                result.cameraImageHeight = configuration.Value.height;
                result.cameraFramerate = configuration.Value.framerate ?? 0;
            }
            return result;
        }

        private static PlaneDiagnostic BuildPlane(ARPlane plane, string hitPlaneId, Camera camera)
        {
            var result = new PlaneDiagnostic
            {
                id = plane.trackableId.ToString(),
                isHitPlane = plane.trackableId.ToString() == hitPlaneId,
                trackingState = plane.trackingState.ToString(),
                alignment = plane.alignment.ToString(),
                center = VectorData.From(plane.center),
                worldPosition = VectorData.From(plane.transform.position),
                worldRotation = VectorData.From(plane.transform.rotation),
                extents = VectorData.From(plane.extents),
                centerScreenPixels = VectorData.From(ProjectToScreen(plane.center, camera)),
            };
            foreach (var point in plane.boundary)
            {
                var world = plane.transform.TransformPoint(new Vector3(point.x, 0f, point.y));
                result.boundary.Add(new PlaneVertexDiagnostic
                {
                    worldPosition = VectorData.From(world),
                    screenPixels = VectorData.From(ProjectToScreen(world, camera)),
                });
            }
            return result;
        }
#endif

        /// <summary>屏幕投影与是否处于相机前方且在屏幕内；测试与诊断共用同一坐标约定。</summary>
        public static Vector3 ProjectToScreen(Vector3 worldPosition, Camera camera)
        {
            return camera.WorldToScreenPoint(worldPosition);
        }

        public static bool IsOnScreen(Vector3 screenPosition)
        {
            return screenPosition.z > 0f
                && screenPosition.x >= 0f && screenPosition.x <= Screen.width
                && screenPosition.y >= 0f && screenPosition.y <= Screen.height;
        }

        private static float[] MatrixValues(Matrix4x4 matrix)
        {
            var values = new float[16];
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    values[row * 4 + column] = matrix[row, column];
                }
            }
            return values;
        }
    }

    [Serializable]
    public class PlacementDiagnosticRecord
    {
        public int schemaVersion;
        public string capturedAt;
        public int placementFrame;
        public int captureFrame;
        public string screenshotFile;
        public string session;
        public string notTrackingReason;
        public ScreenDiagnostic screen;
        public CameraDiagnostic camera;
        public HitDiagnostic hit;
        public MarkerDiagnostic marker;
        public System.Collections.Generic.List<PlaneDiagnostic> planes = new System.Collections.Generic.List<PlaneDiagnostic>();
    }

    [Serializable]
    public class VectorData
    {
        public double x;
        public double y;
        public double z;

        public static VectorData From(Vector2 value) => new VectorData { x = value.x, y = value.y };
        public static VectorData From(Vector3 value) => new VectorData { x = value.x, y = value.y, z = value.z };
        public static VectorData From(Quaternion value) => new VectorData { x = value.x, y = value.y, z = value.z, w = value.w };
        public double w;
    }

    [Serializable]
    public class ScreenDiagnostic
    {
        public int width;
        public int height;
        public string orientation;
        public VectorData touchPixels;
        public VectorData touchNormalized;
    }

    [Serializable]
    public class CameraDiagnostic
    {
        public VectorData worldPosition;
        public VectorData worldRotation;
        public VectorData forward;
        public float fieldOfView;
        public float nearClipPlane;
        public float farClipPlane;
        public float[] projectionMatrix;
        public float[] worldToCameraMatrix;
        public bool intrinsicsAvailable;
        public VectorData focalLength;
        public VectorData principalPoint;
        public int imageWidth;
        public int imageHeight;
        public int cameraImageWidth;
        public int cameraImageHeight;
        public int cameraFramerate;
        public string graphicsDevice;
        public string deviceModel;
    }

    [Serializable]
    public class HitDiagnostic
    {
        public string planeId;
        public VectorData worldPosition;
        public VectorData worldRotation;
        public float distanceFromCamera;
        public VectorData screenPixels;
        public VectorData screenOffsetFromTouch;
    }

    [Serializable]
    public class MarkerDiagnostic
    {
        public VectorData worldPosition;
        public VectorData worldRotation;
        public VectorData worldScale;
        public string anchorId;
        public VectorData screenPixels;
        public VectorData screenOffsetFromTouch;
        public float pixelDistanceFromTouch;
        public float distanceFromCamera;
        public bool onScreen;
        public bool rendererVisible;
    }

    [Serializable]
    public class PlaneDiagnostic
    {
        public string id;
        public bool isHitPlane;
        public string trackingState;
        public string alignment;
        public VectorData center;
        public VectorData worldPosition;
        public VectorData worldRotation;
        public VectorData extents;
        public VectorData centerScreenPixels;
        public System.Collections.Generic.List<PlaneVertexDiagnostic> boundary = new System.Collections.Generic.List<PlaneVertexDiagnostic>();
    }

    [Serializable]
    public class PlaneVertexDiagnostic
    {
        public VectorData worldPosition;
        public VectorData screenPixels;
    }
}
