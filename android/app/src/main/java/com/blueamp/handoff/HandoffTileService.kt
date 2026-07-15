package com.blueamp.handoff

import android.content.Intent
import android.service.quicksettings.Tile
import android.service.quicksettings.TileService
import androidx.core.content.ContextCompat

/**
 * Quick Settings tile for manual switching. Active = earbuds on the phone; a tap sends
 * them the other way via HandoffService (which also tells the PC over RFCOMM). The tile
 * reads real A2DP state while visible, so it stays honest even after automatic handoffs.
 */
class HandoffTileService : TileService() {

    private var earbuds: EarbudController? = null

    override fun onStartListening() {
        super.onStartListening()
        val controller = EarbudController(this)
        earbuds = controller
        controller.bind { refresh() } // A2DP proxy arrives async; refresh again when it does
        refresh()
    }

    override fun onStopListening() {
        earbuds?.unbind()
        earbuds = null
        super.onStopListening()
    }

    override fun onClick() {
        val prefs = Prefs(this)
        if (!prefs.isConfigured) return // tile shows UNAVAILABLE; nothing sane to do

        val onPhone = earbudsOnPhone(prefs)
        val action = if (onPhone) HandoffService.ACTION_TO_PC else HandoffService.ACTION_TO_PHONE
        val intent = Intent(this, HandoffService::class.java).setAction(action)
        ContextCompat.startForegroundService(this, intent)

        // Flip optimistically; onStartListening re-syncs from real state next time.
        qsTile?.let {
            it.state = if (onPhone) Tile.STATE_INACTIVE else Tile.STATE_ACTIVE
            it.updateTile()
        }
    }

    private fun refresh() {
        val tile = qsTile ?: return
        val prefs = Prefs(this)
        tile.label = getString(R.string.tile_label)
        tile.state = when {
            !prefs.isConfigured -> Tile.STATE_UNAVAILABLE
            earbudsOnPhone(prefs) -> Tile.STATE_ACTIVE
            else -> Tile.STATE_INACTIVE
        }
        tile.updateTile()
    }

    private fun earbudsOnPhone(prefs: Prefs): Boolean {
        val controller = earbuds ?: return false
        val mac = prefs.earbudMac ?: return false
        val device = controller.device(mac) ?: return false
        return controller.hasPermission() && controller.isConnected(device)
    }
}
