// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0
// Probe-only. The app build disables this plugin. It never reads setup file bytes.

package io.github.eyegrok;

import android.content.Context;
import android.content.Intent;
import android.speech.SpeechRecognizer;
import android.speech.tts.TextToSpeech;

import java.io.File;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.TimeUnit;

public final class EyeGrokProbe {
    private EyeGrokProbe() {}

    public static String services(Context ctx) {
        boolean rec = false;
        int services = 0;
        int engines = 0;
        int status = -1;
        TextToSpeech tts = null;
        try {
            rec = SpeechRecognizer.isRecognitionAvailable(ctx);
            services = ctx.getPackageManager().queryIntentServices(
                new Intent("android.speech.RecognitionService"), 0).size();
            final int[] box = new int[] { -1 };
            final CountDownLatch latch = new CountDownLatch(1);
            tts = new TextToSpeech(ctx, new TextToSpeech.OnInitListener() {
                @Override
                public void onInit(int initStatus) {
                    box[0] = initStatus;
                    latch.countDown();
                }
            });
            if (tts.getEngines() != null) engines = tts.getEngines().size();
            latch.await(2, TimeUnit.SECONDS);
            status = box[0];
        } catch (Throwable ignored) {
            status = -2;
        } finally {
            if (tts != null) tts.shutdown();
        }
        return "rec=" + rec + " services=" + services + " engines=" + engines + " tts=" + status;
    }

    public static String externalDir(Context ctx) {
        File dir = ctx.getExternalFilesDir(null);
        if (dir == null) return "";
        return dir.getAbsolutePath();
    }

    public static boolean setupExists(Context ctx) {
        File dir = ctx.getExternalFilesDir(null);
        if (dir == null) return false;
        return new File(dir, "eyegrok-setup.json").exists();
    }
}
