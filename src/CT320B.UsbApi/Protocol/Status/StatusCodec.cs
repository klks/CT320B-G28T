namespace CT320B.UsbApi.Protocol.Status;

/// <summary>
/// Encodes/decodes the printer's binary status &amp; RFID side-channel (distinct from the text
/// TSPL/CPCL stream). Frames are <c>0xDD</c>-delimited with a trailing CRC-8:
/// <c>DD seq &lt;payload…&gt; crc DD</c>, where the CRC covers <c>seq</c> + payload.
/// Confirmed against a native-oracle capture (<c>GetRfidData_req.bin</c> = <c>DD 00 03 01 00 00 51 DD</c>).
/// </summary>
public static class StatusCodec
{
    /// <summary>Frame start/end delimiter byte.</summary>
    public const byte Delimiter = 0xDD;

    /// <summary>RFID data field size the firmware returns/expects (48 bytes).</summary>
    public const int RfidDataFieldSize = 0x30;

    private static readonly byte[] RfidDataPayload = [0x03, 0x01, 0x00, 0x00];
    private static readonly byte[] PrintModePayload = [0x05, 0x01, 0x00, 0x00];

    /// <summary>A decoded status/RFID reply.</summary>
    /// <param name="Type">Response type byte (e.g. 3 = RFID, 5 = print mode).</param>
    /// <param name="Subtype">Response subtype byte (e.g. 0x81 for print mode).</param>
    /// <param name="Data">The data field (length = the frame's big-endian size).</param>
    public readonly record struct StatusReply(byte Type, byte Subtype, byte[] Data);

    /// <summary>
    /// Builds a request packet: <c>DD seq &lt;payload…&gt; crc DD</c>, with
    /// <c>crc = CRC8(seq ++ payload)</c>.
    /// </summary>
    public static byte[] BuildFrame(byte seq, ReadOnlySpan<byte> payload)
    {
        // CRC is over seq followed by the payload.
        Span<byte> crcInput = stackalloc byte[1 + payload.Length];
        crcInput[0] = seq;
        payload.CopyTo(crcInput[1..]);
        byte crc = Crc8.Compute(crcInput);

        var frame = new byte[payload.Length + 4];
        frame[0] = Delimiter;
        frame[1] = seq;
        payload.CopyTo(frame.AsSpan(2));
        frame[^2] = crc;
        frame[^1] = Delimiter;
        return frame;
    }

    /// <summary>The <c>GetRfidData</c> request packet (command 0x0103).</summary>
    public static byte[] GetRfidDataRequest(byte seq) => BuildFrame(seq, RfidDataPayload);

    /// <summary>The <c>GetChitengPrintMode</c> request packet (command 0x0105).</summary>
    public static byte[] GetPrintModeRequest(byte seq) => BuildFrame(seq, PrintModePayload);

    /// <summary>
    /// The <c>GetChitengPrintMemory</c> request packet (slot 53, command 0x0507). The reply is a
    /// type-5, subtype-0x87, 1-byte-data frame; <see cref="TryParsePrintMemory"/> validates it.
    /// </summary>
    public static byte[] GetPrintMemoryRequest(byte seq) => BuildFrame(seq, [0x05, 0x07, 0x00, 0x00]);

    /// <summary>
    /// Chiteng status sub-command (slot 50, command 0x0503) carrying one argument byte. The
    /// firmware derives <paramref name="arg"/> from a float via <c>(uint)f</c>. Frame:
    /// <c>DD seq 05 03 00 00 00 arg crc DD</c>. (Proprietary semantics — see docs/protocol_internal.md §6.)
    /// </summary>
    public static byte[] StatusCmd0503Request(byte seq, byte arg) =>
        BuildFrame(seq, [0x05, 0x03, 0x00, 0x00, 0x00, arg]);

    /// <summary>Chiteng status sub-command (slot 51, command 0x0502) carrying one argument byte.</summary>
    public static byte[] StatusCmd0502Request(byte seq, byte arg) =>
        BuildFrame(seq, [0x05, 0x02, 0x00, 0x01, arg]);

    /// <summary>Chiteng status sub-command (slot 52, command 0x0507/sub 0x01) carrying one argument byte.</summary>
    public static byte[] StatusCmd0507Request(byte seq, byte arg) =>
        BuildFrame(seq, [0x05, 0x07, 0x00, 0x01, arg]);

    /// <summary>
    /// Parses any status/RFID reply: <c>DD ? type subtype sizeHi sizeLo &lt;data[size]&gt; crc</c>,
    /// where <c>size</c> is big-endian and <c>crc</c> (CRC-8 over bytes <c>[1 .. 5+size]</c>) must
    /// match. Generalizes the per-command parsers (RFID <c>sub_10008760</c>, print mode
    /// <c>sub_10008120</c>, etc. — same frame, different type/size).
    /// </summary>
    public static bool TryParseReply(ReadOnlySpan<byte> response, out StatusReply reply)
    {
        reply = default;
        if (response.Length < 7) return false;             // DD + 5 header + crc minimum
        if (response[0] != Delimiter) return false;

        int size = (response[4] << 8) | response[5];
        int crcCoverage = 5 + size;                        // bytes [1 .. 5+size]
        if (response.Length < 6 + size + 1) return false;  // header(6) + data + crc

        byte crcField = response[6 + size];
        if (Crc8.Compute(response.Slice(1, crcCoverage)) != crcField) return false;

        reply = new StatusReply(response[2], response[3], response.Slice(6, size).ToArray());
        return true;
    }

    /// <summary>
    /// Parses an RFID reply: <c>DD ? type ? sizeHi sizeLo &lt;48-byte data&gt; crc …</c>, where
    /// <c>type==3</c>, <c>size==0x30</c> (big-endian), and <c>crc</c> (CRC-8 over bytes [1..53])
    /// must match. On success, <paramref name="data"/> receives the 48-byte data field.
    /// Mirrors <c>ReceiveRfidCmd</c> (<c>sub_10008760</c>).
    /// </summary>
    public static bool TryParseRfidResponse(ReadOnlySpan<byte> response, out byte[] data)
    {
        data = [];
        if (!TryParseReply(response, out StatusReply reply)) return false;
        if (reply.Type != 3 || reply.Data.Length != RfidDataFieldSize) return false;
        data = reply.Data;
        return true;
    }

    /// <summary>
    /// Parses a <c>GetChitengPrintMemory</c> reply (slot 53): requires <c>type==5</c>,
    /// <c>subtype==0x87</c> and a single data byte (CRC-validated). Mirrors <c>sub_100085B0</c>.
    /// </summary>
    public static bool TryParsePrintMemory(ReadOnlySpan<byte> response, out byte value)
    {
        value = 0;
        if (!TryParseReply(response, out StatusReply reply)) return false;
        if (reply.Type != 5 || reply.Subtype != 0x87 || reply.Data.Length < 1) return false;
        value = reply.Data[0];
        return true;
    }
}
