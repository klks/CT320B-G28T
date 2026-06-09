namespace CT320B.UsbApi.Protocol.Status;

/// <summary>
/// CRC-8 used by the printer's binary status/RFID channel — polynomial <c>0x07</c>, init
/// <c>0x00</c>, no input/output reflection, no final XOR (the classic "CRC-8"). This is a direct
/// port of <c>sub_10001090</c> (and the inline copies in the RFID command builders).
/// </summary>
public static class Crc8
{
    public static byte Compute(ReadOnlySpan<byte> data)
    {
        byte crc = 0;
        foreach (byte b in data)
        {
            crc ^= b;
            for (int i = 0; i < 8; i++)
                crc = (crc & 0x80) != 0 ? (byte)((crc << 1) ^ 0x07) : (byte)(crc << 1);
        }
        return crc;
    }
}
