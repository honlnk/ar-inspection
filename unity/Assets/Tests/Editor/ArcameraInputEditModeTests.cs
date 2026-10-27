using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using Inspection.EditorTools;

/// <summary>
/// 计划 005 阶段 2 EditMode 测试：AR 输入链路统一配置（ProjectSetup.EnsureArcameraInput）
/// 与精确门禁（GateVerify.GetArcameraInputFailures）的防回归契约。
/// 全部在临时空场景中搭建最小 AR 骨架（AR Session + XR Origin + 主相机）验证，
/// 不打开、不修改正式 Inspection 场景；负例覆盖复盘 002 的缺陷配置
/// （旧版空间跟踪驱动、缺 AR 输入管理器、缺手持 AR 绑定、重复组件），
/// 保证门禁对旧配置明确失败、对修复后的新配置通过，不回到「名字相近就算过」的旧口径。
/// </summary>
public class ArcameraInputEditModeTests
{
    private GameObject _sessionGo;
    private GameObject _cameraGo;

    [SetUp]
    public void SetUp()
    {
        // 默认搭出复盘 002 的缺陷骨架：AR Session 无输入管理器、主相机挂旧版空间跟踪驱动
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildMinimalRig(withLegacyDriver: true, withInputManager: false);
    }

    [TearDown]
    public void TearDown()
    {
        // 丢弃临时场景（未保存），不把测试对象留在编辑器里
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    // ---------- 统一配置：修复缺陷配置 ----------

    [Test]
    public void EnsureArcameraInput_ReplacesLegacyDriverAndAddsInputManager()
    {
        ProjectSetup.EnsureArcameraInput();

        Assert.AreEqual(1, _sessionGo.GetComponents<ARInputManager>().Length,
            "AR Session 同对象应恰好有一个 AR 输入管理器");
        var drivers = _cameraGo.GetComponents<TrackedPoseDriver>();
        Assert.AreEqual(1, drivers.Length, "主相机应恰好有一个 Input System 版姿态驱动");
        Assert.AreEqual(0, CountLegacyPoseDrivers(_cameraGo),
            "旧版空间跟踪姿态驱动应被删除，不与新驱动并存");

        var driver = drivers[0];
        var positionAction = driver.positionInput.action;
        var rotationAction = driver.rotationInput.action;
        Assert.IsNotNull(positionAction, "位置输入动作应随组件存在");
        Assert.IsNotNull(rotationAction, "旋转输入动作应随组件存在");
        Assert.IsTrue(HasBinding(positionAction, "<XRHMD>/centerEyePosition"), "位置输入应含头显兼容绑定");
        Assert.IsTrue(HasBinding(positionAction, "<HandheldARInputDevice>/devicePosition"),
            "位置输入应含手持 AR 设备绑定");
        Assert.IsTrue(HasBinding(rotationAction, "<XRHMD>/centerEyeRotation"), "旋转输入应含头显兼容绑定");
        Assert.IsTrue(HasBinding(rotationAction, "<HandheldARInputDevice>/deviceRotation"),
            "旋转输入应含手持 AR 设备绑定");
    }

    [Test]
    public void EnsureArcameraInput_IsIdempotentOnRepeatedRuns()
    {
        ProjectSetup.EnsureArcameraInput();
        ProjectSetup.EnsureArcameraInput();

        Assert.AreEqual(1, _sessionGo.GetComponents<ARInputManager>().Length,
            "重复执行不得新增重复 AR 输入管理器");
        Assert.AreEqual(1, _cameraGo.GetComponents<TrackedPoseDriver>().Length,
            "重复执行不得新增重复姿态驱动");
        Assert.AreEqual(0, GateVerify.GetArcameraInputFailures().Count, "重复执行后门禁应保持通过");
    }

    // ---------- 精确门禁：负例必须明确失败 ----------

    [Test]
    public void GateDetectsLegacyDriverAndMissingInputManager()
    {
        var failures = GateVerify.GetArcameraInputFailures();
        Assert.IsNotEmpty(failures, "缺陷配置（旧驱动 + 缺输入管理器）必须被门禁拒绝");
        var joined = string.Join("；", failures);
        StringAssert.Contains("ARInputManager", joined, "应指出缺少 AR 输入管理器");
        StringAssert.Contains("Input System 版姿态驱动", joined, "应指出缺少 Input System 版姿态驱动");
        StringAssert.Contains("旧版空间跟踪姿态驱动", joined, "应指出仍存在旧版空间跟踪驱动");

        ProjectSetup.EnsureArcameraInput();
        Assert.AreEqual(0, GateVerify.GetArcameraInputFailures().Count, "修复后同口径门禁应通过");
    }

    [Test]
    public void GateDetectsMissingHandheldBindings()
    {
        // 干净骨架 + 输入管理器 + 只带头显兼容位置绑定的驱动（模拟绑定被部分删除/未写入）。
        // 旋转输入保持组件默认（动作实例存在但无绑定），门禁应报缺绑定而不是放过
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        BuildMinimalRig(withLegacyDriver: false, withInputManager: true);
        var driver = _cameraGo.AddComponent<TrackedPoseDriver>();
        driver.positionInput = new InputActionProperty(new InputAction(
            "Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3"));

        var failures = GateVerify.GetArcameraInputFailures();
        Assert.IsNotEmpty(failures, "缺手持 AR 绑定的配置必须被门禁拒绝");
        var joined = string.Join("；", failures);
        StringAssert.Contains("位置输入缺少手持 AR 设备绑定", joined, "应指出位置输入缺手持 AR 设备绑定");
        StringAssert.Contains("旋转输入缺少手持 AR 设备绑定", joined, "应指出旋转输入缺手持 AR 设备绑定");

        ProjectSetup.EnsureArcameraInput();
        Assert.AreEqual(0, GateVerify.GetArcameraInputFailures().Count, "重新统一配置后门禁应通过");
    }

    [Test]
    public void GateDetectsDuplicatePoseDrivers()
    {
        ProjectSetup.EnsureArcameraInput();
        // ARInputManager 带 DisallowMultipleComponent，编辑器 API 加不出第二个（重复仅可能来自
        // 手改 YAML，门禁保留计数检查作防线）；姿态驱动无此限制，用第二个驱动验证重复检测
        _cameraGo.AddComponent<TrackedPoseDriver>();

        var failures = GateVerify.GetArcameraInputFailures();
        Assert.IsNotEmpty(failures, "重复姿态驱动必须被门禁拒绝");
        StringAssert.Contains("2 个 Input System 姿态驱动", string.Join("；", failures), "应指出姿态驱动重复");

        ProjectSetup.EnsureArcameraInput();
        Assert.AreEqual(0, GateVerify.GetArcameraInputFailures().Count, "统一配置应收敛重复组件");
    }

    // ---------- 工具 ----------

    /// <summary>
    /// 旧版空间跟踪驱动不在自定义 asmdef 的自动引擎引用里，按类型全名反射添加，
    /// 与编辑器工具识别旧组件的方式一致。
    /// </summary>
    private static void AddLegacyPoseDriver(GameObject target)
    {
        var legacyType = System.Type.GetType(
            "UnityEngine.SpatialTracking.TrackedPoseDriver, UnityEngine.SpatialTracking");
        Assert.IsNotNull(legacyType, "本机 Unity 应包含 UnityEngine.SpatialTracking 模块");
        Assert.IsNotNull(target.AddComponent(legacyType), "旧驱动组件应可添加到主相机");
    }

    private static int CountLegacyPoseDrivers(GameObject target)
    {
        var count = 0;
        foreach (var component in target.GetComponents<Component>())
        {
            if (component != null
                && component.GetType().FullName == "UnityEngine.SpatialTracking.TrackedPoseDriver")
            {
                count++;
            }
        }
        return count;
    }

    private void BuildMinimalRig(bool withLegacyDriver, bool withInputManager)
    {
        _sessionGo = new GameObject("AR Session");
        _sessionGo.AddComponent<ARSession>();
        if (withInputManager)
        {
            _sessionGo.AddComponent<ARInputManager>();
        }

        var originGo = new GameObject("XR Origin");
        var origin = originGo.AddComponent<XROrigin>();
        var offsetGo = new GameObject("Camera Offset");
        offsetGo.transform.SetParent(originGo.transform, false);
        _cameraGo = new GameObject("Main Camera");
        var camera = _cameraGo.AddComponent<Camera>();
        _cameraGo.AddComponent<ARCameraManager>();
        _cameraGo.AddComponent<ARCameraBackground>();
        if (withLegacyDriver)
        {
            AddLegacyPoseDriver(_cameraGo);
        }
        _cameraGo.transform.SetParent(offsetGo.transform, false);
        _cameraGo.tag = "MainCamera";
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offsetGo;
    }

    private static bool HasBinding(InputAction action, string path)
    {
        foreach (var binding in action.bindings)
        {
            if (binding.path == path)
            {
                return true;
            }
        }
        return false;
    }
}
