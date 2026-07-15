package com.blueamp.handoff

import android.annotation.SuppressLint
import android.bluetooth.BluetoothAdapter
import android.bluetooth.BluetoothSocket
import android.util.Log
import java.io.BufferedReader
import java.io.InputStreamReader
import java.io.OutputStream
import kotlin.concurrent.thread

/**
 * RFCOMM client to the BlueAmp PC. A background thread keeps a socket open, reads
 * newline-delimited JSON, and reconnects with capped exponential backoff whenever the
 * link drops (e.g. after a range trip). The connection existing is itself the PC's
 * "phone is back in range" signal — no polling on either side.
 */
class RfcommClient(
    private val pcMac: String,
    private val onLine: (String) -> Unit,
    private val onConnected: () -> Unit,
    private val onDisconnected: () -> Unit,
) {
    @Volatile private var running = false
    @Volatile private var socket: BluetoothSocket? = null
    @Volatile private var out: OutputStream? = null

    val isConnected: Boolean get() = socket?.isConnected == true

    fun start() {
        if (running) return
        running = true
        thread(name = "blueamp-rfcomm") { loop() }
    }

    fun stop() {
        running = false
        closeSocket()
    }

    @SuppressLint("MissingPermission")
    private fun loop() {
        val adapter = BluetoothAdapter.getDefaultAdapter() ?: return
        var backoffMs = MIN_BACKOFF_MS

        while (running) {
            var wasConnected = false
            try {
                val device = adapter.getRemoteDevice(pcMac)
                val s = device.createRfcommSocketToServiceRecord(Protocol.SERVICE_UUID)
                adapter.cancelDiscovery() // discovery slows down / breaks connect()
                s.connect()

                socket = s
                out = s.outputStream
                backoffMs = MIN_BACKOFF_MS
                wasConnected = true
                onConnected()

                val reader = BufferedReader(InputStreamReader(s.inputStream))
                while (running) {
                    val line = reader.readLine() ?: break // null = peer closed
                    if (line.isNotBlank()) onLine(line)
                }
            } catch (e: Exception) {
                Log.i(TAG, "RFCOMM session ended: ${e.message}")
            } finally {
                closeSocket()
                if (wasConnected) onDisconnected()
            }

            if (!running) break
            try { Thread.sleep(backoffMs) } catch (e: InterruptedException) { break }
            backoffMs = (backoffMs * 2).coerceAtMost(MAX_BACKOFF_MS)
        }
    }

    @Synchronized
    fun send(message: String) {
        val stream = out ?: return
        try {
            stream.write((message + "\n").toByteArray(Charsets.UTF_8))
            stream.flush()
        } catch (e: Exception) {
            Log.w(TAG, "RFCOMM send failed: ${e.message}")
        }
    }

    private fun closeSocket() {
        try { socket?.close() } catch (_: Exception) {}
        socket = null
        out = null
    }

    companion object {
        private const val TAG = "RfcommClient"
        private const val MIN_BACKOFF_MS = 2_000L
        private const val MAX_BACKOFF_MS = 30_000L
    }
}
