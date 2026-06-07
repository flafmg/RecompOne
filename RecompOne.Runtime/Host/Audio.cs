using Silk.NET.OpenAL;
using ALDevice = Silk.NET.OpenAL.Device;
using ALCtx = Silk.NET.OpenAL.Context;

namespace RecompOne.Runtime.Host;

internal static unsafe class Audio
{
    static ALContext? _alc;
    static AL? _al;
    static ALDevice* _device;
    static ALCtx* _context;

    
    const int NumBuffers = 8;
    const int FramesPerBuffer = 735;

    static uint _source;
    static uint[] _buffers = new uint[NumBuffers];
    static short[] _sampleBuf = new short[FramesPerBuffer * 2];

    public static void Initialize()
    {
        try
        {
            _alc = ALContext.GetApi(true);
            _al = AL.GetApi(true);
            _device = _alc.OpenDevice("");
            if (_device == null)
            {
                Console.Error.WriteLine("[Host] no audio device, audio disabled");
                return;
            }
            _context = _alc.CreateContext(_device, null);
            _alc.MakeContextCurrent(_context);

            _source = _al.GenSource();
            fixed (uint* ptr = _buffers)
                _al.GenBuffers(NumBuffers, ptr);

            //initial empty rihgt
            for (int i = 0; i < _buffers.Length; i++)
            {
                _al.BufferData(_buffers[i], BufferFormat.Stereo16, _sampleBuf, 44100);
                uint b = _buffers[i];
                _al.SourceQueueBuffers(_source, 1, &b);
            }

            _al.SourcePlay(_source);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"[Host] audio init failed: {e.Message}");
        }
    }

    public static void Present(Spu? spu)
    {
        if (_al == null || spu == null) return;

        _al.GetSourceProperty(_source, GetSourceInteger.BuffersProcessed, out int processed);
        while (processed > 0)
        {
            uint buf = 0;
            _al.SourceUnqueueBuffers(_source, 1, &buf);
            
            for (int i = 0; i < FramesPerBuffer; i++)
            {
                var (l, r) = spu.Tick();
                if (XaAudio.Next(out short xl, out short xr))
                {
                    l = (short)Math.Clamp(l + xl, -32768, 32767);
                    r = (short)Math.Clamp(r + xr, -32768, 32767);
                }
                _sampleBuf[i * 2] = l;
                _sampleBuf[i * 2 + 1] = r;
            }

            _al.BufferData(buf, BufferFormat.Stereo16, _sampleBuf, 44100);
            _al.SourceQueueBuffers(_source, 1, &buf);
            processed--;
        }

        _al.GetSourceProperty(_source, GetSourceInteger.SourceState, out int state);
        if (state != (int)SourceState.Playing)
            _al.SourcePlay(_source);
    }

    public static void Shutdown()
    {
        if (_alc == null) return;
        if (_al != null)
        {
            _al.SourceStop(_source);
            _al.DeleteSource(_source);
            _al.DeleteBuffers(_buffers);
        }
        if (_context != null) _alc.DestroyContext(_context);
        if (_device != null) _alc.CloseDevice(_device);
    }
}
