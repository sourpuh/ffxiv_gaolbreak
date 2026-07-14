using System.Text;
using TerraFX.Interop.DirectX;

namespace Gaolbreak.Capture;

internal static unsafe class ShaderCompiler
{
    public static string? CompileVS(ID3D11Device* device, string source, string vsEntry, out ID3D11VertexShader* vs)
    {
        vs = null;
        var error = Compile(source, vsEntry, "vs_5_0", out var vsBlob);
        if (error != null) return $"VS compile failed: {error}";

        ID3D11VertexShader* vsObj;
        var result = device->CreateVertexShader(vsBlob->GetBufferPointer(), vsBlob->GetBufferSize(), null, &vsObj);
        vsBlob->Release();
        if (result < 0) return $"VS creation failed: {result}";
        vs = vsObj;
        return null;
    }

    public static string? CompilePS(ID3D11Device* device, string source, string psEntry, out ID3D11PixelShader* ps)
    {
        ps = null;
        var error = Compile(source, psEntry, "ps_5_0", out var psBlob);
        if (error != null) return $"PS compile failed: {error}";

        ID3D11PixelShader* psObj;
        var result = device->CreatePixelShader(psBlob->GetBufferPointer(), psBlob->GetBufferSize(), null, &psObj);
        psBlob->Release();
        if (result < 0) return $"PS creation failed: {result}";
        ps = psObj;
        return null;
    }

    private static string? Compile(string source, string entry, string target, out ID3DBlob* blob)
    {
        blob = null;
        ID3DBlob* code;
        ID3DBlob* errors = null;
        int result;

        fixed (byte* pSrcData = Encoding.ASCII.GetBytes(source))
        fixed (byte* pEntry = Encoding.ASCII.GetBytes(entry + "\0"))
        fixed (byte* pTarget = Encoding.ASCII.GetBytes(target + "\0"))
        {
            result = DirectX.D3DCompile(pSrcData, (nuint)source.Length, null, null, null,
                (sbyte*)pEntry, (sbyte*)pTarget, 0, 0, &code, &errors);
        }
        if (result < 0)
        {
            string message = "unknown error";
            if (errors != null)
            {
                message = new string((sbyte*)errors->GetBufferPointer()).Trim();
                errors->Release();
            }
            return message;
        }
        if (errors != null) errors->Release();
        blob = code;
        return null;
    }
}
