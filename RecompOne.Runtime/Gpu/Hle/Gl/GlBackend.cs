using System.Runtime.InteropServices;
using Silk.NET.OpenGL;

namespace RecompOne.Runtime.Hle;

public sealed class GlBackend : IGpuBackend
{
    [StructLayout(LayoutKind.Sequential)]
    struct GlVertex { public float X, Y; public uint Color; public int Clut, Texpage; public float U, V; }

    const int MaxVerts = 0x40000;

    readonly GL _gl;
    readonly GlVram _vram;

    uint _vao, _vbo, _presentVao, _presentVbo, _progPrim, _progPresent, _progPresent24;
    uint _presentFbo, _presentTex;
    int _presentW, _presentH;

    readonly GlVertex[] _verts = new GlVertex[MaxVerts];
    int _count;

    HleDrawEnv _env;

    bool _kTransparent;
    int _kBlend, _kSetMask, _kCheckMask;
    int _kTwAndX, _kTwAndY, _kTwOrX, _kTwOrY;
    int _kClipX0, _kClipY0, _kClipX1, _kClipY1;
    int _uTexWindow, _uBlend, _uBlendOpaque, _uSetMask, _uCheckMask;
    int _uPresentOrigin, _uPresentSize, _uPresent24Origin, _uPresent24Size;

    public bool Ready { get; private set; }

    public GlBackend(GL gl) { _gl = gl; _vram = new GlVram(gl); }

    public unsafe void InitGl()
    {
        _vram.Init();

        _progPrim = GlShaders.Build(_gl, GlShaders.PrimVs, GlShaders.PrimFs, "prim");
        _progPresent = GlShaders.Build(_gl, GlShaders.FullscreenVs, GlShaders.PresentFs, "present");
        _progPresent24 = GlShaders.Build(_gl, GlShaders.FullscreenVs, GlShaders.Present24Fs, "present24");
        if (_progPrim == 0 || _progPresent == 0 || _progPresent24 == 0) return;

        _uTexWindow = _gl.GetUniformLocation(_progPrim, "uTexWindow");
        _uBlend = _gl.GetUniformLocation(_progPrim, "uBlend");
        _uBlendOpaque = _gl.GetUniformLocation(_progPrim, "uBlendOpaque");
        _uSetMask = _gl.GetUniformLocation(_progPrim, "uSetMask");
        _uCheckMask = _gl.GetUniformLocation(_progPrim, "uCheckMask");

        _gl.UseProgram(_progPrim);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uVram"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_progPrim, "uScale"), GlVram.Scale);

        _uPresentOrigin = _gl.GetUniformLocation(_progPresent, "uOrigin");
        _uPresentSize = _gl.GetUniformLocation(_progPresent, "uSize");
        _gl.UseProgram(_progPresent);
        _gl.Uniform1(_gl.GetUniformLocation(_progPresent, "uVram"), 0);

        _uPresent24Origin = _gl.GetUniformLocation(_progPresent24, "uOrigin");
        _uPresent24Size = _gl.GetUniformLocation(_progPresent24, "uSize");
        _gl.UseProgram(_progPresent24);
        _gl.Uniform1(_gl.GetUniformLocation(_progPresent24, "uVram"), 0);
        _gl.Uniform1(_gl.GetUniformLocation(_progPresent24, "uScale"), GlVram.Scale);

        _vao = _gl.GenVertexArray();
        _vbo = _gl.GenBuffer();
        _gl.BindVertexArray(_vao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(MaxVerts * sizeof(GlVertex)), null, BufferUsageARB.DynamicDraw);
        uint stride = (uint)sizeof(GlVertex);
        _gl.EnableVertexAttribArray(0); _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, stride, (void*)0);
        _gl.EnableVertexAttribArray(1); _gl.VertexAttribIPointer(1, 1, VertexAttribIType.UnsignedInt, stride, (void*)8);
        _gl.EnableVertexAttribArray(2); _gl.VertexAttribIPointer(2, 1, VertexAttribIType.Int, stride, (void*)12);
        _gl.EnableVertexAttribArray(3); _gl.VertexAttribIPointer(3, 1, VertexAttribIType.Int, stride, (void*)16);
        _gl.EnableVertexAttribArray(4); _gl.VertexAttribPointer(4, 2, VertexAttribPointerType.Float, false, stride, (void*)20);

        // fullscreen quad for present, real vbo since gl_VertexID without arrays does not draw on mesa for some reason?? or i did it wrong?
        _presentVao = _gl.GenVertexArray();
        _presentVbo = _gl.GenBuffer();
        _gl.BindVertexArray(_presentVao);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _presentVbo);
        float[] quad = { -1f, -1f, 1f, -1f, -1f, 1f, 1f, 1f };
        fixed (float* qp = quad)
            _gl.BufferData(BufferTargetARB.ArrayBuffer, (nuint)(quad.Length * sizeof(float)), qp, BufferUsageARB.StaticDraw);
        _gl.EnableVertexAttribArray(0);
        _gl.VertexAttribPointer(0, 2, VertexAttribPointerType.Float, false, 2 * sizeof(float), (void*)0);
        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, 0);

        _presentTex = _gl.GenTexture();
        _gl.BindTexture(TextureTarget.Texture2D, _presentTex);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)GLEnum.Linear);
        _gl.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)GLEnum.Linear);
        _presentFbo = _gl.GenFramebuffer();
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _presentFbo);
        _gl.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, _presentTex, 0);
        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);

        _kClipX1 = 1023; _kClipY1 = 511;
        Ready = true;
    }

    public void SetDrawEnv(in HleDrawEnv env) => _env = env;

    bool DesiredMatches(bool transparent, int blend)
    {
        int twAndX = ~(_env.TwMaskX * 8) & 0xFF, twAndY = ~(_env.TwMaskY * 8) & 0xFF;
        int twOrX = (_env.TwOffX & _env.TwMaskX) * 8, twOrY = (_env.TwOffY & _env.TwMaskY) * 8;
        return _kTransparent == transparent && _kBlend == blend
            && _kSetMask == (_env.SetMask ? 1 : 0) && _kCheckMask == (_env.CheckMask ? 1 : 0)
            && _kTwAndX == twAndX && _kTwAndY == twAndY && _kTwOrX == twOrX && _kTwOrY == twOrY
            && _kClipX0 == _env.ClipX0 && _kClipY0 == _env.ClipY0 && _kClipX1 == _env.ClipX1 && _kClipY1 == _env.ClipY1;
    }

    void Begin(in PrimFlags f, int vertsNeeded)
    {
        bool transparent = f.SemiTrans;
        int blend = f.BlendMode;
        if (_count > 0 && !DesiredMatches(transparent, blend)) Flush();
        if (_count + vertsNeeded > MaxVerts) Flush();

        _kTransparent = transparent; _kBlend = blend;
        _kSetMask = _env.SetMask ? 1 : 0; _kCheckMask = _env.CheckMask ? 1 : 0;
        _kTwAndX = ~(_env.TwMaskX * 8) & 0xFF; _kTwAndY = ~(_env.TwMaskY * 8) & 0xFF;
        _kTwOrX = (_env.TwOffX & _env.TwMaskX) * 8; _kTwOrY = (_env.TwOffY & _env.TwMaskY) * 8;
        _kClipX0 = _env.ClipX0; _kClipY0 = _env.ClipY0; _kClipX1 = _env.ClipX1; _kClipY1 = _env.ClipY1;
    }

    GlVertex V(in HleVertex v, in PrimFlags f)
    {
        uint color = (f.Textured && f.RawTexture) ? 0x808080u : (uint)(v.R | (v.G << 8) | (v.B << 16));
        return new GlVertex
        {
            X = v.X, Y = v.Y,
            Color = color,
            Clut = f.Clut & 0x7FFF,
            Texpage = f.Textured ? (f.TPage & 0x1FF) : 0x8000,
            U = v.U, V = v.V,
        };
    }

    public void DrawTri(in HleVertex a, in HleVertex b, in HleVertex c, in PrimFlags f)
    {
        Begin(f, 3);
        _verts[_count++] = V(a, f); _verts[_count++] = V(b, f); _verts[_count++] = V(c, f);
    }

    public void DrawRect(in HleRect r, in PrimFlags f)
    {
        Begin(f, 6);
        var a = new HleVertex { X = r.X, Y = r.Y, R = r.R, G = r.G, B = r.B, U = r.U, V = r.V };
        var b = new HleVertex { X = r.X + r.W, Y = r.Y, R = r.R, G = r.G, B = r.B, U = (short)(r.U + r.W), V = r.V };
        var c = new HleVertex { X = r.X, Y = r.Y + r.H, R = r.R, G = r.G, B = r.B, U = r.U, V = (short)(r.V + r.H) };
        var d = new HleVertex { X = r.X + r.W, Y = r.Y + r.H, R = r.R, G = r.G, B = r.B, U = (short)(r.U + r.W), V = (short)(r.V + r.H) };
        _verts[_count++] = V(a, f); _verts[_count++] = V(b, f); _verts[_count++] = V(c, f);
        _verts[_count++] = V(b, f); _verts[_count++] = V(d, f); _verts[_count++] = V(c, f);
    }

    public void DrawLine(in HleVertex a, in HleVertex b, in PrimFlags f)
    {
        Begin(f, 6);
        float x1 = a.X, y1 = a.Y;
        float x2 = b.X, y2 = b.Y;
        float dx = x2 - x1, dy = y2 - y1;

        if (dx == 0 && dy == 0)
        {
            LineVert(x1, y1, a, f); LineVert(x1 + 1, y1, a, f); LineVert(x1 + 1, y1 + 1, a, f);
            LineVert(x1 + 1, y1 + 1, a, f); LineVert(x1, y1 + 1, a, f); LineVert(x1, y1, a, f);
            return;
        }

        float xo, yo;
        if (Math.Abs(dx) > Math.Abs(dy)) { xo = 0; yo = 1; if (dx > 0) x2++; else x1++; }
        else { xo = 1; yo = 0; if (dy > 0) y2++; else y1++; }

        LineVert(x1, y1, a, f); LineVert(x2, y2, b, f); LineVert(x2 + xo, y2 + yo, b, f);
        LineVert(x2 + xo, y2 + yo, b, f); LineVert(x1 + xo, y1 + yo, a, f); LineVert(x1, y1, a, f);
    }

    void LineVert(float x, float y, in HleVertex src, in PrimFlags f)
    {
        var v = src; v.X = x; v.Y = y;
        _verts[_count++] = V(v, f);
    }

    public void FillRect(int x, int y, int w, int h, ushort color15) { Flush(); _vram.Fill(x, y, w, h, color15); }
    public void CopyVram(int sx, int sy, int dx, int dy, int w, int h) { Flush(); _vram.CopyRect(sx, sy, dx, dy, w, h); }
    public void WriteVram(int x, int y, int w, int h, ReadOnlySpan<ushort> px) { Flush(); _vram.WriteRect(x, y, w, h, px); }
    public void ReadVram(int x, int y, int w, int h, Span<ushort> px) { Flush(); _vram.ReadRect(x, y, w, h, px); }

    public unsafe void Flush()
    {
        if (_count == 0) return;
        _vram.BindDraw();
        _vram.Barrier();

        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.CullFace);
        _gl.Enable(EnableCap.ScissorTest);
        int sw = _kClipX1 - _kClipX0 + 1, sh = _kClipY1 - _kClipY0 + 1;
        _gl.Scissor(_kClipX0 * GlVram.Scale, _kClipY0 * GlVram.Scale, (uint)Math.Max(0, sw * GlVram.Scale), (uint)Math.Max(0, sh * GlVram.Scale));

        _gl.UseProgram(_progPrim);
        _gl.BindVertexArray(_vao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _vram.Texture);
        _gl.Uniform4(_uTexWindow, _kTwAndX, _kTwAndY, _kTwOrX, _kTwOrY);
        _gl.Uniform1(_uSetMask, _kSetMask == 1 ? 1f : 0f);
        _gl.Uniform1(_uCheckMask, _kCheckMask);
        _gl.Uniform4(_uBlendOpaque, 1f, 1f, 1f, 0f);

        _gl.BindBuffer(BufferTargetARB.ArrayBuffer, _vbo);
        _gl.BufferSubData<GlVertex>(BufferTargetARB.ArrayBuffer, 0, _verts.AsSpan(0, _count));

        if (!_kTransparent)
        {
            _gl.Disable(EnableCap.Blend);
            _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
        }
        else
        {
            _gl.Enable(EnableCap.Blend);
            _gl.BlendFuncSeparate(BlendingFactor.Src1Color, BlendingFactor.Src1Alpha, BlendingFactor.One, BlendingFactor.Zero);
            if (_kBlend == 2)
            {
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
                SetBlend(0f, 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);

                _vram.Barrier();
                _gl.BlendEquationSeparate(BlendEquationModeEXT.FuncReverseSubtract, BlendEquationModeEXT.FuncAdd);
                SetBlend(1f, 1f);
                _gl.Uniform4(_uBlendOpaque, 0f, 0f, 0f, 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
            }
            else
            {
                _gl.BlendEquation(BlendEquationModeEXT.FuncAdd);
                SetBlend(_kBlend switch { 0 => 0.5f, 3 => 0.25f, _ => 1f }, _kBlend == 0 ? 0.5f : 1f);
                _gl.DrawArrays(PrimitiveType.Triangles, 0, (uint)_count);
            }
        }

        _gl.Disable(EnableCap.ScissorTest);
        _count = 0;
    }

    void SetBlend(float src, float dst) => _gl.Uniform4(_uBlend, src, src, src, dst);

    public void Present(in HleDispEnv disp) => PresentDisplay(disp.X, disp.Y, disp.W, disp.H, disp.Rgb24);

    public unsafe (uint tex, int w, int h) PresentDisplay(int dispX, int dispY, int w, int h, bool rgb24 = false, int outW = 0, int outH = 0)
    {
        if (!Ready || w <= 0 || h <= 0) return (0, 0, 0);
        Flush();

        int fbW = w * GlVram.Scale;
        int fbH = h * GlVram.Scale;
        EnsurePresentSize(fbW, fbH);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, _presentFbo);
        _gl.Viewport(0, 0, (uint)fbW, (uint)fbH);
        _gl.Disable(EnableCap.DepthTest);
        _gl.Disable(EnableCap.Blend);
        _gl.Disable(EnableCap.ScissorTest);
        _gl.Disable(EnableCap.CullFace);

        _gl.UseProgram(rgb24 ? _progPresent24 : _progPresent);
        _gl.BindVertexArray(_presentVao);
        _gl.ActiveTexture(TextureUnit.Texture0);
        _gl.BindTexture(TextureTarget.Texture2D, _vram.Texture);
        _gl.Uniform2(rgb24 ? _uPresent24Origin : _uPresentOrigin, (float)dispX, dispY);
        _gl.Uniform2(rgb24 ? _uPresent24Size : _uPresentSize, (float)w, h);
        _gl.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

        _gl.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        return (_presentTex, fbW, fbH);
    }

    unsafe void EnsurePresentSize(int w, int h)
    {
        if (w == _presentW && h == _presentH) return;
        _gl.BindTexture(TextureTarget.Texture2D, _presentTex);
        _gl.TexImage2D(TextureTarget.Texture2D, 0, InternalFormat.Rgba8, (uint)w, (uint)h, 0, PixelFormat.Rgba, PixelType.UnsignedByte, null);
        _presentW = w; _presentH = h;
    }

    public void Dispose()
    {
        _vram.Dispose();
        if (_vbo != 0) _gl.DeleteBuffer(_vbo);
        if (_presentVbo != 0) _gl.DeleteBuffer(_presentVbo);
        if (_vao != 0) _gl.DeleteVertexArray(_vao);
        if (_presentVao != 0) _gl.DeleteVertexArray(_presentVao);
        if (_progPrim != 0) _gl.DeleteProgram(_progPrim);
        if (_progPresent != 0) _gl.DeleteProgram(_progPresent);
        if (_progPresent24 != 0) _gl.DeleteProgram(_progPresent24);
        if (_presentTex != 0) _gl.DeleteTexture(_presentTex);
        if (_presentFbo != 0) _gl.DeleteFramebuffer(_presentFbo);
    }
}
