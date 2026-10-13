// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0
// Probe-only. The app build disables this plugin.

package io.github.eyegrok;

import android.content.Context;
import android.graphics.ImageFormat;
import android.hardware.camera2.CameraAccessException;
import android.hardware.camera2.CameraCaptureSession;
import android.hardware.camera2.CameraCharacteristics;
import android.hardware.camera2.CameraDevice;
import android.hardware.camera2.CameraManager;
import android.hardware.camera2.CaptureRequest;
import android.hardware.camera2.params.StreamConfigurationMap;
import android.media.Image;
import android.media.ImageReader;
import android.os.Handler;
import android.os.HandlerThread;
import android.os.SystemClock;
import android.util.Size;
import android.view.Surface;

import java.util.Collections;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.atomic.AtomicReference;

public final class EyeGrokCamProbe {
    private EyeGrokCamProbe() {}

    // First line is the count. Each later line is id|facing|hw.
    public static String describeCameras(Context ctx) {
        try {
            CameraManager manager = (CameraManager) ctx.getSystemService(Context.CAMERA_SERVICE);
            String[] ids = manager.getCameraIdList();
            StringBuilder sb = new StringBuilder();
            sb.append(ids.length);
            for (int i = 0; i < ids.length; i++) {
                CameraCharacteristics chars = manager.getCameraCharacteristics(ids[i]);
                Integer facing = chars.get(CameraCharacteristics.LENS_FACING);
                Integer hw = chars.get(CameraCharacteristics.INFO_SUPPORTED_HARDWARE_LEVEL);
                int face = facing == null ? 2 : facing.intValue();
                int level = hw == null ? 2 : hw.intValue();
                sb.append('\n').append(ids[i]).append('|').append(face).append('|').append(level);
            }
            return sb.toString();
        } catch (Throwable ignored) {
            return "0";
        }
    }

    // open|ms|err|frame|w|h
    public static String openAndFrame(Context ctx) {
        long start = SystemClock.elapsedRealtime();
        HandlerThread thread = new HandlerThread("eyegrok-cam");
        thread.start();
        Handler handler = new Handler(thread.getLooper());
        CameraDevice device = null;
        ImageReader reader = null;
        CameraCaptureSession session = null;
        try {
            CameraManager manager = (CameraManager) ctx.getSystemService(Context.CAMERA_SERVICE);
            String[] ids = manager.getCameraIdList();
            if (ids.length == 0) {
                return fail(start, "IllegalStateException");
            }
            String id = ids[0];
            CameraCharacteristics chars = manager.getCameraCharacteristics(id);
            Size size = chooseSize(chars);
            reader = ImageReader.newInstance(size.getWidth(), size.getHeight(), ImageFormat.YUV_420_888, 2);
            final CountDownLatch frameLatch = new CountDownLatch(1);
            final int[] wh = new int[] { 0, 0 };
            reader.setOnImageAvailableListener(new ImageReader.OnImageAvailableListener() {
                @Override
                public void onImageAvailable(ImageReader source) {
                    Image image = null;
                    try {
                        image = source.acquireLatestImage();
                        if (image != null) {
                            wh[0] = image.getWidth();
                            wh[1] = image.getHeight();
                            frameLatch.countDown();
                        }
                    } catch (Throwable ignored) {
                    } finally {
                        if (image != null) image.close();
                    }
                }
            }, handler);

            final CountDownLatch openLatch = new CountDownLatch(1);
            final AtomicReference<CameraDevice> opened = new AtomicReference<CameraDevice>();
            final AtomicReference<String> error = new AtomicReference<String>("");
            manager.openCamera(id, new CameraDevice.StateCallback() {
                @Override
                public void onOpened(CameraDevice camera) {
                    opened.set(camera);
                    openLatch.countDown();
                }

                @Override
                public void onDisconnected(CameraDevice camera) {
                    camera.close();
                    error.set("CameraAccessException");
                    openLatch.countDown();
                }

                @Override
                public void onError(CameraDevice camera, int err) {
                    camera.close();
                    error.set("CameraAccessException");
                    openLatch.countDown();
                }
            }, handler);

            if (!openLatch.await(5, TimeUnit.SECONDS) || opened.get() == null) {
                String err = error.get();
                if (err == null || err.length() == 0) err = "TimeoutException";
                return fail(start, err);
            }
            device = opened.get();
            long openMs = SystemClock.elapsedRealtime() - start;
            final Surface surface = reader.getSurface();
            final CaptureRequest.Builder builder = device.createCaptureRequest(CameraDevice.TEMPLATE_PREVIEW);
            builder.addTarget(surface);
            final CountDownLatch sessionLatch = new CountDownLatch(1);
            final CameraDevice captureDevice = device;
            final AtomicReference<CameraCaptureSession> sessionRef = new AtomicReference<CameraCaptureSession>();
            captureDevice.createCaptureSession(Collections.singletonList(surface), new CameraCaptureSession.StateCallback() {
                @Override
                public void onConfigured(CameraCaptureSession created) {
                    sessionRef.set(created);
                    try {
                        created.setRepeatingRequest(builder.build(), null, handler);
                    } catch (CameraAccessException ex) {
                        error.set(ex.getClass().getSimpleName());
                    }
                    sessionLatch.countDown();
                }

                @Override
                public void onConfigureFailed(CameraCaptureSession created) {
                    error.set("CameraAccessException");
                    sessionLatch.countDown();
                }
            }, handler);

            long remain = remaining(start);
            if (!sessionLatch.await(remain, TimeUnit.MILLISECONDS) || sessionRef.get() == null) {
                return "ok|" + openMs + "||fail|0|0";
            }
            session = sessionRef.get();
            boolean got = frameLatch.await(remaining(start), TimeUnit.MILLISECONDS);
            if (!got || wh[0] <= 0) {
                return "ok|" + openMs + "||fail|0|0";
            }
            return "ok|" + openMs + "||ok|" + wh[0] + "|" + wh[1];
        } catch (Throwable ex) {
            return fail(start, ex.getClass().getSimpleName());
        } finally {
            if (session != null) {
                try { session.close(); } catch (Throwable ignored) {}
            }
            if (device != null) {
                try { device.close(); } catch (Throwable ignored) {}
            }
            if (reader != null) {
                try { reader.close(); } catch (Throwable ignored) {}
            }
            thread.quitSafely();
        }
    }

    private static long remaining(long start) {
        long remain = 5000L - (SystemClock.elapsedRealtime() - start);
        if (remain < 1L) return 1L;
        return remain;
    }

    private static String fail(long start, String err) {
        long ms = SystemClock.elapsedRealtime() - start;
        return "fail|" + ms + "|" + err + "|fail|0|0";
    }

    private static Size chooseSize(CameraCharacteristics chars) {
        StreamConfigurationMap map = chars.get(CameraCharacteristics.SCALER_STREAM_CONFIGURATION_MAP);
        Size[] sizes = map == null ? null : map.getOutputSizes(ImageFormat.YUV_420_888);
        if (sizes == null || sizes.length == 0) return new Size(640, 480);
        Size best = sizes[0];
        int bestArea = best.getWidth() * best.getHeight();
        for (int i = 1; i < sizes.length; i++) {
            int area = sizes[i].getWidth() * sizes[i].getHeight();
            if (area < bestArea && area >= 160 * 120) {
                best = sizes[i];
                bestArea = area;
            }
        }
        return best;
    }
}
