package com.blueamp.handoff

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothA2dp
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothManager
import android.bluetooth.BluetoothProfile
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.util.Log
import androidx.core.content.ContextCompat

/**
 * Toggles the earbuds' A2DP link on the phone side using the hidden
 * BluetoothA2dp.connect()/disconnect() methods via reflection — the same technique
 * Tasker and "Bluetooth Auto Connect" use. When reflection is blocked (some OEM ROMs
 * hide these behind the non-SDK denylist), connect() returns false and the caller
 * falls back to a user-tap notification.
 */
class EarbudController(private val context: Context) {

    private val adapter: BluetoothAdapter? =
        (context.getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter

    @Volatile private var a2dp: BluetoothA2dp? = null

    fun bind(onReady: () -> Unit = {}) {
        adapter?.getProfileProxy(context, object : BluetoothProfile.ServiceListener {
            override fun onServiceConnected(profile: Int, proxy: BluetoothProfile) {
                if (profile == BluetoothProfile.A2DP) {
                    a2dp = proxy as BluetoothA2dp
                    onReady()
                }
            }

            override fun onServiceDisconnected(profile: Int) {
                if (profile == BluetoothProfile.A2DP) a2dp = null
            }
        }, BluetoothProfile.A2DP)
    }

    fun unbind() {
        a2dp?.let { adapter?.closeProfileProxy(BluetoothProfile.A2DP, it) }
        a2dp = null
    }

    @SuppressLint("MissingPermission")
    fun device(mac: String): BluetoothDevice? =
        try { adapter?.getRemoteDevice(mac) } catch (e: Exception) { null }

    @SuppressLint("MissingPermission")
    fun isConnected(device: BluetoothDevice): Boolean =
        a2dp?.getConnectionState(device) == BluetoothProfile.STATE_CONNECTED

    /** @return true if the reflective call dispatched; false if blocked (use fallback). */
    fun connect(device: BluetoothDevice): Boolean = invokeHidden("connect", device)

    fun disconnect(device: BluetoothDevice): Boolean = invokeHidden("disconnect", device)

    private fun invokeHidden(name: String, device: BluetoothDevice): Boolean {
        val proxy = a2dp ?: return false
        return try {
            val method = BluetoothA2dp::class.java.getMethod(name, BluetoothDevice::class.java)
            method.isAccessible = true
            (method.invoke(proxy, device) as? Boolean) ?: true
        } catch (e: Exception) {
            Log.w(TAG, "Hidden BluetoothA2dp.$name unavailable: ${e.message}")
            false
        }
    }

    fun hasPermission(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            ContextCompat.checkSelfPermission(context, Manifest.permission.BLUETOOTH_CONNECT) ==
            PackageManager.PERMISSION_GRANTED

    companion object {
        private const val TAG = "EarbudController"
    }
}
