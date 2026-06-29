using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public sealed class GlVram
{
    public const int Scale = 4;
    public const int Width = VramShadow.Width * Scale;
    public const int Height = VramShadow.Height * Scale;

    readonly GL _gl;
    uint _drawTex, _readTex;
    uint _drawFbo, _readFbo;
    bool _readDirty = true;
    ushort[] _writeBuf = [];
    ushort[] _readBuf = [];

    public uint DrawTex => _drawTex;
    public uint ReadTex => _readTex;

    public GlVram(GL gl) => _gl = gl;

    public void Init()
    {
        _drawTex = CreateTex();
        _readTex = CreateTex();
        _drawFbo = CreateFbo(_drawTex);
        _readFbo = CreateFbo(_readTex);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
    }

    uint CreateTex()
    {
        uint t = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, t);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Nearest);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)GLEnum.ClampToEdge);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)GLEnum.ClampToEdge);
        _gl.TexImage2D<ushort>(TextureTarget.Texture2D, 0, InternalFormat.Rgb5A1, Width, Height, 0,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, new ushort[Width * Height].AsSpan());
        return t;
    }

    uint CreateFbo(uint tex)
    {
        uint f = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, f);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, tex, 0);
        return f;
    }

    public void BindDraw()
    {
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFbo);
        _gl.Viewport(0, 0, Width, Height);
    }

    public void MarkDrawn() => _readDirty = true;

    public void SyncRead()
    {
        if (!_readDirty) return;
        _readDirty = false;
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _drawFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _readFbo);
        _gl.BlitFramebuffer(0, 0, Width, Height, 0, 0, Width, Height,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFbo);
    }

    public void WriteRect(int x, int y, int w, int h, ReadOnlySpan<ushort> px)
    {
        int scaledW = w * Scale;
        int scaledH = h * Scale;
        int needed = scaledW * scaledH;
        if (_writeBuf.Length < needed) _writeBuf = new ushort[needed];

        for (int r = 0; r < h; r++)
        {
            int srcRow = r * w;
            for (int c = 0; c < w; c++)
            {
                ushort val = px[srcRow + c];
                for (int dr = 0; dr < Scale; dr++)
                {
                    int dstIndex = (r * Scale + dr) * scaledW + (c * Scale);
                    _writeBuf.AsSpan(dstIndex, Scale).Fill(val);
                }
            }
        }

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        _gl.BindTexture(TextureTarget.Texture2D, _drawTex);
        _gl.PixelStore(PixelStoreParameter.UnpackAlignment, 2);
        _gl.TexSubImage2D<ushort>(TextureTarget.Texture2D, 0, x * Scale, y * Scale, (uint)scaledW, (uint)scaledH,
            PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, _writeBuf);
        _readDirty = true;
    }

    public void Fill(int x, int y, int w, int h, ushort color15)
    {
        float r = (color15 & 0x1F) / 31f, g = ((color15 >> 5) & 0x1F) / 31f, b = ((color15 >> 10) & 0x1F) / 31f;
        float a = (color15 & 0x8000) != 0 ? 1f : 0f;
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFbo);
        _gl.Enable(EnableCap.ScissorTest);
        _gl.Scissor(x * Scale, y * Scale, (uint)Math.Max(0, w * Scale), (uint)Math.Max(0, h * Scale));
        _gl.ClearColor(r, g, b, a);
        _gl.Clear(ClearBufferMask.ColorBufferBit);
        _gl.Disable(EnableCap.ScissorTest);
        _readDirty = true;
    }

    public void CopyRect(int sx, int sy, int dx, int dy, int w, int h)
    {
        SyncRead();
        _gl.Disable(EnableCap.ScissorTest);
        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _readFbo);
        _gl.BindFramebuffer(FramebufferTarget.DrawFramebuffer, _drawFbo);
        _gl.BlitFramebuffer(sx * Scale, sy * Scale, (sx + w) * Scale, (sy + h) * Scale,
            dx * Scale, dy * Scale, (dx + w) * Scale, (dy + h) * Scale,
            ClearBufferMask.ColorBufferBit, BlitFramebufferFilter.Nearest);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFbo);
        _readDirty = true;
    }

    public void ReadRect(int x, int y, int w, int h, Span<ushort> dst)
    {
        int scaledW = w * Scale;
        int scaledH = h * Scale;
        int needed = scaledW * scaledH;
        if (_readBuf.Length < needed) _readBuf = new ushort[needed];

        _gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, _drawFbo);
        _gl.PixelStore(PixelStoreParameter.PackAlignment, 2);
        _gl.ReadPixels<ushort>(x * Scale, y * Scale, (uint)scaledW, (uint)scaledH, PixelFormat.Rgba, PixelType.UnsignedShort1555Rev, _readBuf);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _drawFbo);

        for (int r = 0; r < h; r++)
        {
            int srcRow = r * Scale * scaledW;
            int dstRow = r * w;
            for (int c = 0; c < w; c++)
            {
                dst[dstRow + c] = _readBuf[srcRow + c * Scale];
            }
        }
    }

    public void Dispose()
    {
        if (_drawFbo != 0) _gl.DeleteFramebuffer(_drawFbo);
        if (_readFbo != 0) _gl.DeleteFramebuffer(_readFbo);
        if (_drawTex != 0) _gl.DeleteTexture(_drawTex);
        if (_readTex != 0) _gl.DeleteTexture(_readTex);
    }
}
