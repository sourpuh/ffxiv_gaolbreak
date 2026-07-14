using TerraFX.Interop.DirectX;

namespace Gaolbreak.Capture;

internal sealed unsafe class GraphicsPipelineSnapshotter
{
    private const uint RtvCount = D3D11.D3D11_SIMULTANEOUS_RENDER_TARGET_COUNT;
    private const uint MaxViewports = D3D11.D3D11_VIEWPORT_AND_SCISSORRECT_OBJECT_COUNT_PER_PIPELINE;

    private readonly ID3D11RenderTargetView*[] rtvs = new ID3D11RenderTargetView*[RtvCount];
    private readonly D3D11_VIEWPORT[] viewports = new D3D11_VIEWPORT[MaxViewports];
    private ID3D11DepthStencilView* dsv;
    private ID3D11BlendState* blend;
    private ID3D11RasterizerState* raster;
    private ID3D11ShaderResourceView* srv;
    private ID3D11PixelShader* ps;
    private ID3D11VertexShader* vs;
    private ID3D11InputLayout* layout;
    private D3D_PRIMITIVE_TOPOLOGY topology;
    private uint sampleMask;
    private uint viewportCount;
    private readonly float[] blendFactor = new float[4];

    public ID3D11PixelShader* PixelShader => ps;

    public void Snapshot(ID3D11DeviceContext* context)
    {
        fixed (ID3D11RenderTargetView** r = rtvs)
        {
            ID3D11DepthStencilView* d;
            context->OMGetRenderTargets(RtvCount, r, &d);
            dsv = d;
        }
        ID3D11BlendState* b;
        uint sm;
        fixed (float* f = blendFactor)
            context->OMGetBlendState(&b, f, &sm);
        blend = b;
        sampleMask = sm;

        ID3D11RasterizerState* rs;
        context->RSGetState(&rs);
        raster = rs;

        ID3D11PixelShader* p;
        context->PSGetShader(&p, null, null);
        ps = p;

        ID3D11VertexShader* v;
        context->VSGetShader(&v, null, null);
        vs = v;

        ID3D11InputLayout* il;
        context->IAGetInputLayout(&il);
        layout = il;

        D3D_PRIMITIVE_TOPOLOGY t;
        context->IAGetPrimitiveTopology(&t);
        topology = t;

        ID3D11ShaderResourceView* s;
        context->PSGetShaderResources(0, 1, &s);
        srv = s;

        uint vc = MaxViewports;
        fixed (D3D11_VIEWPORT* vp = viewports)
            context->RSGetViewports(&vc, vp);
        viewportCount = vc;
    }

    public void Restore(ID3D11DeviceContext* context)
    {
        fixed (ID3D11RenderTargetView** r = rtvs)
            context->OMSetRenderTargets(RtvCount, r, dsv);
        fixed (float* f = blendFactor)
            context->OMSetBlendState(blend, f, sampleMask);
        context->RSSetState(raster);
        context->VSSetShader(vs, null, 0);
        context->PSSetShader(ps, null, 0);
        context->IASetInputLayout(layout);
        context->IASetPrimitiveTopology(topology);
        ID3D11ShaderResourceView* s = srv;
        context->PSSetShaderResources(0, 1, &s);
        if (viewportCount > 0)
        {
            fixed (D3D11_VIEWPORT* vp = viewports)
                context->RSSetViewports(viewportCount, vp);
        }
        Release();
    }

    public void Release()
    {
        for (uint i = 0; i < RtvCount; i++)
            if (rtvs[i] != null) { rtvs[i]->Release(); rtvs[i] = null; }
        if (dsv != null) { dsv->Release(); dsv = null; }
        if (blend != null) { blend->Release(); blend = null; }
        if (raster != null) { raster->Release(); raster = null; }
        if (srv != null) { srv->Release(); srv = null; }
        if (ps != null) { ps->Release(); ps = null; }
        if (vs != null) { vs->Release(); vs = null; }
        if (layout != null) { layout->Release(); layout = null; }
    }
}
