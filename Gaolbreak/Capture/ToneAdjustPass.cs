using TerraFX.Interop.DirectX;
using Texture = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.Texture;
using TextureFormat = FFXIVClientStructs.FFXIV.Client.Graphics.Kernel.TextureFormat;

namespace Gaolbreak.Capture;

internal sealed unsafe class ToneAdjustPass : IDisposable
{
    private const string ShaderSource = """
        Texture2D g_Input : register(t0);
        SamplerState g_Sampler : register(s0);

        struct VSOutput
        {
            float4 pos : SV_POSITION;
            float2 uv : TEXCOORD0;
        };

        VSOutput VS(uint id : SV_VertexID)
        {
            VSOutput o;
            float2 uv = float2((id << 1) & 2, id & 2);
            o.pos = float4(uv * float2(2, -2) + float2(-1, 1), 0, 1);
            o.uv = uv;
            return o;
        }

        float4 UnpremultPS(VSOutput i) : SV_TARGET
        {
            float4 c = g_Input.Sample(g_Sampler, i.uv);
            return float4(c.a > 0.0 ? c.rgb / c.a : c.rgb, c.a);
        }

        float4 RepremultPS(VSOutput i) : SV_TARGET
        {
            float4 c = g_Input.Sample(g_Sampler, i.uv);
            return float4(c.a > 0.0 ? c.rgb * c.a : c.rgb, c.a);
        }
        """;

    private readonly ID3D11Device* device;
    private readonly ID3D11VertexShader* vs;
    private readonly ID3D11PixelShader* unpremultPs;
    private readonly ID3D11PixelShader* repremultPs;
    private readonly ID3D11BlendState* opaqueBlend;
    private readonly ID3D11RasterizerState* rasterizer;
    private readonly string? compileError;

    private readonly GraphicsPipelineSnapshotter pipeline = new();
    private readonly GameTexture unpremult = new();
    private readonly GameTexture toneAdj = new();

    public ToneAdjustPass(ID3D11Device* device)
    {
        this.device = device;
        compileError = ShaderCompiler.CompileVS(device, ShaderSource, "VS", out vs)
            ?? ShaderCompiler.CompilePS(device, ShaderSource, "UnpremultPS", out unpremultPs)
            ?? ShaderCompiler.CompilePS(device, ShaderSource, "RepremultPS", out repremultPs);

        if (compileError is not null)
        {
            Plugin.Log.Warning($"Failed to compile ToneAdjustPass shader: {compileError}");
        }

        var blendDesc = new D3D11_BLEND_DESC();
        blendDesc.RenderTarget[0] = new D3D11_RENDER_TARGET_BLEND_DESC
        {
            BlendEnable = false,
            SrcBlend = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlend = D3D11_BLEND.D3D11_BLEND_ZERO,
            BlendOp = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            SrcBlendAlpha = D3D11_BLEND.D3D11_BLEND_ONE,
            DestBlendAlpha = D3D11_BLEND.D3D11_BLEND_ZERO,
            BlendOpAlpha = D3D11_BLEND_OP.D3D11_BLEND_OP_ADD,
            RenderTargetWriteMask = (byte)D3D11_COLOR_WRITE_ENABLE.D3D11_COLOR_WRITE_ENABLE_ALL,
        };
        ID3D11BlendState* blend;
        if (device->CreateBlendState(&blendDesc, &blend) >= 0) opaqueBlend = blend;

        var rasterDesc = new D3D11_RASTERIZER_DESC
        {
            FillMode = D3D11_FILL_MODE.D3D11_FILL_SOLID,
            CullMode = D3D11_CULL_MODE.D3D11_CULL_NONE,
            DepthClipEnable = true,
        };
        ID3D11RasterizerState* raster;
        if (device->CreateRasterizerState(&rasterDesc, &raster) >= 0) rasterizer = raster;
    }

    internal readonly unsafe ref struct Scope : IDisposable
    {
        private readonly ToneAdjustPass? pass;
        private readonly ID3D11DeviceContext* context;

        internal Scope(ToneAdjustPass pass, ID3D11DeviceContext* context)
        {
            this.pass = pass;
            this.context = context;
        }

        public void Apply(GameTexture tex) => pass?.Apply(context, tex);
        public void Dispose() => pass?.End(context);
    }

    public Scope Begin(ID3D11DeviceContext* context, Texture* toneAdjustSource)
    {
        if (compileError != null || opaqueBlend == null || rasterizer == null || toneAdjustSource == null || toneAdjustSource->D3D11Texture2D == null)
        {
            return default;
        }

        ID3D11ShaderResourceView* slot0;
        context->PSGetShaderResources(0, 1, &slot0);
        bool isToneAdjActive = slot0 != null && IsViewOf(slot0, toneAdjustSource);
        if (slot0 != null) slot0->Release();
        if (!isToneAdjActive)
        {
            return default;
        }

        pipeline.Snapshot(context);

        context->VSSetShader(vs, null, 0);
        context->IASetInputLayout(null);
        context->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY.D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        context->RSSetState(rasterizer);
        float* zero = stackalloc float[4];
        context->OMSetBlendState(opaqueBlend, zero, 0xFFFFFFFF);
        return new Scope(this, context);
    }

    private void Apply(ID3D11DeviceContext* context, GameTexture tex)
    {
        if (!Ensure(tex.Width, tex.Height)) return;

        var vp = new D3D11_VIEWPORT { Width = tex.Width, Height = tex.Height, MaxDepth = 1f };
        context->RSSetViewports(1, &vp);

        Draw(context, unpremultPs, tex.Srv, unpremult.Rtv);
        Draw(context, pipeline.PixelShader, unpremult.Srv, toneAdj.Rtv);
        Draw(context, repremultPs, toneAdj.Srv, tex.Rtv);
    }

    private static void Draw(ID3D11DeviceContext* context, ID3D11PixelShader* ps, ID3D11ShaderResourceView* src, ID3D11RenderTargetView* dst)
    {
        ID3D11ShaderResourceView* nullSrv = null;
        context->OMSetRenderTargets(0, null, null);
        context->PSSetShader(ps, null, 0);
        context->PSSetShaderResources(0, 1, &src);
        context->OMSetRenderTargets(1, &dst, null);
        context->Draw(3, 0);
        context->PSSetShaderResources(0, 1, &nullSrv);
    }

    private void End(ID3D11DeviceContext* context)
    {
        pipeline.Restore(context);
    }

    private bool Ensure(uint width, uint height)
    {
        if (unpremult.IsValid && unpremult.Width == width && unpremult.Height == height) return true;
        unpremult.Dispose();
        toneAdj.Dispose();
        if (!unpremult.Create(width, height, TextureFormat.R16G16B16A16_FLOAT)
            || !toneAdj.Create(width, height, TextureFormat.R16G16B16A16_FLOAT))
        {
            unpremult.Dispose();
            toneAdj.Dispose();
            return false;
        }
        return true;
    }

    private static bool IsViewOf(ID3D11ShaderResourceView* srv, Texture* tex)
    {
        if (srv == tex->D3D11ShaderResourceView) return true;
        ID3D11Resource* res;
        srv->GetResource(&res);
        if (res == null) return false;
        bool match = res == (ID3D11Resource*)tex->D3D11Texture2D;
        res->Release();
        return match;
    }

    public void Dispose()
    {
        pipeline.Release();
        unpremult.Dispose();
        toneAdj.Dispose();
        if (vs != null) vs->Release();
        if (unpremultPs != null) unpremultPs->Release();
        if (repremultPs != null) repremultPs->Release();
        if (opaqueBlend != null) opaqueBlend->Release();
        if (rasterizer != null) rasterizer->Release();
    }
}
