package com.blueamp.handoff

import android.Manifest
import android.annotation.SuppressLint
import android.bluetooth.BluetoothManager
import android.content.Context
import android.content.pm.PackageManager
import android.os.Build
import android.os.Bundle
import android.util.TypedValue
import android.view.View
import android.widget.Button
import android.widget.LinearLayout
import android.widget.ScrollView
import android.widget.TextView
import android.widget.Toast
import androidx.appcompat.app.AlertDialog
import androidx.appcompat.app.AppCompatActivity
import androidx.appcompat.widget.SwitchCompat
import androidx.core.app.ActivityCompat
import androidx.core.content.ContextCompat

/**
 * One-screen setup: pick the PC and the earbuds from the paired-device list, choose the
 * earbud mode, then start the always-on HandoffService. Everything persists in Prefs,
 * so once configured this screen never needs to be opened again.
 */
class MainActivity : AppCompatActivity() {

    private lateinit var prefs: Prefs
    private lateinit var pcButton: Button
    private lateinit var earbudButton: Button
    private lateinit var singlePointSwitch: SwitchCompat
    private lateinit var startButton: Button
    private lateinit var statusText: TextView

    override fun onCreate(savedInstanceState: Bundle?) {
        super.onCreate(savedInstanceState)
        prefs = Prefs(this)
        setContentView(buildUi())
        requestNeededPermissions()
        refresh()
    }

    // ── UI (built in code — one screen doesn't earn a layout file) ─────────────

    private fun buildUi(): View {
        val pad = dp(20)
        val column = LinearLayout(this).apply {
            orientation = LinearLayout.VERTICAL
            setPadding(pad, pad, pad, pad)
        }

        column.addView(TextView(this).apply {
            text = "BlueAmp Handoff"
            setTextSize(TypedValue.COMPLEX_UNIT_SP, 22f)
        })
        column.addView(TextView(this).apply {
            text = "Switches your earbuds between this phone and the PC running BlueAmp — " +
                "automatically when you walk out of range, or manually from the Quick Settings tile."
            setPadding(0, dp(8), 0, dp(16))
        })

        pcButton = Button(this).also { it.setOnClickListener { pickDevice("Select the BlueAmp PC") { mac -> prefs.pcMac = mac; refresh() } } }
        column.addView(pcButton)

        earbudButton = Button(this).also { it.setOnClickListener { pickDevice("Select your earbuds") { mac -> prefs.earbudMac = mac; refresh() } } }
        column.addView(earbudButton)

        singlePointSwitch = SwitchCompat(this).apply {
            text = "Single-point earbuds (can hold only one connection)"
            setPadding(0, dp(12), 0, dp(12))
            isChecked = prefs.singlePoint
            setOnCheckedChangeListener { _, checked -> prefs.singlePoint = checked }
        }
        column.addView(singlePointSwitch)

        startButton = Button(this).also {
            it.text = "Start handoff service"
            it.setOnClickListener { startService() }
        }
        column.addView(startButton)

        statusText = TextView(this).apply { setPadding(0, dp(16), 0, 0) }
        column.addView(statusText)

        return ScrollView(this).apply { addView(column) }
    }

    private fun refresh() {
        pcButton.text = "PC: ${deviceLabel(prefs.pcMac) ?: "not set"}"
        earbudButton.text = "Earbuds: ${deviceLabel(prefs.earbudMac) ?: "not set"}"
        startButton.isEnabled = prefs.isConfigured
        statusText.text = when {
            !hasBluetoothPermission() ->
                "Bluetooth permission is required to see paired devices."
            !prefs.isConfigured ->
                "Pick both devices above, then start the service."
            else ->
                "Mode: ${prefs.mode}. The service keeps running in the background; " +
                    "use the notification buttons or the Quick Settings tile to switch manually."
        }
    }

    // ── Device picking ──────────────────────────────────────────────────────────

    @SuppressLint("MissingPermission")
    private fun pickDevice(title: String, onPicked: (String) -> Unit) {
        if (!hasBluetoothPermission()) {
            requestNeededPermissions()
            return
        }
        val adapter = (getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter
        val bonded = adapter?.bondedDevices?.sortedBy { it.name ?: it.address }.orEmpty()
        if (bonded.isEmpty()) {
            Toast.makeText(this, "No paired Bluetooth devices found", Toast.LENGTH_LONG).show()
            return
        }
        val labels = bonded.map { "${it.name ?: "Unknown"}  (${it.address})" }.toTypedArray()
        AlertDialog.Builder(this)
            .setTitle(title)
            .setItems(labels) { _, index -> onPicked(bonded[index].address) }
            .show()
    }

    @SuppressLint("MissingPermission")
    private fun deviceLabel(mac: String?): String? {
        mac ?: return null
        if (!hasBluetoothPermission()) return mac
        val adapter = (getSystemService(Context.BLUETOOTH_SERVICE) as? BluetoothManager)?.adapter
        return try { adapter?.getRemoteDevice(mac)?.name ?: mac } catch (e: Exception) { mac }
    }

    // ── Service ─────────────────────────────────────────────────────────────────

    private fun startService() {
        if (!prefs.isConfigured) return
        HandoffService.start(this)
        Toast.makeText(this, "Handoff service started", Toast.LENGTH_SHORT).show()
        refresh()
    }

    // ── Permissions ─────────────────────────────────────────────────────────────

    private fun hasBluetoothPermission(): Boolean =
        Build.VERSION.SDK_INT < Build.VERSION_CODES.S ||
            ContextCompat.checkSelfPermission(this, Manifest.permission.BLUETOOTH_CONNECT) ==
            PackageManager.PERMISSION_GRANTED

    private fun requestNeededPermissions() {
        val missing = mutableListOf<String>()
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.S && !hasBluetoothPermission()) {
            missing += Manifest.permission.BLUETOOTH_CONNECT
        }
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.TIRAMISU &&
            ContextCompat.checkSelfPermission(this, Manifest.permission.POST_NOTIFICATIONS) !=
            PackageManager.PERMISSION_GRANTED
        ) {
            missing += Manifest.permission.POST_NOTIFICATIONS
        }
        if (missing.isNotEmpty()) {
            ActivityCompat.requestPermissions(this, missing.toTypedArray(), PERMISSION_REQUEST)
        }
    }

    override fun onRequestPermissionsResult(requestCode: Int, permissions: Array<out String>, grantResults: IntArray) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults)
        if (requestCode == PERMISSION_REQUEST) refresh()
    }

    private fun dp(value: Int): Int =
        (value * resources.displayMetrics.density).toInt()

    companion object {
        private const val PERMISSION_REQUEST = 1
    }
}
