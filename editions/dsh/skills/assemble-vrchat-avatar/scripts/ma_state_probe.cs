// Execute through the installed swax/UnityMCP-VRC execute_editor_command.
// Read only. Generate literals with emit_ma_probe.py; do not import this into Assets.
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using UnityEngine.SceneManagement;
public class EditorCommand {
    const string TargetPath = __AVATAR_LITERAL__;
    const string MenuRootPath = __MENU_ROOT_LITERAL__;
    static readonly string[] ExclusiveParameters = new string[] { __PARAMETER_LITERALS__ };
    static readonly string[] OutfitRoots = new string[] { __OUTFIT_ROOT_LITERALS__ };
    static readonly string[] BodyRendererPaths = new string[] { __BODY_RENDERER_LITERALS__ };
    public class TargetRow { public string path; public bool active; public bool resolved; }
    public class ItemRow {
        public string path, parameter, type;
        public float value;
        public bool isDefault, automaticValue, hasToggle, toggleSchemaSupported;
        public List<TargetRow> targets = new List<TargetRow>();
    }
    static string P(Transform t) { return t.parent == null ? t.name : P(t.parent)+"/"+t.name; }
    static string Rel(Transform root, Transform t) { return t == root ? "" : P(t).Substring(P(root).Length+1); }
    static object Issue(string code, string path, string detail) { return new {code=code,path=path,detail=detail}; }
    static string Text(SerializedProperty p) { return p != null ? p.stringValue : ""; }
    static bool Flag(SerializedProperty p) { return p != null && p.boolValue; }
    public static object Execute() {
        var scene=SceneManager.GetActiveScene();
        var candidates=new List<Transform>();
        foreach(var r in scene.GetRootGameObjects())
            foreach(var t in r.GetComponentsInChildren<Transform>(true))
                if(P(t)==TargetPath) candidates.Add(t);
        if(candidates.Count!=1) throw new Exception("EXACT_UNIQUE_AVATAR_REQUIRED: "+TargetPath);
        var root=candidates[0];
        var components=root.GetComponentsInChildren<Component>(true);
        var descriptor=components.FirstOrDefault(c=>c!=null && c.transform==root && c.GetType().Name=="VRCAvatarDescriptor");
        if(descriptor==null) throw new Exception("TARGET_IS_NOT_AVATAR_ROOT");
        var issues=new List<object>();
        var installers=new List<object>();
        var items=new List<ItemRow>();
        var declared=new Dictionary<string,List<float>>();
        var declarations=new List<object>();
        var ds=new SerializedObject(descriptor);
        var exp=ds.FindProperty("expressionParameters");
        var sources=new List<UnityEngine.Object>();
        if(exp!=null && exp.objectReferenceValue!=null) sources.Add(exp.objectReferenceValue);
        sources.AddRange(components.Where(c=>c!=null && c.GetType().Name=="ModularAvatarParameters").Cast<UnityEngine.Object>());
        foreach(var source in sources) {
            var so=new SerializedObject(source); var ps=so.FindProperty("parameters");
            if(ps==null) continue;
            for(int i=0;i<ps.arraySize;i++) {
                var entry=ps.GetArrayElementAtIndex(i);
                if(Flag(entry.FindPropertyRelative("isPrefix"))) continue;
                var explicitFlag=entry.FindPropertyRelative("hasExplicitDefaultValue");
                if(explicitFlag!=null && !explicitFlag.boolValue) continue;
                var name=Text(entry.FindPropertyRelative("name"))+Text(entry.FindPropertyRelative("nameOrPrefix"));
                var remap=Text(entry.FindPropertyRelative("remapTo")); if(!string.IsNullOrEmpty(remap)) name=remap;
                if(!ExclusiveParameters.Contains(name)) continue;
                var dp=entry.FindPropertyRelative("defaultValue");
                if(dp==null) continue;
                if(!declared.ContainsKey(name)) declared[name]=new List<float>();
                declared[name].Add(dp.floatValue);
                declarations.Add(new {parameter=name,value=dp.floatValue,owner=source is Component?P(((Component)source).transform):AssetDatabase.GetAssetPath(source)});
            }
        }
        foreach(var c in components) {
            if(c==null) continue;
            var type=c.GetType().Name;
            if(type=="ModularAvatarMenuInstaller") {
                var so=new SerializedObject(c); var mp=so.FindProperty("menuToAppend");
                bool hasMenu=mp!=null && mp.objectReferenceValue!=null;
                var provider=c.GetComponents<Component>().FirstOrDefault(x=>x!=null && x.GetType().GetInterfaces().Any(t=>t.Name=="MenuSource"));
                installers.Add(new {path=P(c.transform),menu=hasMenu?AssetDatabase.GetAssetPath(mp.objectReferenceValue):null,source=provider!=null?provider.GetType().Name:null});
                if(!hasMenu && provider==null) issues.Add(Issue("MENU_SOURCE_MISSING",P(c.transform),"Resolve the actual asset/reference or provider on this object; do not infer the owner from an error count."));
            }
            if(type!="ModularAvatarMenuItem") continue;
            var serialized=new SerializedObject(c); var control=serialized.FindProperty("Control");
            if(control==null) { issues.Add(Issue("MENU_SCHEMA_UNSUPPORTED",P(c.transform),"Inspect the installed MA version before changing fields."));continue; }
            var row=new ItemRow {path=P(c.transform),parameter=Text(control.FindPropertyRelative("parameter.name")),
                value=control.FindPropertyRelative("value").floatValue,isDefault=Flag(serialized.FindProperty("isDefault")),
                automaticValue=Flag(serialized.FindProperty("automaticValue"))};
            var getter=c.GetType().GetProperty("Control");
            var backingField=c.GetType().GetField("Control");
            var controlValue=getter!=null?getter.GetValue(c,null):backingField!=null?backingField.GetValue(c):null;
            var typeField=controlValue!=null?controlValue.GetType().GetField("type"):null;
            row.type=typeField!=null?typeField.GetValue(controlValue).ToString():"UNKNOWN";
            var toggle=c.GetComponents<Component>().FirstOrDefault(x=>x!=null && x.GetType().Name=="ModularAvatarObjectToggle");
            row.hasToggle=toggle!=null;
            if(toggle!=null) {
                var ts=new SerializedObject(toggle);var objects=ts.FindProperty("m_objects");
                row.toggleSchemaSupported=objects!=null;
                if(objects!=null) for(int i=0;i<objects.arraySize;i++) {
                    var entry=objects.GetArrayElementAtIndex(i);
                    var reference=entry.FindPropertyRelative("Object");
                    var targetProperty=reference!=null?reference.FindPropertyRelative("targetObject"):null;
                    var target=targetProperty!=null?targetProperty.objectReferenceValue as GameObject:null;
                    string targetPath=target!=null && (target.transform==root || target.transform.IsChildOf(root))?
                        Rel(root,target.transform):Text(reference!=null?reference.FindPropertyRelative("referencePath"):null);
                    row.targets.Add(new TargetRow{path=targetPath,active=Flag(entry.FindPropertyRelative("Active")),resolved=target!=null});
                }
            }
            items.Add(row);
            if(!string.IsNullOrEmpty(MenuRootPath) && (row.path==TargetPath+"/"+MenuRootPath || row.path.StartsWith(TargetPath+"/"+MenuRootPath+"/",StringComparison.Ordinal))
                && row.type=="SubMenu" && serialized.FindProperty("MenuSource")!=null
                && serialized.FindProperty("MenuSource").intValue==1
                && (serialized.FindProperty("menuSource_otherObjectChildren")==null || serialized.FindProperty("menuSource_otherObjectChildren").objectReferenceValue==null)
                && c.transform.childCount==0)
                issues.Add(Issue("EMPTY_CHILDREN_SUBMENU",row.path,"Children mode has no children; inspect intended parent/child placement."));
        }
        var groups=new List<object>();
        foreach(var name in ExclusiveParameters) {
            var choices=items.Where(i=>i.parameter==name && (i.type=="Toggle" || i.type=="Button")).ToList();
            var explicitDefaults=declared.ContainsKey(name)?declared[name]:new List<float>();
            var defaults=choices.Where(i=>i.isDefault).ToList();
            if(choices.Count==0) issues.Add(Issue("EXCLUSIVE_GROUP_NOT_FOUND",name,"No matching controls; check the exact parameter and scope."));
            else if(defaults.Count==0 && explicitDefaults.Count==0)
                issues.Add(Issue("EXCLUSIVE_DEFAULT_MISSING",name,"All menu isDefault flags are false and no explicit parameter default was found."));
            if(defaults.Count>1) issues.Add(Issue("MULTIPLE_DEFAULT_ITEMS",name,"The requested exclusive group has multiple default menu items."));
            if(explicitDefaults.Distinct().Count()>1) issues.Add(Issue("CONFLICTING_PARAMETER_DEFAULTS",name,"Multiple declarations disagree."));
            foreach(var value in explicitDefaults)
                if(!choices.Any(i=>i.automaticValue || Math.Abs(i.value-value)<0.005f))
                    issues.Add(Issue("DEFAULT_WITHOUT_MENU_CHOICE",name,"An explicit default value matches no fixed menu choice."));
            var union=new HashSet<string>(choices.SelectMany(i=>i.targets).Select(t=>t.path).Where(p=>!string.IsNullOrEmpty(p)));
            foreach(var choice in choices) {
                if(!choice.hasToggle) { issues.Add(Issue("TOGGLE_CONSUMER_NOT_FOUND",choice.path,"Inspect the real Animator/other consumer before acceptance; no consumer is inferred."));continue; }
                if(!choice.toggleSchemaSupported) {issues.Add(Issue("TOGGLE_SCHEMA_UNSUPPORTED",choice.path,"Inspect the installed field schema."));continue;}
                var covered=new HashSet<string>(choice.targets.Select(t=>t.path));
                var missing=union.Where(p=>!covered.Contains(p)).OrderBy(p=>p).ToArray();
                if(missing.Length>0) issues.Add(Issue("EXCLUSIVE_COVERAGE_GAP",choice.path,string.Join(", ",missing)));
                if(choice.targets.Any(t=>!t.resolved)) issues.Add(Issue("UNRESOLVED_TOGGLE_REFERENCE",choice.path,"Verify AvatarObjectReference resolution for the recorded target path."));
            }
            groups.Add(new {parameter=name,explicitDefaults=explicitDefaults,choices=choices});
        }
        var outfits=new List<object>();
        var shapeChecks=new List<object>();
        foreach(var relative in OutfitRoots.Distinct()) {
            var outfit=root.Find(relative);
            if(outfit==null) {issues.Add(Issue("OUTFIT_ROOT_NOT_FOUND",relative,"Resolve the exact garment root in this candidate."));continue;}
            var renderers=outfit.GetComponentsInChildren<Renderer>(true);
            var geometry=new List<object>(); int meshCount=0;
            foreach(var renderer in renderers) {
                Mesh mesh=null;
                var skinned=renderer as SkinnedMeshRenderer;
                if(skinned!=null) mesh=skinned.sharedMesh;
                else if(renderer is MeshRenderer) {
                    var filter=renderer.GetComponent<MeshFilter>(); if(filter!=null) mesh=filter.sharedMesh;
                }
                bool usable=mesh!=null && mesh.vertexCount>0;
                if(usable) meshCount++;
                geometry.Add(new {path=P(renderer.transform),type=renderer.GetType().Name,
                    activeSelf=renderer.gameObject.activeSelf,activeHierarchy=renderer.gameObject.activeInHierarchy,
                    enabled=renderer.enabled,usableMesh=usable,vertices=mesh!=null?mesh.vertexCount:0,
                    mesh=mesh!=null?AssetDatabase.GetAssetPath(mesh):null,
                    materials=renderer.sharedMaterials.Select(m=>m!=null?AssetDatabase.GetAssetPath(m):null).ToArray()});
            }
            outfits.Add(new {path=P(outfit),prefab=PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(outfit.gameObject),
                rendererCount=renderers.Length,usableMeshCount=meshCount,geometry=geometry});
            if(meshCount==0) issues.Add(Issue("OUTFIT_HAS_NO_MESH",P(outfit),
                "This root contains no usable clothing mesh. Resolve paired geometry or actual provider output before calling it installed; this is not proof that the whole source package has no clothes."));
            foreach(var changer in outfit.GetComponentsInChildren<Component>(true).Where(c=>c!=null && c.GetType().Name=="ModularAvatarShapeChanger")) {
                var so=new SerializedObject(changer);var shapes=so.FindProperty("m_shapes")??so.FindProperty("Shapes");
                if(shapes==null) {issues.Add(Issue("SHAPE_CHANGER_SCHEMA_UNSUPPORTED",P(changer.transform),"Inspect the installed MA version; no targets were validated."));continue;}
                for(int i=0;i<shapes.arraySize;i++) {
                    var entry=shapes.GetArrayElementAtIndex(i);var reference=entry.FindPropertyRelative("Object");
                    var direct=reference!=null?reference.FindPropertyRelative("targetObject"):null;
                    var target=direct!=null?direct.objectReferenceValue as GameObject:null;
                    string resolution=target!=null?"direct":"unresolved";
                    var referencePath=Text(reference!=null?reference.FindPropertyRelative("referencePath"):null);
                    if(target==null && !string.IsNullOrEmpty(referencePath)) {
                        var relativeTarget=referencePath.StartsWith(TargetPath+"/",StringComparison.Ordinal)?referencePath.Substring(TargetPath.Length+1):referencePath;
                        var found=root.Find(relativeTarget);if(found!=null){target=found.gameObject;resolution="relative_path";}
                    }
                    bool inAvatar=target!=null && (target.transform==root || target.transform.IsChildOf(root));
                    var smr=inAvatar?target.GetComponent<SkinnedMeshRenderer>():null;
                    var shapeProperty=entry.FindPropertyRelative("ShapeName");string shape=Text(shapeProperty);
                    var change=entry.FindPropertyRelative("ChangeType");var value=entry.FindPropertyRelative("Value");
                    var operation=change!=null && change.propertyType==SerializedPropertyType.Enum && change.enumValueIndex>=0 && change.enumValueIndex<change.enumNames.Length?change.enumNames[change.enumValueIndex]:"UNKNOWN";
                    bool exists=smr!=null && smr.sharedMesh!=null && smr.sharedMesh.GetBlendShapeIndex(shape)>=0;
                    shapeChecks.Add(new {owner=P(changer.transform),target=target!=null?P(target.transform):referencePath,
                        shape=shape,operation=operation,value=value!=null?value.floatValue:0,
                        resolution=resolution,inAvatar=inAvatar,shapeExists=exists});
                    if(target==null) issues.Add(Issue("SHAPE_TARGET_UNRESOLVED",P(changer.transform),referencePath));
                    else if(!inAvatar) issues.Add(Issue("SHAPE_TARGET_OUTSIDE_AVATAR",P(changer.transform),P(target.transform)));
                    else if(shapeProperty==null) issues.Add(Issue("SHAPE_CHANGER_SCHEMA_UNSUPPORTED",P(changer.transform),"ShapeName field missing; inspect the installed version."));
                    else if(!exists) issues.Add(Issue("SHAPE_NOT_ON_TARGET",P(changer.transform),shape+" -> "+P(target.transform)));
                }
            }
        }
        var bodies=new List<object>();
        var bodyRenderers=new List<SkinnedMeshRenderer>();
        if(BodyRendererPaths.Length==0)
            bodyRenderers.AddRange(root.GetComponentsInChildren<SkinnedMeshRenderer>(true).Where(r=>r.transform.parent==root && r.name.StartsWith("Body",StringComparison.OrdinalIgnoreCase)));
        else foreach(var relative in BodyRendererPaths.Distinct()) {
            var target=root.Find(relative);var renderer=target!=null?target.GetComponent<SkinnedMeshRenderer>():null;
            if(renderer==null) issues.Add(Issue("BODY_RENDERER_NOT_FOUND",relative,"Resolve the exact affected body renderer."));
            else bodyRenderers.Add(renderer);
        }
        foreach(var renderer in bodyRenderers) {
            var mesh=renderer.sharedMesh;var nonzero=new List<object>();
            if(mesh!=null) for(int i=0;i<mesh.blendShapeCount;i++) {
                float weight=renderer.GetBlendShapeWeight(i);
                if(Math.Abs(weight)>0.0001f) nonzero.Add(new {name=mesh.GetBlendShapeName(i),weight=weight});
            }
            bodies.Add(new {path=P(renderer.transform),mesh=mesh!=null?AssetDatabase.GetAssetPath(mesh):null,
                shapeCount=mesh!=null?mesh.blendShapeCount:0,nonzeroShapes=nonzero,
                materials=renderer.sharedMaterials.Select(m=>m!=null?AssetDatabase.GetAssetPath(m):null).ToArray()});
        }

        return new {evidence="UNITY_RESOLVED",project=Application.dataPath,unity=Application.unityVersion,
            scene=scene.path,sceneDirty=scene.isDirty,target=TargetPath,compiling=EditorApplication.isCompiling,
            updating=EditorApplication.isUpdating,playing=EditorApplication.isPlaying,
            installers=installers,groups=groups,parameterDeclarations=declarations,issues=issues,
            outfitGeometry=outfits,shapeTargetChecks=shapeChecks,
            bodyMeshes=bodies,missingScriptCount=components.Count(c=>c==null),
            sourceActiveChildren=root.Cast<Transform>().Select(t=>new {name=t.name,active=t.gameObject.activeSelf}).ToArray(),
            acceptance="SOURCE_CHECK_ONLY_NOT_VISUAL_OR_BUILT_ACCEPTANCE"};
    }
}
