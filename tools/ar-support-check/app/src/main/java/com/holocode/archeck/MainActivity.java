package com.holocode.archeck;

import android.app.Activity;
import android.graphics.Color;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.util.Log;
import android.widget.TextView;

import com.google.ar.core.ArCoreApk;
import com.google.ar.core.ArCoreApk.Availability;
import com.google.ar.core.ArCoreApk.InstallStatus;
import com.google.ar.core.Session;
import com.google.ar.core.exceptions.UnavailableArcoreNotInstalledException;
import com.google.ar.core.exceptions.UnavailableDeviceNotCompatibleException;
import com.google.ar.core.exceptions.UnavailableException;
import com.google.ar.core.exceptions.UnavailableSdkTooOldException;

public class MainActivity extends Activity {

    private static final String TAG = "ARCHECK";
    private final StringBuilder log = new StringBuilder();
    private final Handler ui = new Handler(Looper.getMainLooper());
    private TextView tv;

    private void say(String s) {
        Log.i(TAG, s);
        ui.post(() -> {
            log.append(s).append("\n\n");
            tv.setText(log.toString());
        });
    }

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        tv = new TextView(this);
        tv.setTextSize(18f);
        tv.setTextColor(Color.BLACK);
        tv.setPadding(56, 120, 56, 56);
        setContentView(tv);
        say("ARCore 支持检测\n\n机型: " + Build.MANUFACTURER + " " + Build.MODEL
                + "\n设备码: " + Build.DEVICE
                + "\nAndroid: " + Build.VERSION.RELEASE + " (API " + Build.VERSION.SDK_INT + ")");
        new Thread(this::runCheck, "archeck").start();
    }

    private void runCheck() {
        // 第一步：官方入场检查。SUPPORTED_UNKNOWN 表示正在向 Google 服务器查询，需要轮询。
        ArCoreApk arCoreApk = ArCoreApk.getInstance();
        Availability av = Availability.UNKNOWN_CHECKING;
        for (int i = 1; i <= 30; i++) {
            av = arCoreApk.checkAvailability(this);
            say("checkAvailability #" + i + " → " + av);
            if (!av.isTransient()) {
                break;
            }
            try {
                Thread.sleep(2000);
            } catch (InterruptedException e) {
                Thread.currentThread().interrupt();
                break;
            }
        }

        // 若支持但运行时没装好，先走官方安装流程
        if (av == Availability.SUPPORTED_NOT_INSTALLED
                || av == Availability.SUPPORTED_APK_TOO_OLD
                || av == Availability.SUPPORTED_INSTALLED) {
            try {
                InstallStatus st = arCoreApk.requestInstall(this, true);
                say("requestInstall → " + st);
            } catch (Exception e) {
                say("requestInstall 异常: " + e);
            }
            av = arCoreApk.checkAvailability(this);
            say("复查 checkAvailability → " + av);
        }

        // 第二步（终极判定）：无论上面结果如何，都真实创建一次 AR 会话
        String verdict;
        Session session = null;
        try {
            session = new Session(this);
            say("★ Session 创建成功（相机与跟踪子系统已初始化）");
            try {
                session.resume();
                say("★ Session.resume() 成功：相机已启动、跟踪已开始运行");
                for (int i = 1; i <= 3; i++) {
                    Thread.sleep(1000);
                    com.google.ar.core.Frame frame = session.update();
                    com.google.ar.core.Camera cam = frame.getCamera();
                    String pose;
                    try {
                        pose = String.valueOf(cam.getDisplayOrientedPose().getTranslation());
                    } catch (RuntimeException e) {
                        pose = "(姿态尚未就绪: " + e.getClass().getSimpleName() + ")";
                    }
                    say("第" + i + "秒 帧@" + frame.getTimestamp()
                            + " 跟踪状态=" + cam.getTrackingState()
                            + " 位置=" + pose);
                }
                session.pause();
                say("★ 3 秒采样完成，跟踪数据正常产出");
                verdict = "★ 最终判定：设备实际可运行 ARCore（会话+相机跟踪全部成功）";
            } catch (SecurityException e) {
                say("✗ resume 失败 [相机权限]: " + e);
                verdict = "△ 会话可创建，但相机权限被拒";
            } catch (com.google.ar.core.exceptions.CameraNotAvailableException e) {
                say("✗ resume 失败 [相机不可用]: " + e);
                verdict = "△ 会话可创建，但相机启动失败";
            }
        } catch (UnavailableDeviceNotCompatibleException e) {
            say("✗ Session 失败 [设备不兼容]: " + e);
            verdict = "✗ 最终判定：设备不支持 ARCore（UNSUPPORTED_DEVICE_NOT_CAPABLE）";
        } catch (UnavailableArcoreNotInstalledException e) {
            say("✗ Session 失败 [ARCore 未安装]: " + e);
            verdict = "✗ 最终判定：ARCore 运行时未安装";
        } catch (UnavailableSdkTooOldException e) {
            say("✗ Session 失败 [设备运行时过旧]: " + e);
            verdict = "✗ 最终判定：设备端 ARCore 运行时过旧";
        } catch (Exception e) {
            say("✗ Session 异常 [" + e.getClass().getSimpleName() + "]: " + e);
            verdict = "✗ 最终判定：会话创建失败，见上方异常";
        } finally {
            if (session != null) {
                session.close();
            }
        }
        say(verdict + "\n(availability 最终值 = " + av + ")");
        Log.i(TAG, "VERDICT: " + verdict + " | availability=" + av);
    }
}
