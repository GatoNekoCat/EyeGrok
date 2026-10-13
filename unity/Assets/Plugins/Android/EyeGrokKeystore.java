// Copyright 2026 EyeGrok contributors
// SPDX-License-Identifier: Apache-2.0

package io.github.eyegrok;

import android.content.Context;
import android.content.SharedPreferences;

import java.io.File;
import android.security.keystore.KeyGenParameterSpec;
import android.security.keystore.KeyProperties;
import android.util.Base64;

import java.nio.charset.StandardCharsets;
import java.security.KeyStore;

import javax.crypto.Cipher;
import javax.crypto.KeyGenerator;
import javax.crypto.SecretKey;
import javax.crypto.spec.GCMParameterSpec;

public final class EyeGrokKeystore {
    public static final String APP_ALIAS = "eyegrok_v1";
    private static final String STORE = "AndroidKeyStore";
    private static final String PREFS = "eyegrok_secure";
    private static final int GCM_TAG_BITS = 128;

    private EyeGrokKeystore() {}

    public static void put(Context ctx, String alias, String name, String value) throws Exception {
        ensureKey(alias);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.ENCRYPT_MODE, secret(alias));
        byte[] iv = cipher.getIV();
        byte[] ct = cipher.doFinal(value.getBytes(StandardCharsets.UTF_8));
        String stored = Base64.encodeToString(iv, Base64.NO_WRAP)
            + ":"
            + Base64.encodeToString(ct, Base64.NO_WRAP);
        prefs(ctx).edit().putString(prefKey(alias, name), stored).apply();
    }

    public static String get(Context ctx, String alias, String name) throws Exception {
        String stored = prefs(ctx).getString(prefKey(alias, name), null);
        if (stored == null) return "";
        int split = stored.indexOf(':');
        if (split <= 0) return "";
        byte[] iv = Base64.decode(stored.substring(0, split), Base64.NO_WRAP);
        byte[] ct = Base64.decode(stored.substring(split + 1), Base64.NO_WRAP);
        Cipher cipher = Cipher.getInstance("AES/GCM/NoPadding");
        cipher.init(Cipher.DECRYPT_MODE, secret(alias), new GCMParameterSpec(GCM_TAG_BITS, iv));
        return new String(cipher.doFinal(ct), StandardCharsets.UTF_8);
    }

    public static void deleteAlias(Context ctx, String alias) throws Exception {
        SharedPreferences prefs = prefs(ctx);
        SharedPreferences.Editor editor = prefs.edit();
        String prefix = alias + ":";
        for (String key : prefs.getAll().keySet()) {
            if (key.startsWith(prefix)) editor.remove(key);
        }
        editor.apply();
        KeyStore ks = KeyStore.getInstance(STORE);
        ks.load(null);
        if (ks.containsAlias(alias)) ks.deleteEntry(alias);
    }

    public static String externalFilesDir(Context ctx) {
        File dir = ctx.getExternalFilesDir(null);
        if (dir == null) return "";
        return dir.getAbsolutePath();
    }

    public static String probeRoundTrip(Context ctx) {
        String alias = "eyegrok_probe";
        try {
            put(ctx, alias, "probe", "ping");
            String got = get(ctx, alias, "probe");
            deleteAlias(ctx, alias);
            if (!"ping".equals(got)) throw new IllegalStateException("mismatch");
            return "ok";
        } catch (Throwable t) {
            try {
                deleteAlias(ctx, alias);
            } catch (Throwable ignored) {
            }
            return "fail " + t.getClass().getSimpleName();
        }
    }

    private static void ensureKey(String alias) throws Exception {
        KeyStore ks = KeyStore.getInstance(STORE);
        ks.load(null);
        if (ks.containsAlias(alias)) return;
        KeyGenerator generator = KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, STORE);
        KeyGenParameterSpec spec = new KeyGenParameterSpec.Builder(
            alias,
            KeyProperties.PURPOSE_ENCRYPT | KeyProperties.PURPOSE_DECRYPT)
            .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
            .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
            .setKeySize(256)
            .setRandomizedEncryptionRequired(true)
            .build();
        generator.init(spec);
        generator.generateKey();
    }

    private static SecretKey secret(String alias) throws Exception {
        KeyStore ks = KeyStore.getInstance(STORE);
        ks.load(null);
        return (SecretKey) ks.getKey(alias, null);
    }

    private static SharedPreferences prefs(Context ctx) {
        return ctx.getSharedPreferences(PREFS, Context.MODE_PRIVATE);
    }

    private static String prefKey(String alias, String name) {
        return alias + ":" + name;
    }
}
