package com.blueamp.handoff

import org.json.JSONObject
import java.util.UUID

/**
 * The PC↔phone wire protocol. Must stay byte-for-byte compatible with
 * HandoffChannelService.cs on the BlueAmp (Windows) side: the same service UUID and
 * the same newline-delimited JSON messages.
 */
object Protocol {
    /** Must equal HandoffServiceUuid in HandoffChannelService.cs. */
    val SERVICE_UUID: UUID = UUID.fromString("6f2b1a30-9c8d-4e7f-ab12-3c4d5e6f7a8b")

    const val HELLO = "HELLO"
    const val EARBUDS_TO_PHONE = "EARBUDS_TO_PHONE"
    const val EARBUDS_TO_PC = "EARBUDS_TO_PC"
    const val PHONE_STATUS = "PHONE_STATUS"

    // Earbud-state strings mirror the C# EarbudLocation enum names.
    const val STATE_ON_PC = "OnPc"
    const val STATE_ON_PHONE = "OnPhone"
    const val STATE_FREE = "Free"
    const val STATE_UNKNOWN = "Unknown"

    fun command(type: String): String = JSONObject().put("type", type).toString()

    fun phoneStatus(state: String): String =
        JSONObject().put("type", PHONE_STATUS).put("earbudState", state).toString()

    fun hello(mode: String, state: String): String =
        JSONObject().put("type", HELLO).put("mode", mode).put("earbudState", state).toString()

    fun typeOf(line: String): String? =
        try { JSONObject(line).optString("type").ifEmpty { null } } catch (e: Exception) { null }
}
