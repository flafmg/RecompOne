using System.Numerics;

namespace RecompOne.Runtime.Memory;

//to make a ram map similar do pcsxRedux's
public sealed class RamLogger
{
    public const int Width = 2048;
    public const int Height = 1024; 

    readonly uint[] _writeTimestamps = new uint[Width * Height];
    uint _cycle;

    public float  DecayFrames = 90f;
    public Vector4 WriteColor = new(1f, 0.18f, 0.18f, 1f);
    public bool   ShowGreyscale = true;

    public uint Cycle => _cycle;
    public void Tick() => _cycle++;

    public uint GetWriteStamp(int byteIdx) =>
        (uint)byteIdx < (uint)_writeTimestamps.Length ? _writeTimestamps[byteIdx] : 0u;

    public float HeatAt(int byteIdx)
    {
        uint ts = GetWriteStamp(byteIdx);
        return ts == 0 ? 0f : MathF.Max(0f, 1f - (_cycle - ts) / DecayFrames);
    }

    public void RecordWrite(uint physAddr, int bytes)
    {
        for (int i = 0; i < bytes; i++)
        {
            int idx = (int)((physAddr + (uint)i) & 0x1FFFFF);
            if (idx < _writeTimestamps.Length) _writeTimestamps[idx] = _cycle;
        }
    }

    public void BuildTexture(ReadOnlySpan<byte> ram, byte[] output)
    {
        int total = Width * Height;
        float wr = WriteColor.X, wg = WriteColor.Y, wb = WriteColor.Z;

        for (int i = 0; i < total; i++)
        {
            byte b = i < ram.Length ? ram[i] : (byte)0;
            float grey = b / 255f;

            uint ts = _writeTimestamps[i];
            float heat = ts == 0 ? 0f : MathF.Max(0f, 1f - (_cycle - ts) / DecayFrames);

            float r, g, bl;
            if (ShowGreyscale)
            {
                r = grey + (wr - grey) * heat;
                g = grey + (wg - grey) * heat;
                bl = grey + (wb - grey) * heat;
            }
            else r = wr * heat; g = wg * heat; bl = wb * heat;
            
            
            
            int o = i << 2;
            output[o] = (byte)(r  * 255);
            output[o + 1] = (byte)(g  * 255);
            output[o + 2] = (byte)(bl * 255);
            output[o + 3] = 255;
        }
    }
}
