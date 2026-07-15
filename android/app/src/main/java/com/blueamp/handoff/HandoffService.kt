package com.blueamp.handoff

import android.app.Notification
import android.app.NotificationChannel
import android.app.NotificationManager
import android.app.PendingIntent
import android.app.Service
import android.bluetooth.BluetoothA2dp
import android.bluetooth.BluetoothDevice
import android.bluetooth.BluetoothProfile
import android.content.BroadcastReceiver
import android.content.Context
import android.content.Intent
import android.content.IntentFilter
import android.content.pm.ServiceInfo
import android.os.Build
import android.os.Handler
import android.os.IBinder
import android.os.Looper
import android.util.Log
import androidx.core.app.NotificationCompat
import androidx.core.content.ContextCompat

/**
 * The always-running brain of the companion app. It:
 *  - keeps the RFCOMM link to the PC alive (and treats that link as the "in range" signal),
 *  - obeys EARBUDS_TO_* commands from the PC,
 *  - on walk-away (PC A2DP link lost) connects the earbuds to the phone after a short
 *    grace period (single-point only; multipoint firmware re-routes on its own),
 *  - exposes manual switching through the Quick Settings tile and notification buttons.
 */
class HandoffService : Service() {

    private lateinit var prefs: Prefs
    private lateinit var earbuds: EarbudController
    private var rfcomm: RfcommClient? = null

    private val main = Handler(Looper.getMainLooper())
    private var pendingWalkAway: Runnable? = null
    private var lastSwitchMs = 0L

    // ── Lifecycle ──────────────────────────────────────────────────────────────

    override fun onCreate() {
        super.onCreate()
        prefs = Prefs(this)
        earbuds = EarbudController(this)
        earbuds.bind()

        createChannel()
        startForegroundCompat(buildNotification(statusText()))

        registerPcLinkReceiver()
        startRfcomm()
    }

    override fun onStartCommand(intent: Intent?, flags: Int, startId: Int): Int {
        when (intent?.action) {
            ACTION_TO_PHONE -> earbudsToPhone(sendToPc = true)
            ACTION_TO_PC -> earbudsToPc(sendToPc = true)
            ACTION_CONNECT_FALLBACK -> connectEarbudsLocal() // user tapped the fallback notification
        }
        return START_STICKY
    }

    override fun onDestroy() {
        pendingWalkAway?.let { main.removeCallbacks(it) }
        runCatching { unregisterReceiver(pcLinkReceiver) }
        rfcomm?.stop()
        earbuds.unbind()
        super.onDestroy()
    }

    override fun onBind(intent: Intent?): IBinder? = null

    // ── RFCOMM link to the PC ───────────────────────────────────────────────────

    private fun startRfcomm() {
        val pc = prefs.pcMac ?: return
        rfcomm = RfcommClient(
            pcMac = pc,
            onLine = { line -> main.post { handleMessage(line) } },
            onConnected = { main.post { onPcConnected() } },
            onDisconnected = { main.post { updateNotification() } },
        ).also { it.start() }
    }

    private fun onPcConnected() {
        // Announce our mode + current earbud state so the PC can sync its display.
        rfcomm?.send(Protocol.hello(prefs.mode, earbudStateString()))
        updateNotification()
    }

    private fun handleMessage(line: String) {
        when (Protocol.typeOf(line)) {
            // PC has already released its side and wants the earbuds on the phone.
            Protocol.EARBUDS_TO_PHONE -> earbudsToPhone(sendToPc = false)
            // PC is about to claim the earbuds; release them here first.
            Protocol.EARBUDS_TO_PC -> earbudsToPc(sendToPc = false)
            Protocol.HELLO -> Log.i(TAG, "PC hello: $line")
        }
    }

    // ── Switching ───────────────────────────────────────────────────────────────

    /** Bring the earbuds onto the phone. */
    private fun earbudsToPhone(sendToPc: Boolean) {
        if (sendToPc) rfcomm?.send(Protocol.command(Protocol.EARBUDS_TO_PHONE))
        connectEarbudsLocal()
        lastSwitchMs = System.currentTimeMillis()
    }

    /** Release the earbuds so the PC can take them. */
    private fun earbudsToPc(sendToPc: Boolean) {
        disconnectEarbudsLocal()
        if (sendToPc) rfcomm?.send(Protocol.command(Protocol.EARBUDS_TO_PC))
        lastSwitchMs = System.currentTimeMillis()
    }

    private fun connectEarbudsLocal() {
        val mac = prefs.earbudMac ?: return
        val device = earbuds.device(mac) ?: return
        if (!earbuds.hasPermission()) { postFallbackNotification(); return }
        if (!earbuds.connect(device)) {
            // Reflection blocked on this ROM — offer a one-tap manual connect instead.
            postFallbackNotification()
        }
        rfcomm?.send(Protocol.phoneStatus(Protocol.STATE_ON_PHONE))
        updateNotification()
    }

    private fun disconnectEarbudsLocal() {
        val mac = prefs.earbudMac ?: return
        val device = earbuds.device(mac) ?: return
        if (earbuds.hasPermission()) earbuds.disconnect(device)
        rfcomm?.send(Protocol.phoneStatus(Protocol.STATE_ON_PC))
        updateNotification()
    }

    // ── Walk-away detection (PC A2DP link lost) ─────────────────────────────────

    private val pcLinkReceiver = object : BroadcastReceiver() {
        override fun onReceive(context: Context, intent: Intent) {
            val device = intent.getParcelableExtra<BluetoothDevice>(BluetoothDevice.EXTRA_DEVICE) ?: return
            if (!device.address.equals(prefs.pcMac, ignoreCase = true)) return

            val disconnected = when (intent.action) {
                BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED ->
                    intent.getIntExtra(BluetoothProfile.EXTRA_STATE, -1) == BluetoothProfile.STATE_DISCONNECTED
                BluetoothDevice.ACTION_ACL_DISCONNECTED -> true
                else -> false
            }
            if (disconnected) onPcLinkLost() else onPcLinkReturned()
        }
    }

    private fun registerPcLinkReceiver() {
        val filter = IntentFilter().apply {
            addAction(BluetoothA2dp.ACTION_CONNECTION_STATE_CHANGED)
            addAction(BluetoothDevice.ACTION_ACL_CONNECTED)
            addAction(BluetoothDevice.ACTION_ACL_DISCONNECTED)
        }
        ContextCompat.registerReceiver(this, pcLinkReceiver, filter, ContextCompat.RECEIVER_NOT_EXPORTED)
    }

    private fun onPcLinkLost() {
        // Multipoint earbuds keep both links and re-route in firmware — nothing to do.
        if (!prefs.singlePoint) return
        if (withinDwell()) return

        // Wait out a brief grace period: a momentary blip shouldn't yank the earbuds.
        pendingWalkAway?.let { main.removeCallbacks(it) }
        val task = Runnable {
            Log.i(TAG, "PC out of range — connecting earbuds to phone")
            earbudsToPhone(sendToPc = false) // PC is gone; no point messaging it
        }
        pendingWalkAway = task
        main.postDelayed(task, WALK_AWAY_GRACE_MS)
    }

    private fun onPcLinkReturned() {
        // A blip resolved before the grace elapsed — cancel the pending handoff.
        pendingWalkAway?.let { main.removeCallbacks(it); pendingWalkAway = null }
    }

    private fun withinDwell(): Boolean =
        System.currentTimeMillis() - lastSwitchMs < DWELL_MS

    // ── State helpers ────────────────────────────────────────────────────────────

    private fun earbudStateString(): String {
        val mac = prefs.earbudMac ?: return Protocol.STATE_UNKNOWN
        val device = earbuds.device(mac) ?: return Protocol.STATE_UNKNOWN
        return if (earbuds.hasPermission() && earbuds.isConnected(device))
            Protocol.STATE_ON_PHONE else Protocol.STATE_FREE
    }

    private fun statusText(): String {
        val pc = if (rfcomm?.isConnected == true) "PC linked" else "PC out of range"
        return "$pc · earbuds ${if (earbudStateString() == Protocol.STATE_ON_PHONE) "on phone" else "free"}"
    }

    // ── Notifications ────────────────────────────────────────────────────────────

    private fun buildNotification(text: String): Notification =
        NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle("BlueAmp Handoff")
            .setContentText(text)
            .setOngoing(true)
            .setPriority(NotificationCompat.PRIORITY_LOW)
            .addAction(0, "Earbuds → phone", servicePendingIntent(ACTION_TO_PHONE, 1))
            .addAction(0, "Earbuds → PC", servicePendingIntent(ACTION_TO_PC, 2))
            .build()

    private fun updateNotification() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        nm.notify(NOTIF_ID, buildNotification(statusText()))
    }

    private fun postFallbackNotification() {
        val nm = getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager
        val notif = NotificationCompat.Builder(this, CHANNEL_ID)
            .setSmallIcon(android.R.drawable.stat_sys_data_bluetooth)
            .setContentTitle("Connect earbuds?")
            .setContentText("Automatic switching is blocked on this device — tap to connect.")
            .setPriority(NotificationCompat.PRIORITY_HIGH)
            .setAutoCancel(true)
            .addAction(0, "Connect earbuds", servicePendingIntent(ACTION_CONNECT_FALLBACK, 3))
            .build()
        nm.notify(FALLBACK_NOTIF_ID, notif)
    }

    private fun servicePendingIntent(action: String, requestCode: Int): PendingIntent {
        val intent = Intent(this, HandoffService::class.java).setAction(action)
        val flags = PendingIntent.FLAG_UPDATE_CURRENT or PendingIntent.FLAG_IMMUTABLE
        return PendingIntent.getForegroundService(this, requestCode, intent, flags)
    }

    private fun startForegroundCompat(notification: Notification) {
        if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
            startForeground(NOTIF_ID, notification, ServiceInfo.FOREGROUND_SERVICE_TYPE_CONNECTED_DEVICE)
        } else {
            startForeground(NOTIF_ID, notification)
        }
    }

    private fun createChannel() {
        if (Build.VERSION.SDK_INT < Build.VERSION_CODES.O) return
        val channel = NotificationChannel(CHANNEL_ID, "Earbud Handoff", NotificationManager.IMPORTANCE_LOW)
        channel.description = "Keeps the PC link alive and switches earbuds"
        (getSystemService(Context.NOTIFICATION_SERVICE) as NotificationManager).createNotificationChannel(channel)
    }

    companion object {
        private const val TAG = "HandoffService"
        private const val CHANNEL_ID = "blueamp_handoff"
        private const val NOTIF_ID = 1
        private const val FALLBACK_NOTIF_ID = 2

        private const val WALK_AWAY_GRACE_MS = 10_000L
        private const val DWELL_MS = 30_000L

        const val ACTION_TO_PHONE = "com.blueamp.handoff.TO_PHONE"
        const val ACTION_TO_PC = "com.blueamp.handoff.TO_PC"
        const val ACTION_CONNECT_FALLBACK = "com.blueamp.handoff.CONNECT_FALLBACK"

        fun start(context: Context) {
            val intent = Intent(context, HandoffService::class.java)
            ContextCompat.startForegroundService(context, intent)
        }
    }
}
