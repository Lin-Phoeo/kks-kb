using System.Reflection;
using System.Text.Json;

namespace UnityEngine
{
    public class Object
    {
        public bool Destroyed;
        public string name { get; set; }
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b) || (ReferenceEquals(a, null) || a.Destroyed) && (ReferenceEquals(b, null) || b.Destroyed);
        public static bool operator !=(Object a, Object b) => !(a == b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => base.GetHashCode();
        public static void Destroy(Object value) { if (value == null) return; value.Destroyed = true; Faults.Destroyed.Add(value); }
        public static void DestroyImmediate(Object value, bool allowAssets) { Destroy(value); }
    }
    public class Texture : Object { public int width { get; set; } = 1; public int height { get; set; } = 1; }
    public enum TextureFormat { ARGB32 = 5 }
    public enum RenderTextureFormat { Default = 0 }
    public enum RenderTextureReadWrite { Linear = 1 }
    public struct Rect { public Rect(float x, float y, float width, float height) { } }
    public struct Color { public float r, g, b, a; }
    public class Texture2D : Texture
    {
        public bool Linear; public bool Mips; public TextureFormat Format;
        public Texture2D(int width, int height) { this.width = width; this.height = height; Faults.Created.Add(this); }
        public Texture2D(int width, int height, TextureFormat format, bool mips, bool linear)
            : this(width, height) { Format = format; Mips = mips; Linear = linear; }
        public void ReadPixels(Rect rect, int x, int y, bool recalculate) { Faults.Fail("read"); }
        public void Apply() { Faults.Fail("apply"); }
        public Color[] GetPixels() { Faults.Fail("get-pixels"); return new[] { new Color { a = 0.5f, g = 0.25f } }; }
        public void SetPixels(Color[] pixels) { Faults.Fail("set-pixels"); }
    }
    public class RenderTexture : Texture
    {
        public static RenderTexture active { get; set; }
        public static RenderTexture GetTemporary(int width, int height, int depth, RenderTextureFormat format, RenderTextureReadWrite readWrite)
        {
            Faults.Fail("temporary"); Faults.TemporaryCalls++; Faults.Depth = depth; Faults.ReadWrite = readWrite;
            return new RenderTexture { width = width, height = height };
        }
        public static void ReleaseTemporary(RenderTexture texture) { Faults.Released++; Faults.ReleasedWasActive |= ReferenceEquals(active, texture); }
    }
    public static class Graphics { public static void Blit(Texture input, RenderTexture output) { Faults.Fail("blit"); } }
    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(Texture2D texture) { Faults.EncodeCalls++; Faults.Fail("encode"); return Faults.EmptyEncoded ? Array.Empty<byte>() : Faults.Png; }
    }
    public class Component : Object
    {
        public Transform transform { get; set; }
        public GameObject gameObject { get; set; }
        public readonly Dictionary<Type, Component> Components = new();
        public T GetComponent<T>() where T : Component => Components.TryGetValue(typeof(T),out var component)?(T)component:null;
    }
    public class GameObject : Object { public GameObject(){} public GameObject(string name){this.name=name;} public Transform transform {get;} = new Transform(); }
    public struct Vector3 { public float x,y,z; }
    public struct Matrix4x4
    {
        public static Matrix4x4 identity => default;
        public static Matrix4x4 operator *(Matrix4x4 a, Matrix4x4 b) => a;
        public Vector3 MultiplyPoint(Vector3 p) => p;
        public Vector3 MultiplyVector(Vector3 p) => p;
    }
    public struct BoneWeight { }
    public class Mesh : Object
    {
        public Mesh() { Faults.Created.Add(this); }
        public int blendShapeCount { get; set; }
        public Vector3[] vertices { get; set; } = new Vector3[1];
        public Vector3[] normals { get; set; } = new Vector3[1];
        public BoneWeight[] boneWeights { get; set; } = Array.Empty<BoneWeight>();
    }
    public class Cloth : Component { public Vector3[] vertices { get; set; } = new Vector3[1]; public Vector3[] normals { get; set; } = new Vector3[1]; }
    public class Shader : Object { }
    public class Material : Object { public Shader shader {get;}=new();public readonly Dictionary<string,Texture> Textures=new();public bool HasProperty(string name)=>Textures.ContainsKey(name);public Texture GetTexture(string name)=>Textures[name]; }
    public class MeshRenderer : Component { }
    public class SkinnedMeshRenderer : Component
    {
        public float[] Weights = {12f,34f,56f}; public int FailCapture=-1; public int FailZero=-1;
        public Transform rootBone { get; set; }
        public float GetBlendShapeWeight(int index) { if(index==FailCapture)throw new InvalidOperationException("capture fault");return Weights[index]; }
        public void SetBlendShapeWeight(int index,float value) { if(index==FailZero&&value==0)throw new InvalidOperationException("zero fault");Weights[index]=value; }
        public void BakeMesh(Mesh mesh) { Faults.Fail("bake"); }
    }
    public class Transform : Component
    {
        public Transform parent {get;set;} public Transform Find(string name)=>null;
        public Matrix4x4 worldToLocalMatrix => default;
        public Matrix4x4 localToWorldMatrix => default;
    }
    public static class Application { public static string dataPath { get; set; } }
    public static class Faults
    {
        public static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+jZBsAAAAASUVORK5CYII=");
        public static string Stage; public static bool EmptyEncoded; public static int EncodeCalls, TemporaryCalls, Released, Depth;
        public static bool ReleasedWasActive; public static RenderTextureReadWrite ReadWrite;
        public static List<Object> Created = new(), Destroyed = new();
        public static void Fail(string stage) { if (stage == Stage) throw new InvalidOperationException("synthetic " + stage); }
        public static void Reset(string stage = null)
        { Stage = stage; EmptyEncoded = false; EncodeCalls = TemporaryCalls = Released = Depth = 0; ReleasedWasActive = false; Created.Clear(); Destroyed.Clear(); }
    }
}
namespace Exporter
{
    public enum BlendShapeOptions { WriteDeformations }
    public delegate void FbxSkinnedMeshCallback(UnityEngine.Transform root,IntPtr node,UnityEngine.SkinnedMeshRenderer renderer,IntPtr mesh);
    public delegate void FbxNodeCallback(UnityEngine.Transform root,IntPtr node);
    public class FbxExportSettings { public bool ExportMeshes=>true;public bool ExportSkinnedMeshes=>true; public FbxNodeCallback OnFbxNodeCreated; public bool ExportCloth => true; public BlendShapeOptions blendShapeOptions; public FbxSkinnedMeshCallback OnFbxSkinnedMeshCreated; }
    public class FbxExportCollection { public Dictionary<UnityEngine.Transform,IntPtr> FbxNodes = new();public List<UnityEngine.MeshRenderer> Meshes=new();public List<UnityEngine.SkinnedMeshRenderer> SkinnedMeshes=new();public List<UnityEngine.Cloth> Cloths=new(); }
    public class FbxCustomPropertyBehaviour : UnityEngine.Component { public int Applies; public void Apply(IntPtr mesh) { Applies++; } }
    public class FbxNodeCustomProperties : FbxCustomPropertyBehaviour { }
    public class FbxMaterialCustomProperties : FbxCustomPropertyBehaviour { }
    public class FbxSkinnedMeshCustomProperties : FbxCustomPropertyBehaviour { }
    public static class UnityFbxExporterBinding
    {
        public delegate IntPtr IntPtrIntPtrString(IntPtr parent,string name);
        public delegate IntPtr IntPtrStringStringETextureUseEAlphaSource(string path,string name,ETextureUse use,EAlphaSource alpha);
        public delegate void VoidIntPtrIntPtrTextureChannels(IntPtr material,IntPtr texture,TextureChannels channel);
        public enum EAlphaSource{eNone=0,eBlack=2}
        public enum TextureChannels{Diffuse=0,BumpMap=1,NormalMap=2,Ambient=3,Emissive=4,Reflection=5,Shininess=6,Specular=7,TransparentColor=8}
        public static readonly List<string> CreatedPaths=new();public static readonly List<TextureChannels> Attached=new();
        public static IntPtrIntPtrString CreateNode=(parent,name)=>new IntPtr(7);
        public static IntPtrStringStringETextureUseEAlphaSource CreateTexture=(path,name,use,alpha)=>{CreatedPaths.Add(path);return new IntPtr(9);};
        public static VoidIntPtrIntPtrTextureChannels MaterialSetTexture=(material,texture,channel)=>Attached.Add(channel);
        public static FbxExportCollection ScanHierarchy(UnityEngine.Transform root,bool createRoot,FbxExportSettings settings)=>throw new Exception("unpopulated");
        private static void PreprocessHierarchy(UnityEngine.Transform container,UnityEngine.Transform root){}
        private static void RecursiveAddTransforms(UnityEngine.Transform root,IntPtr parent,FbxExportCollection collection,bool rootLevel,FbxExportSettings settings,bool foundMesh){}
        public enum ETextureUse { eStandard = 0, eBumpNormalMap = 5 }
        public static float[] blendWeights; public static UnityEngine.SkinnedMeshRenderer blendSmr; public static UnityEngine.Mesh blendMesh;
        public delegate IntPtr IntPtrString(string name);
        public delegate IntPtr IntPtrIntPtr(IntPtr node);
        public delegate void VoidIntPtr(IntPtr node);
        public static IntPtrString SkeletonCreateRoot = name => new IntPtr(1);
        public static void NodeSetTransform(IntPtr node,UnityEngine.Transform transform,bool rootLevel) { }
        public static IntPtr ProcessMesh(UnityEngine.Mesh mesh,UnityEngine.Vector3[] vertices,UnityEngine.Vector3[] normals,IntPtr parent,
            BlendShapeOptions options,TextureExporter texture,string path,UnityEngine.Material[] materials)
        { UnityEngine.Faults.Fail("process-mesh");return UnityEngine.Faults.Stage=="zero-mesh"?IntPtr.Zero:new IntPtr(2); }
        public static IntPtrIntPtr MeshCreateSkin = mesh => new IntPtr(3);
        public static IntPtrIntPtr ClusterCreate = node => new IntPtr(4);
        public static void AddBoneWeights(UnityEngine.BoneWeight[] weights,IntPtr[] clusters) { }
        public static VoidIntPtr MeshStoreBindPose = mesh => { };
    }
    public class TextureExporter : UnityEngine.Component
    {
        public void ExportTextures(List<string> names,UnityEngine.Material material,IntPtr native,string materialName,string path)=>throw new Exception("unpopulated");
        private static bool GetTexture(UnityEngine.Material material,string name,ref UnityEngine.Texture texture)=>throw new Exception("unpopulated");
        private UnityFbxExporterBinding.ETextureUse calculateTextureUseFromTextureName(string name)=>throw new Exception("unpopulated");
        private UnityFbxExporterBinding.TextureChannels calculateChannelFromTextureName(string name)=>throw new Exception("unpopulated");
        public static void AddTextureToMaterial(IntPtr material,string textureName,UnityFbxExporterBinding.TextureChannels channel,UnityFbxExporterBinding.ETextureUse use)=>throw new Exception("unpopulated");
        public static void AddTextureToMaterial(IntPtr material,string textureName,UnityFbxExporterBinding.TextureChannels channel,UnityFbxExporterBinding.ETextureUse use,string propertyName)=>throw new Exception("unpopulated");
        public bool SaveTexture(ref UnityEngine.Texture texture, string path, string filename, UnityFbxExporterBinding.ETextureUse use) => throw new Exception("unpopulated");
        public bool WriteAllBytes(string filename, byte[] data) => throw new Exception("unpopulated");
        private static void EnsureFolderExists(string path) => throw new Exception("unpopulated");
        public static UnityEngine.Texture2D ConvertToRGBNormalMap(UnityEngine.Texture2D source) => throw new Exception("unpopulated");
        public IntPtr ProcessMaterial(UnityEngine.Material mat,string path)
        { UnityEngine.Faults.Fail("material");if(UnityEngine.Faults.Stage=="material-encode"){UnityEngine.Faults.Stage="encode";UnityEngine.Texture t=new UnityEngine.Texture();SaveTexture(ref t,path,"skin-map",UnityFbxExporterBinding.ETextureUse.eStandard);}return new IntPtr(1); }
    }
    public static class Fbx
    {
        public static readonly List<string> Files = new();
        public static bool Export(string filename, bool skip, UnityEngine.Transform[] roots)
        { Files.Add(filename); File.WriteAllText(filename, "synthetic fbx " + Files.Count); return true; }
        public static UnityEngine.Mesh SourceMesh; public static UnityEngine.Cloth SourceCloth;
        private static bool checkIsValid(UnityEngine.SkinnedMeshRenderer smr,bool exportCloth,out UnityEngine.Transform[] bones,
            out UnityEngine.Mesh mesh,out UnityEngine.Cloth cloth,out UnityEngine.Material[] materials)
        { bones=Array.Empty<UnityEngine.Transform>();mesh=SourceMesh;cloth=SourceCloth;materials=new[]{new UnityEngine.Material()};return true; }
        public static void ExportSkinnedMeshOrCloth(UnityEngine.SkinnedMeshRenderer smr,UnityEngine.Transform root,TextureExporter exporter,string path,
            Dictionary<IntPtr,IntPtr> skeleton,Dictionary<UnityEngine.Material,IntPtr> materials,FbxExportSettings settings,FbxExportCollection collection) => throw new Exception("unpopulated");
        private static void RecursivelyEnsureLimb(UnityEngine.Transform root,UnityEngine.Transform bone,Dictionary<UnityEngine.Transform,IntPtr> nodes,Dictionary<IntPtr,IntPtr> skeleton) { }
        private static void SkinAddCluster(IntPtr skin,IntPtr cluster,IntPtr root,UnityEngine.Transform bone) { }
    }
}
public static class Data { public static List<string> Albedo=new(){"_MainTex","_BaseMap"},BumpMap=new(){"_BumpMap"},NormalMap=new(){"_NormalMap"},Ambient=new(),Emissive=new(),Reflection=new(),Shininess=new(),Specular=new(),Transparent=new(); }
public class Grey
{
    public static bool GenerateFBXMaterials {get;set;}=true;public static bool ApplyTexturesToFBXMaterials {get;set;}=true;
    public static string ErrorMessage = "";
    public static string BasePath { get; set; } = "Export";
    public static bool AttemptBumpConversion { get; set; }
    public static bool BumpConversionSaveBoth { get; set; }
    public static void LogFormat(string format, params object[] args) { }
    public static void LogWarningFormat(string format, params object[] args) { }
    public static void LogErrorFormat(string format, params object[] args) { }
    public static void Export(string name, bool skip, UnityEngine.Transform root) => throw new Exception("unpopulated");
}
public static class Program
{
    public static int Main(string[] args)
    {
        bool repaired = args[0] == "candidate";
        if(args[0].StartsWith("general"))return General.Run(args[0],args[1]);
        var checks = new List<object>();
        void Check(string name, bool passed) { checks.Add(new { name, passed }); if (!passed) throw new Exception(name); }
        string scratch = Path.Combine(Path.GetTempPath(), "grey-export-probe-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        var exporter = new Exporter.TextureExporter();
        try
        {
            string textureDir = Path.Combine(scratch, "output"); Directory.CreateDirectory(Path.Combine(textureDir, "Textures"));
            string file = Path.Combine(textureDir, "Textures", "map.png");
            File.WriteAllBytes(file, Enumerable.Repeat((byte)99, 500).ToArray());
            Check("writer reports valid PNG success", exporter.WriteAllBytes(file, UnityEngine.Faults.Png));
            byte[] saved = File.ReadAllBytes(file);
            Check("actual writer truncates old trailing bytes", repaired ? saved.SequenceEqual(UnityEngine.Faults.Png) : saved.Length == 500);
            Check("PNG IEND remains final bytes", repaired ? saved.TakeLast(12).SequenceEqual(UnityEngine.Faults.Png.TakeLast(12)) : !saved.TakeLast(12).SequenceEqual(UnityEngine.Faults.Png.TakeLast(12)));
            bool rejected = false;
            try { exporter.WriteAllBytes(scratch, UnityEngine.Faults.Png); } catch (IOException) { rejected = true; } catch (UnauthorizedAccessException) { rejected = true; }
            Check("actual writer IO failure propagates", repaired ? rejected : !rejected);
            Check("empty bytes remain rejected", !exporter.WriteAllBytes(file, Array.Empty<byte>()));
            var stages = new[] { "normal", "temporary", "blit", "read", "apply", "encode", "io", "empty-encode", "get-pixels", "set-pixels", "converted-io" };
            foreach (string stage in stages)
            {
                UnityEngine.Faults.Reset(stage);
                Grey.AttemptBumpConversion = stage is "get-pixels" or "set-pixels" or "converted-io";
                Grey.BumpConversionSaveBoth = stage == "converted-io";
                UnityEngine.Faults.EmptyEncoded = stage == "empty-encode";
                var previous = new UnityEngine.RenderTexture { name = "previous target" }; UnityEngine.RenderTexture.active = previous;
                UnityEngine.Texture input = new UnityEngine.Texture { width = 1, height = 1 };
                string name = "case_" + stage; string expectedPath = Path.Combine(textureDir, "Textures", name + ".png");
                if (stage == "io") Directory.CreateDirectory(expectedPath);
                if (stage == "converted-io") Directory.CreateDirectory(Path.Combine(textureDir, "Textures", name + "_converted.png"));
                bool threw = false, result = false;
                try { result = exporter.SaveTexture(ref input, textureDir, name, Exporter.UnityFbxExporterBinding.ETextureUse.eBumpNormalMap); }
                catch (Exception error) { threw = true; if (stage == "skin-normal") Console.Error.WriteLine(error); }
                bool fault = stage != "normal";
                if (repaired) Check(stage + " result", fault ? threw : !threw && result);
                else Check(stage + " legacy failure reproduced", !threw && (stage == "normal" || stage == "converted-io" ? result : !result));
                bool cleaned = ReferenceEquals(UnityEngine.RenderTexture.active, previous) && UnityEngine.Faults.Released == UnityEngine.Faults.TemporaryCalls;
                if (repaired) Check(stage + " restores old RT and releases temporary", cleaned && !UnityEngine.Faults.ReleasedWasActive);
                else Check(stage + " legacy cleanup path", stage is "normal" or "temporary" or "io" or "empty-encode" or "converted-io" ? cleaned : !cleaned);
                if (repaired) Check(stage + " owns and destroys only new Texture2D", UnityEngine.Faults.Created.All(x => x.Destroyed) && !input.Destroyed && !previous.Destroyed);
                if (UnityEngine.Faults.Created.Count > 0)
                {
                    var first = (UnityEngine.Texture2D)UnityEngine.Faults.Created[0];
                    Check(stage + " preserves color/format flags", first.Format == (UnityEngine.TextureFormat)5 && !first.Mips && first.Linear && UnityEngine.Faults.Depth == 24 && UnityEngine.Faults.ReadWrite == (UnityEngine.RenderTextureReadWrite)1);
                }
            }
            foreach (bool both in new[] { false, true })
            {
                UnityEngine.Faults.Reset(); Grey.AttemptBumpConversion = true; Grey.BumpConversionSaveBoth = both;
                UnityEngine.Texture input = new UnityEngine.Texture(); string name = both ? "normal-both" : "normal-only";
                Check("normal conversion " + both, exporter.SaveTexture(ref input, textureDir, name, Exporter.UnityFbxExporterBinding.ETextureUse.eBumpNormalMap));
                Check("relative filenames unchanged " + both, File.Exists(Path.Combine(textureDir, "Textures", name + ".png")) &&
                    File.Exists(Path.Combine(textureDir, "Textures", name + "_converted.png")) == both);
                if (repaired) Check("converted textures destroyed " + both, UnityEngine.Faults.Created.All(x => x.Destroyed));
            }
            foreach (string stage in new[] { "skin-normal", "material", "material-encode", "bake", "process-mesh", "zero-mesh", "capture", "zero-weight", "cloth", "no-blend" })
            {
                var smr = new UnityEngine.SkinnedMeshRenderer { transform = new UnityEngine.Transform(), name = "fixture renderer" };
                var sourceMesh = new UnityEngine.Mesh { name = "fixture borrowed mesh", blendShapeCount = stage == "no-blend" ? 0 : 3 };
                Exporter.Fbx.SourceMesh = sourceMesh; Exporter.Fbx.SourceCloth = stage == "cloth" ? new UnityEngine.Cloth() : null;
                UnityEngine.Faults.Reset(stage);
                if (stage == "capture") smr.FailCapture = 1;
                if (stage == "zero-weight") smr.FailZero = 1;
                var previous = new UnityEngine.RenderTexture(); UnityEngine.RenderTexture.active = previous;
                Exporter.UnityFbxExporterBinding.blendSmr = null; Exporter.UnityFbxExporterBinding.blendMesh = null; Exporter.UnityFbxExporterBinding.blendWeights = null;
                var collection = new Exporter.FbxExportCollection();collection.FbxNodes.Add(smr.transform,new IntPtr(1));
                bool threw = false;
                try { Exporter.Fbx.ExportSkinnedMeshOrCloth(smr, smr.transform, exporter, textureDir, new(), new(), new(), collection); }
                catch (Exception error) { threw = true; if (stage == "skin-normal") Console.Error.WriteLine(error); }
                bool fault = stage is "material" or "material-encode" or "bake" or "process-mesh" or "zero-mesh" or "capture" or "zero-weight";
                Check(stage + " full skinned IL failure result", repaired ? threw == fault : threw == (fault && stage != "zero-mesh" && stage != "material-encode"));
                bool restored = smr.Weights.SequenceEqual(new[] { 12f,34f,56f });
                if (repaired) Check(stage + " restores only captured weights", restored);
                else Check(stage + " legacy weight state reproduced", stage is "skin-normal" or "material-encode" or "zero-mesh" or "cloth" or "no-blend" ? restored : !restored);
                if (repaired)
                {
                    Check(stage + " owned mesh and textures released", UnityEngine.Faults.Created.All(x => x.Destroyed) && !sourceMesh.Destroyed);
                    Check(stage + " static borrowed references cleared", Exporter.UnityFbxExporterBinding.blendSmr is null && Exporter.UnityFbxExporterBinding.blendMesh is null && Exporter.UnityFbxExporterBinding.blendWeights is null);
                    Check(stage + " upstream texture failure restores RT", ReferenceEquals(UnityEngine.RenderTexture.active,previous) && UnityEngine.Faults.TemporaryCalls == UnityEngine.Faults.Released);
                }
            }
            UnityEngine.Application.dataPath = Path.Combine(scratch, "CharaStudio_Data"); Directory.CreateDirectory(UnityEngine.Application.dataPath);
            Grey.BasePath = "Export"; Exporter.Fbx.Files.Clear();
            var root = new UnityEngine.Transform { name = "synthetic model" };
            Grey.Export("Synthetic", false, root); string firstFile = Exporter.Fbx.Files[0];
            File.WriteAllText(firstFile, "original export sentinel");
            Grey.Export("Synthetic", false, root); string secondFile = Exporter.Fbx.Files[1];
            Check("same-name export directories stay distinct", repaired ? firstFile != secondFile : true);
            if (repaired) Check("existing export remains unchanged", File.ReadAllText(firstFile) == "original export sentinel");
            Check("FBX and textures share common directory", Exporter.Fbx.Files.All(path => Path.GetFileName(path) == "Synthetic.fbx" && Directory.Exists(Path.Combine(Path.GetDirectoryName(path)!, "Textures"))));
            if (repaired)
            {
                var helper = typeof(Program).Assembly.GetType("GreyExportRepair.ExportSafety", true);
                MethodInfo unique = helper.GetMethod("UniqueDirectory", BindingFlags.Static | BindingFlags.NonPublic)!;
                string requested = Path.Combine(scratch, "already-existing"); Directory.CreateDirectory(requested); File.WriteAllText(Path.Combine(requested, "keep.txt"), "keep");
                string created = (string)unique.Invoke(null, new object[] { requested });
                Check("existing output base never reused", Path.GetFullPath(created).TrimEnd(Path.DirectorySeparatorChar) != Path.GetFullPath(requested) && File.ReadAllText(Path.Combine(requested, "keep.txt")) == "keep");
            }
            var report = new { mode = args[0], passed = true, checks = checks.Count, cases = checks,
                actualIl = "SaveTexture, WriteAllBytes, EnsureFolderExists, ConvertToRGBNormalMap, full ExportSkinnedMeshOrCloth, Grey.Export and new helper",
                boundary = "Actual extracted IL with synthetic Unity texture/rendering/native FBX. System.IO uses isolated temporary files, including a valid PNG payload. No real GPU, color roundtrip or FBX library invocation." };
            string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(args[1], json); Console.WriteLine(json); return 0;
        }
        finally { Directory.Delete(scratch, true); }
    }
}
