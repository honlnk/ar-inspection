using System;
using NUnit.Framework;
using UnityEngine;
using Inspection.AR;

/// <summary>放置诊断的屏幕坐标约定与 JSON 字段契约；不模拟 AR、不写手机文件。</summary>
public class PlacementDiagnosticsEditModeTests
{
    private Camera camera;

    [SetUp]
    public void SetUp()
    {
        camera = new GameObject("PlacementDiagnosticsTestCamera").AddComponent<Camera>();
        camera.transform.position = Vector3.zero;
        camera.transform.rotation = Quaternion.identity;
    }

    [TearDown]
    public void TearDown()
    {
        if (camera != null)
        {
            UnityEngine.Object.DestroyImmediate(camera.gameObject);
        }
    }

    [Test]
    public void ScreenProjection_ClickAtHit_HasZeroOffset()
    {
        var hit = new Vector3(0f, 0f, 5f);
        var projected = PlacementDiagnostics.ProjectToScreen(hit, camera);
        var touch = new Vector2(projected.x, projected.y);

        Assert.AreEqual(camera.pixelWidth / 2f, projected.x, 0.01f);
        Assert.AreEqual(camera.pixelHeight / 2f, projected.y, 0.01f);
        Assert.AreEqual(0f, Vector2.Distance(projected, touch), 0.001f);
    }

    [Test]
    public void ScreenProjection_BelowClick_HasNegativeVerticalOffset()
    {
        var center = PlacementDiagnostics.ProjectToScreen(new Vector3(0f, 0f, 5f), camera);
        var below = PlacementDiagnostics.ProjectToScreen(new Vector3(0f, -1f, 5f), camera);
        var offset = below - center;

        Assert.AreEqual(0f, offset.x, 0.01f);
        Assert.Less(offset.y, 0f, "屏幕坐标原点为左下角，向下偏移应为负 Y");
    }

    [Test]
    public void DiagnosticJson_RoundTrip_PreservesScreenshotPlanesAndProjection()
    {
        var source = new PlacementDiagnosticRecord
        {
            schemaVersion = 1,
            capturedAt = new DateTimeOffset(2026, 10, 28, 10, 0, 0, TimeSpan.FromHours(8)).ToString("O"),
            screenshotFile = "placement-20261028-100000-000-001.png",
            screen = new ScreenDiagnostic
            {
                width = 1080,
                height = 2400,
                touchPixels = VectorData.From(new Vector2(400f, 1500f)),
                touchNormalized = VectorData.From(new Vector2(400f / 1080f, 1500f / 2400f)),
            },
            hit = new HitDiagnostic { planeId = "plane-a", worldPosition = VectorData.From(new Vector3(0f, 0f, 1f)) },
            marker = new MarkerDiagnostic
            {
                screenPixels = VectorData.From(new Vector3(400f, 1300f, 5f)),
                screenOffsetFromTouch = VectorData.From(new Vector3(0f, -200f, 5f)),
                pixelDistanceFromTouch = 200f,
            },
        };
        source.planes.Add(new PlaneDiagnostic
        {
            id = "plane-a",
            isHitPlane = true,
            boundary =
            {
                new PlaneVertexDiagnostic
                {
                    worldPosition = VectorData.From(new Vector3(0.1f, 0f, 1f)),
                    screenPixels = VectorData.From(new Vector3(500f, 1200f, 1f)),
                },
            },
        });

        var json = JsonUtility.ToJson(source, true);
        var restored = JsonUtility.FromJson<PlacementDiagnosticRecord>(json);

        StringAssert.Contains("2026-10-28T10:00:00.0000000+08:00", json);
        StringAssert.Contains("screenOffsetFromTouch", json);
        Assert.AreEqual(source.screenshotFile, restored.screenshotFile);
        Assert.AreEqual(1080, restored.screen.width);
        Assert.AreEqual(400f, restored.screen.touchPixels.x);
        Assert.AreEqual(-200f, restored.marker.screenOffsetFromTouch.y);
        Assert.AreEqual(1, restored.planes.Count);
        Assert.IsTrue(restored.planes[0].isHitPlane);
        Assert.AreEqual(1200f, restored.planes[0].boundary[0].screenPixels.y);
    }
}
