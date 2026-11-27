using System;
using UnityEngine;
using CodexMEColorSpaceFix;

class RenderTargetScopeRegression
{
    private static int passed;
    private static void Check(bool value, string message)
    {
        if (!value) throw new Exception("FAIL " + message);
        passed++;
        Console.WriteLine("PASS " + message);
    }
    private static void DestroySynthetic(RenderTexture target)
    {
        if (ReferenceEquals(RenderTexture.active, target)) throw new InvalidOperationException("ACTIVE_RENDER_TEXTURE_RELEASE");
        target.Release();
        UnityEngine.Object.Destroy(target);
    }
    private static RenderTexture Make() { return new RenderTexture(4, 4, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB); }
    static int Main()
    {
        RenderTexture prior = Make();
        RenderTexture synthetic = Make();
        RenderTexture.active = synthetic;
        bool oldFailed = false;
        try { DestroySynthetic(synthetic); } catch (InvalidOperationException ex) { oldFailed = ex.Message == "ACTIVE_RENDER_TEXTURE_RELEASE"; }
        Check(oldFailed, "old self-test pattern releases the active synthetic target");
        RenderTexture.active = prior;
        GL.sRGBWrite = true;
        var scope = new SelfTestRenderTargetScope();
        RenderTexture.active = synthetic;
        GL.sRGBWrite = false;
        scope.Dispose();
        DestroySynthetic(synthetic);
        Check(ReferenceEquals(RenderTexture.active, prior), "normal self-test cleanup restores pre-test target before release");
        Check(GL.sRGBWrite, "normal cleanup restores write conversion state");
        synthetic = Make();
        scope = new SelfTestRenderTargetScope();
        try { RenderTexture.active = synthetic; GL.sRGBWrite = false; throw new InvalidOperationException("synthetic probe failure"); }
        catch (InvalidOperationException) { }
        finally { scope.Dispose(); DestroySynthetic(synthetic); }
        Check(ReferenceEquals(RenderTexture.active, prior) && GL.sRGBWrite, "exception cleanup restores target and state before release");
        RenderTexture.active = null;
        scope = new SelfTestRenderTargetScope();
        RenderTexture.active = Make();
        scope.Dispose();
        Check(RenderTexture.active == null, "null pre-test target remains null");
        prior = Make(); RenderTexture.active = prior; scope = new SelfTestRenderTargetScope(); UnityEngine.Object.Destroy(prior); RenderTexture.active = Make(); scope.Dispose();
        Check(ReferenceEquals(RenderTexture.active, null), "destroyed previous target falls back to null");
        Console.WriteLine("Render target scope assertions passed: " + passed);
        return 0;
    }
}
