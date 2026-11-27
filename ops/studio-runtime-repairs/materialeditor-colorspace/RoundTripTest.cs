using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace CodexMEColorSpaceFix;

internal static class RoundTripTest
{
	private const int W = 256;

	private const int H = 8;

	public static void Run(string outDir)
	{
        Texture texture = null, texture2 = null;
        var renderTargets = new SelfTestRenderTargetScope();
        try
        {
		StringBuilder sb = new StringBuilder();
		Line(sb, "===== MaterialEditor texture roundtrip self-test =====");
		Line(sb, string.Concat("ColorSpace: ", QualitySettings.activeColorSpace, "   Ramp: ", 256, "x", 8));
		byte[] png = MakeRampPng();
		byte[] a = ReadRamp(png);
		texture = InvokeTextureFromBytes(png);
		if (texture == null)
		{
			Line(sb, "!! Cannot invoke TextureFromBytes; test stopped.");
			Dump(outDir, sb);
			return;
		}
		bool flag = MEColorSpaceFixPlugin.IsSrgbTexture(texture);
		Line(sb, "");
		Line(sb, "[1] ME raw import: " + texture.GetType().Name + " sRGB=" + flag);
		byte[] array = ReadStored(texture);
		Line(sb, "    Stored bytes vs source: " + Diff(a, array));
		Line(sb, "    Shader sample estimate: " + (flag ? ("decoded, non-color data would be wrong " + SampleExample(array, decoded: true)) : ("raw " + SampleExample(array, decoded: false))));
		texture2 = MEColorSpaceFixPlugin.BuildLinear(texture);
		if (texture2 == null)
		{
			Line(sb, "!! BuildLinear returned null; source bytes were not recorded.");
			Dump(outDir, sb);
			return;
		}
		bool flag2 = MEColorSpaceFixPlugin.IsSrgbTexture(texture2);
		byte[] array2 = ReadStored(texture2);
		Line(sb, "");
		Line(sb, "[2] Patched non-color texture: sRGB=" + flag2 + (flag2 ? " << wrong" : " << correct"));
		Line(sb, "    Stored bytes vs source: " + Diff(a, array2));
		Line(sb, "    Shader sample estimate: raw " + SampleExample(array2, decoded: false));
		string path = Path.Combine(outDir, "roundtrip_patched.png");
		if (!InvokeSaveTex(texture2, path))
		{
			Line(sb, "!! Cannot invoke SaveTex; test stopped.");
			Dump(outDir, sb);
			return;
		}
		byte[] b = ReadRamp(File.ReadAllBytes(path));
		Line(sb, "");
		Line(sb, "[3] Patched export:");
		Line(sb, "    Exported PNG vs source: " + Diff(a, b));
		byte[] b2 = ExportLegacy(texture2);
		Line(sb, "");
		Line(sb, "[4] Legacy export simulation:");
		Line(sb, "    Exported PNG vs source: " + Diff(a, b2));
		int num = MaxDiff(a, b);
		int num2 = MaxDiff(a, b2);
		Line(sb, "");
		Line(sb, "===== Result =====");
		Line(sb, "  Legacy max error: " + num2 + " / 255");
		Line(sb, "  Patched max error: " + num + " / 255");
		Line(sb, (num <= 1) ? "  PASS: roundtrip is closed enough for 8-bit texture work." : "  FAIL: roundtrip is not closed; do not trust the texture workflow yet.");
		Dump(outDir, sb);

        }
        finally
        {
            renderTargets.Dispose();
            MEColorSpaceFixPlugin.Release(texture);
            if (texture != null) UnityEngine.Object.Destroy(texture);
        }
    }

	private static void Line(StringBuilder sb, string s)
	{
		sb.AppendLine(s);
		MEColorSpaceFixPlugin.Log.LogInfo(s);
	}

	private static byte[] MakeRampPng()
	{
		Texture2D texture2D = new Texture2D(256, 8, TextureFormat.RGBA32, mipChain: false, linear: true);
		Color32[] array = new Color32[2048];
		for (int i = 0; i < 8; i++)
		{
			for (int j = 0; j < 256; j++)
			{
				ref Color32 reference = ref array[i * 256 + j];
				reference = new Color32((byte)j, (byte)j, (byte)j, byte.MaxValue);
			}
		}
		texture2D.SetPixels32(array);
		texture2D.Apply();
		byte[] result = texture2D.EncodeToPNG();
		UnityEngine.Object.Destroy(texture2D);
		return result;
	}

	private static byte[] ReadRamp(byte[] png)
	{
		Texture2D texture2D = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false, linear: true);
		texture2D.LoadImage(png);
		Color32[] pixels = texture2D.GetPixels32();
		int width = texture2D.width;
		int height = texture2D.height;
		byte[] array = new byte[256];
		for (int i = 0; i < 256; i++)
		{
			int num = Mathf.Min(i, width - 1);
			array[i] = pixels[height / 2 * width + num].r;
		}
		UnityEngine.Object.Destroy(texture2D);
		return array;
	}

	private static byte[] ReadStored(Texture tex)
	{
		RenderTexture active = RenderTexture.active;
		RenderTexture renderTexture = tex as RenderTexture;
		RenderTexture renderTexture2 = null;
		Texture2D texture2D = null;
		try
		{
			if (renderTexture == null)
			{
				renderTexture2 = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32, (!MEColorSpaceFixPlugin.IsSrgbTexture(tex)) ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB);
				Graphics.Blit(tex, renderTexture2);
				renderTexture = renderTexture2;
			}
			RenderTexture.active = renderTexture;
			texture2D = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, mipChain: false, !MEColorSpaceFixPlugin.IsSrgbTexture(tex));
			texture2D.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0);
			texture2D.Apply();
			Color32[] pixels = texture2D.GetPixels32();
			byte[] array = new byte[256];
			for (int i = 0; i < 256; i++)
			{
				int num = Mathf.Min(i, tex.width - 1);
				array[i] = pixels[tex.height / 2 * tex.width + num].r;
			}
			return array;
		}
		finally
		{
			if (texture2D != null)
			{
				UnityEngine.Object.Destroy(texture2D);
			}
			RenderTexture.active = active;
			if (renderTexture2 != null)
			{
				RenderTexture.ReleaseTemporary(renderTexture2);
			}
		}
	}

	private static byte[] ExportLegacy(Texture tex)
	{
		RenderTexture active = RenderTexture.active;
		RenderTexture temporary = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.Default, RenderTextureReadWrite.sRGB);
		Texture2D texture2D = null;
		try
		{
			RenderTexture.active = temporary;
			GL.Clear(clearDepth: false, clearColor: true, new Color(0f, 0f, 0f, 0f));
			Graphics.Blit(tex, temporary);
			texture2D = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, mipChain: false);
			texture2D.ReadPixels(new Rect(0f, 0f, tex.width, tex.height), 0, 0);
			texture2D.Apply();
			byte[] png = texture2D.EncodeToPNG();
			return ReadRamp(png);
		}
		finally
		{
			if (texture2D != null)
			{
				UnityEngine.Object.Destroy(texture2D);
			}
			RenderTexture.active = active;
			RenderTexture.ReleaseTemporary(temporary);
		}
	}

	private static Texture InvokeTextureFromBytes(byte[] png)
	{
		MethodInfo methodInfo = Find("TextureContainerManager", "TextureFromBytes", typeof(byte[]), typeof(TextureFormat), typeof(bool));
		if (methodInfo == null)
		{
			return null;
		}
		return methodInfo.Invoke(null, new object[3]
		{
			png,
			TextureFormat.ARGB32,
			false
		}) as Texture;
	}

	private static bool InvokeSaveTex(Texture tex, string path)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch
			{
				continue;
			}
			Type[] array = types;
			foreach (Type type in array)
			{
				if (type == null || type.Name != "MaterialEditorPluginBase")
				{
					continue;
				}
				MethodInfo[] methods = type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
				foreach (MethodInfo methodInfo in methods)
				{
					if (methodInfo.Name != "SaveTex")
					{
						continue;
					}
					ParameterInfo[] parameters = methodInfo.GetParameters();
					if (parameters.Length >= 2 && typeof(Texture).IsAssignableFrom(parameters[0].ParameterType))
					{
						object[] array2 = new object[parameters.Length];
						array2[0] = tex;
						array2[1] = path;
						for (int l = 2; l < parameters.Length; l++)
						{
							array2[l] = ((parameters[l].DefaultValue != DBNull.Value) ? parameters[l].DefaultValue : (parameters[l].ParameterType.IsValueType ? Activator.CreateInstance(parameters[l].ParameterType) : null));
						}
						methodInfo.Invoke(null, array2);
						return File.Exists(path);
					}
				}
			}
		}
		return false;
	}

	private static MethodInfo Find(string typeName, string method, params Type[] args)
	{
		Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
		foreach (Assembly assembly in assemblies)
		{
			Type[] types;
			try
			{
				types = assembly.GetTypes();
			}
			catch
			{
				continue;
			}
			Type[] array = types;
			foreach (Type type in array)
			{
				if (!(type == null) && !(type.Name != typeName))
				{
					MethodInfo method2 = type.GetMethod(method, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, args, null);
					if (method2 != null)
					{
						return method2;
					}
				}
			}
		}
		return null;
	}

	private static string Diff(byte[] a, byte[] b)
	{
		int num = 0;
		double num2 = 0.0;
		int num3 = 0;
		for (int i = 0; i < 256; i++)
		{
			int num4 = Math.Abs(a[i] - b[i]);
			num2 += (double)num4;
			if (num4 > num)
			{
				num = num4;
				num3 = i;
			}
		}
		return $"max {num}/255 at input {num3}, avg {num2 / 256.0:F2}";
	}

	private static int MaxDiff(byte[] a, byte[] b)
	{
		int num = 0;
		for (int i = 0; i < 256; i++)
		{
			num = Math.Max(num, Math.Abs(a[i] - b[i]));
		}
		return num;
	}

	private static string SampleExample(byte[] stored, bool decoded)
	{
		int[] source = new int[4] { 32, 64, 128, 200 };
		return "[" + string.Join(", ", source.Select(delegate(int v)
		{
			float num = (float)(int)stored[v] / 255f;
			float num2 = (decoded ? Mathf.GammaToLinearSpace(num) : num);
			return $"{v}->{num2:F3}";
		}).ToArray()) + "]";
	}

	private static void Dump(string dir, StringBuilder sb)
	{
		try
		{
			Directory.CreateDirectory(dir);
			string text = Path.Combine(dir, "me_roundtrip_report.txt");
			File.WriteAllText(text, sb.ToString(), Encoding.UTF8);
			MEColorSpaceFixPlugin.Log.LogMessage("Roundtrip self-test report: " + text);
		}
		catch (Exception ex)
		{
			MEColorSpaceFixPlugin.Log.LogWarning("Failed to write report: " + ex.Message);
		}
	}
}
