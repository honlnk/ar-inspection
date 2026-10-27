using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;

namespace Inspection.EditorTools
{
    /// <summary>
    /// Android 开发构建入口：菜单「现场巡检 / 构建 Android 开发包」与命令行
    /// Unity -batchmode -quit -projectPath &lt;工程&gt; -executeMethod Inspection.EditorTools.BuildAndroid.Run
    /// 共用同一逻辑。产物固定输出 Builds/inspection-dev.apk（开发构建 + LZ4 压缩，
    /// 使用 Unity 默认调试签名；Builds/ 目录不入库，见根 .gitignore）。
    /// </summary>
    public static class BuildAndroid
    {
        private const string OutputPath = "Builds/inspection-dev.apk";
        private const string ScenePath = "Assets/Scenes/Inspection.unity";

        [MenuItem("现场巡检/构建 Android 开发包")]
        public static void Run()
        {
            // 批处理模式下工程可能停在别的构建目标，先确保切到 Android（幂等）
            if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.Android)
            {
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android))
                {
                    Fail("切换 Android 构建目标失败");
                    return;
                }
            }

            // 保留 OpenGL ES 3（004 攻坚期由 Vulkan 切换而来）。复盘 002 已证实切换后偏移依旧，
            // 根因是渲染相机未消费手持 AR 设备姿态，由计划 005 修复输入链路解决；
            // 本设置不再承担偏移修复职责，仅作为 ARCore 成熟图形路径保留；幂等
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            Directory.CreateDirectory("Builds");

            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { ScenePath },
                locationPathName = OutputPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                // 开发构建：可连接调试器、明文 HTTP 放行（配合 insecureHttpOption=1）；不做代码裁剪/混淆
                options = BuildOptions.Development | BuildOptions.CompressWithLz4,
            });

            var summary = report.summary;
            if (summary.result != BuildResult.Succeeded)
            {
                Fail($"构建失败：{summary.result}，错误 {summary.totalErrors} 个（完整原因见编辑器日志）");
                return;
            }

            Debug.Log($"[BuildAndroid] 构建成功：{OutputPath}，" +
                      $"大小 {summary.totalSize / (1024f * 1024f):F1} MB，用时 {summary.totalTime.TotalSeconds:F0} 秒");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(0);
            }
        }

        private static void Fail(string message)
        {
            Debug.LogError($"[BuildAndroid] {message}");
            if (Application.isBatchMode)
            {
                EditorApplication.Exit(1);
            }
        }
    }
}
