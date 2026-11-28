using KKAPI;using KKAPI.Studio.SaveLoad;using Studio;using UnityEngine;using System.Text.Json;using Plugin=PostProcessingEffectsV3.PostProcessingEffectsV3;
int passed=0,failed=0;var results=new List<object>();
void Require(bool condition,string why){if(!condition)throw new Exception(why);}
void Test(string name,Action body){KoikatuAPI.Mode=GameMode.Studio;try{body();passed++;results.Add(new{name,passed=true});}catch(Exception e){failed++;results.Add(new{name,passed=false,error=e.GetType().Name+":"+e.Message});}}
ObjectsSelectedEventArgs Args(params ObjectCtrlInfo[] values)=>new(){SelectedObjects=values.ToList()};
Test("empty selection preserves prior focus without exception",()=>{var old=new Transform();var p=new Plugin{charapos=old};p.ObjectsSelected(null,Args());Require(ReferenceEquals(old,p.charapos),"cleared prior focus");});
Test("empty initial selection remains null",()=>{var p=new Plugin();p.ObjectsSelected(null,Args());Require(p.charapos==null,"invented focus");});
Test("single valid character updates exact hips path",()=>{var c=new OCIChar();var hips=new Transform();c.Control.transform.Result=hips;var p=new Plugin();p.ObjectsSelected(null,Args(c));Require(p.charapos==hips&&c.Control.transform.LastPath=="BodyTop/p_cf_body_bone/cf_j_root/cf_n_height/cf_j_hips","valid handler changed");});
Test("multiple selection keeps original first character semantics",()=>{var a=new OCIChar();var b=new OCIChar();a.Control.transform.Result=new();b.Control.transform.Result=new();var p=new Plugin();p.ObjectsSelected(null,Args(a,b));Require(p.charapos==a.Control.transform.Result&&b.Control.transform.LastPath==null,"changed selection order");});
Test("folder selection still retains previous focus",()=>{var prior=new Transform();var p=new Plugin{charapos=prior};p.ObjectsSelected(null,Args(new ObjectCtrlInfo()));Require(p.charapos==prior,"folder branch changed");});
Test("null first item still follows existing catch",()=>{var prior=new Transform();var p=new Plugin{charapos=prior};p.ObjectsSelected(null,Args((ObjectCtrlInfo)null));Require(p.charapos==prior,"null branch changed");});
Test("missing native character control retains previous focus",()=>{var prior=new Transform();var p=new Plugin{charapos=prior};p.ObjectsSelected(null,Args(new OCIChar{Control=null}));Require(p.charapos==prior,"missing control changed");});
Test("valid character without hips retains original null assignment",()=>{var p=new Plugin{charapos=new()};p.ObjectsSelected(null,Args(new OCIChar()));Require(p.charapos==null,"missing hips semantics changed");});
Test("non Studio ignores empty selection",()=>{KoikatuAPI.Mode=GameMode.MainGame;var prior=new Transform();var p=new Plugin{charapos=prior};p.ObjectsSelected(null,Args());Require(p.charapos==prior,"mode guard changed");});
Test("malformed null args remains an explicit caller error",()=>{var p=new Plugin();bool thrown=false;try{p.ObjectsSelected(null,null);}catch(NullReferenceException){thrown=true;}Require(thrown,"unexpected broad exception swallowing");});
Console.WriteLine(JsonSerializer.Serialize(new{checks=passed+failed,passed,failed,results},new JsonSerializerOptions{WriteIndented=true}));return failed==0?0:1;
