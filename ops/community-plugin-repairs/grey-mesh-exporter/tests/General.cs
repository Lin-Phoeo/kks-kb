using System.Text.Json;
using UnityEngine;
using Exporter;
internal static class General
{
 internal static int Run(string mode,string report)
 {
  bool expectedFailure=mode=="general-control";var checks=new List<object>();
  void Test(string name,Action action){try{action();checks.Add(new{name,passed=true});}catch(Exception error){checks.Add(new{name,passed=false,error=error.GetType().Name+": "+error.Message});}}
  void Check(bool value,string message){if(!value)throw new Exception(message);}
  foreach(bool hasProperty in new[]{false,true})Test("direct-renderer root custom property "+hasProperty,()=>{
   var root=new Transform{name="SyntheticCube"};var property=new FbxNodeCustomProperties();if(hasProperty)root.Components[typeof(FbxNodeCustomProperties)]=property;
   int callbacks=0;var result=UnityFbxExporterBinding.ScanHierarchy(root,true,new FbxExportSettings{OnFbxNodeCreated=(t,n)=>callbacks++});
   Check(result.FbxNodes.ContainsKey(root)&&callbacks==1,"root not registered");Check(property.Applies==(hasProperty?1:0),"optional properties not applied correctly");});
  Test("non-renderer root skips optional root properties",()=>{var root=new Transform{name="Parent"};UnityFbxExporterBinding.ScanHierarchy(root,false,new());});
  string temp=Path.Combine(Path.GetTempPath(),"grey-general-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(temp,"Textures"));
  try{
   foreach(string materialName in new[]{"NamedMaterial","","Named_MainTex","含中文材质"})
    foreach(string propertyName in new[]{"_MainTex","_NormalMap","_Unknown"})Test("material "+materialName+" property "+propertyName,()=>{
     Faults.Reset();Grey.AttemptBumpConversion=false;Grey.GenerateFBXMaterials=true;Grey.ApplyTexturesToFBXMaterials=true;
     UnityFbxExporterBinding.CreatedPaths.Clear();UnityFbxExporterBinding.Attached.Clear();var material=new Material();material.Textures[propertyName]=new Texture();
     new TextureExporter().ExportTextures(new(){propertyName},material,new IntPtr(3),materialName,temp);
     Check(UnityFbxExporterBinding.CreatedPaths.Single()=="Textures/"+materialName+propertyName+".png","filename changed");
     Check(UnityFbxExporterBinding.Attached.Count==(Data.Albedo.Contains(propertyName)?1:0),"albedo attachment mismatch");});
   foreach(bool generate in new[]{false,true})Test("disabled material toggle "+generate,()=>{Faults.Reset();Grey.GenerateFBXMaterials=generate;Grey.ApplyTexturesToFBXMaterials=!generate;UnityFbxExporterBinding.Attached.Clear();var m=new Material();m.Textures["_MainTex"]=new Texture();new TextureExporter().ExportTextures(new(){"_MainTex"},m,new IntPtr(3),"Named",temp);Check(UnityFbxExporterBinding.Attached.Count==0,"toggle ignored");});
  }finally{Directory.Delete(temp,true);}
  var output=new{checks=checks.Count,results=checks,expectedBaselineFailures=expectedFailure,scope="Actual extracted ScanHierarchy/GetTexture/ExportTextures/classifiers/AddTextureToMaterial IL; hierarchy preprocessing/native FBX delegates and Unity are synthetic. Does not prove native FBX scene or live hierarchy ownership."};File.WriteAllText(report,JsonSerializer.Serialize(output,new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine(JsonSerializer.Serialize(output));return checks.Any(x=>(bool)x.GetType().GetProperty("passed").GetValue(x)==false)?1:0;
 }
}
