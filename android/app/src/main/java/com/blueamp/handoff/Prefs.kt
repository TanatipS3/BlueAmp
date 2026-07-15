package com.blueamp.handoff

import android.content.Context

/** Small persisted configuration: which PC and earbuds to use, and the earbud mode. */
class Prefs(context: Context) {
    private val sp = context.getSharedPreferences(NAME, Context.MODE_PRIVATE)

    /** Bluetooth MAC of the paired PC running BlueAmp. */
    var pcMac: String?
        get() = sp.getString(KEY_PC, null)
        set(v) = sp.edit().putString(KEY_PC, v).apply()

    /** Bluetooth MAC of the earbuds to hand off. */
    var earbudMac: String?
        get() = sp.getString(KEY_EARBUD, null)
        set(v) = sp.edit().putString(KEY_EARBUD, v).apply()

    /** True for single-point earbuds (phone must actively claim/release them). */
    var singlePoint: Boolean
        get() = sp.getBoolean(KEY_SINGLE, false)
        set(v) = sp.edit().putBoolean(KEY_SINGLE, v).apply()

    val isConfigured: Boolean get() = !pcMac.isNullOrEmpty() && !earbudMac.isNullOrEmpty()

    val mode: String get() = if (singlePoint) "SinglePoint" else "Multipoint"

    companion object {
        private const val NAME = "blueamp_handoff"
        private const val KEY_PC = "pc_mac"
        private const val KEY_EARBUD = "earbud_mac"
        private const val KEY_SINGLE = "single_point"
    }
}
