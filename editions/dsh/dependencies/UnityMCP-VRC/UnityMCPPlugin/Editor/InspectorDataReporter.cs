using UnityEngine;
using UnityEditor;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;

namespace UnityMCP.Editor
{
    public class InspectorDataReporter
    {
        public class ObjectDetailsData
        {
            public string objectName { get; set; }
            public bool includeInactive { get; set; }
        }

        // Properties that are unsafe to read (they instantiate copies) or just noise.
        private static readonly HashSet<string> SkipProperties = new HashSet<string>
        {
            "mesh", "material", "materials",                       // reading these instantiates copies
            "sharedMesh", "sharedMaterial", "sharedMaterials",     // surfaced explicitly below instead
            "bounds", "localBounds",                               // surfaced explicitly below instead
            "gameObject", "transform", "name", "tag", "hideFlags"  // redundant / noisy
        };

        // Cap how many elements of a collection we serialize, to keep the payload bounded.
        private const int MaxCollectionElements = 10;

        // How deep to expand nested user-defined types before falling back to a { type } stub.
        // Bounds the payload and stops runaway recursion on self-referential/cyclic types.
        private const int MaxDepth = 3;

        // Gathers a GameObject's details and returns them as the payload for an "objectDetails"
        // response (or { error } if the object isn't found / gathering fails). The connection layer
        // serializes this, echoing the request id, back to the requesting client. Reflection over
        // Unity objects must run on the main thread, so it goes through RunOnMainThread, which defers
        // while the Editor is compiling and only times out if the Editor stays unfocused past its limit.
        public async Task<object> GetObjectDetailsData(string dataJson)
        {
            try
            {
                var requestData = JsonConvert.DeserializeObject<ObjectDetailsData>(dataJson) ?? new ObjectDetailsData();

                return await EditorUtilities
                    .RunOnMainThread(() => GetObjectDetails(requestData.objectName, requestData.includeInactive))
                    .ConfigureAwait(false);
            }
            catch (Exception e)
            {
                Debug.LogError($"[UnityMCP] Error getting object details: {e.Message}");
                return new { error = e.Message };
            }
        }

        private object GetObjectDetails(string nameOrPath, bool includeInactive)
        {
            var obj = FindGameObject(nameOrPath, includeInactive);
            if (obj == null)
            {
                string hint = includeInactive
                    ? ""
                    : " (searched active objects only; pass includeInactive=true to include disabled objects)";
                return new { error = $"GameObject '{nameOrPath}' not found.{hint}" };
            }

            var componentsData = new List<object>();
            foreach (var comp in obj.GetComponents<Component>())
            {
                if (comp == null) continue;

                var compType = comp.GetType();
                var fieldsData = new Dictionary<string, object>();

                // Public fields are stored values - always safe to read.
                foreach (var field in compType.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try { fieldsData[field.Name] = SummarizeValue(field.GetValue(comp)); }
                    catch { }
                }

                // Public readable, non-indexed properties (minus the unsafe/noisy skip list).
                foreach (var prop in compType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    if (SkipProperties.Contains(prop.Name)) continue;
                    // Reading [Obsolete] getters (e.g. AudioSource.rolloffFactor/minVolume/maxVolume)
                    // spams the console with deprecation warnings - and the values are redundant anyway.
                    if (prop.IsDefined(typeof(ObsoleteAttribute), inherit: true)) continue;
                    try { fieldsData[prop.Name] = SummarizeValue(prop.GetValue(comp)); }
                    catch { }
                }

                // High-value extras that reflection skips (bounds, mesh/material info) - no instantiation.
                AddSpecialComponentData(comp, fieldsData);

                componentsData.Add(new { type = compType.Name, data = fieldsData });
            }

            var children = new List<string>();
            foreach (Transform child in obj.transform)
            {
                children.Add(child.name);
            }

            return new
            {
                name = obj.name,
                path = GetHierarchyPath(obj),
                active = obj.activeSelf,
                activeInHierarchy = obj.activeInHierarchy,
                tag = obj.tag,
                layer = LayerMask.LayerToName(obj.layer),
                transform = new
                {
                    position = FormatVector(obj.transform.position),
                    rotation = FormatVector(obj.transform.rotation.eulerAngles),
                    localScale = FormatVector(obj.transform.localScale),
                    lossyScale = FormatVector(obj.transform.lossyScale)
                },
                childCount = obj.transform.childCount,
                children = children,
                components = componentsData
            };
        }

        // Resolves a GameObject by plain name or hierarchy path ("Parent/Child/Leaf"),
        // optionally including inactive objects.
        private GameObject FindGameObject(string nameOrPath, bool includeInactive)
        {
            if (string.IsNullOrEmpty(nameOrPath)) return null;

            // Fast path: GameObject.Find handles active objects and "/" paths from a root.
            var direct = GameObject.Find(nameOrPath);
            if (direct != null) return direct;

            bool isPath = nameOrPath.Contains('/');
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>())
            {
                // Only scene objects (exclude prefab assets and other persistent objects).
                if (!go.scene.IsValid()) continue;
                if (EditorUtility.IsPersistent(go)) continue;
                if (!includeInactive && !go.activeInHierarchy) continue;

                if (isPath ? GetHierarchyPath(go) == nameOrPath : go.name == nameOrPath)
                {
                    return go;
                }
            }
            return null;
        }

        private string GetHierarchyPath(GameObject go)
        {
            var sb = new StringBuilder(go.name);
            var t = go.transform.parent;
            while (t != null)
            {
                sb.Insert(0, t.name + "/");
                t = t.parent;
            }
            return sb.ToString();
        }

        // Surfaces size/asset info the plain field+property reflection above deliberately skips
        // (those accessors are in the skip-list or instantiate copies): renderer/mesh bounds, vertex
        // counts, and shared mesh/material names - all read without instantiating anything.
        private void AddSpecialComponentData(Component comp, Dictionary<string, object> data)
        {
            if (comp is Renderer renderer)
            {
                try { data["bounds"] = FormatBounds(renderer.bounds); } catch { }
                try
                {
                    var mats = renderer.sharedMaterials;
                    data["sharedMaterials"] = mats.Select(m => m != null ? m.name : "null").ToArray();
                    data["sharedMaterialCount"] = mats.Length;
                }
                catch { }
            }

            if (comp is MeshFilter mf && mf.sharedMesh != null)
            {
                var mesh = mf.sharedMesh;
                data["sharedMesh"] = mesh.name;
                data["vertexCount"] = mesh.vertexCount;
                data["subMeshCount"] = mesh.subMeshCount;
                data["localBounds"] = FormatBounds(mesh.bounds);
            }

            if (comp is SkinnedMeshRenderer smr && smr.sharedMesh != null)
            {
                var mesh = smr.sharedMesh;
                data["sharedMesh"] = mesh.name;
                data["vertexCount"] = mesh.vertexCount;
                data["subMeshCount"] = mesh.subMeshCount;
                data["localBounds"] = FormatBounds(mesh.bounds);
            }
        }

        // Converts an arbitrary field/property value into something JSON-friendly and bounded. The
        // strategy: render primitives, Unity math structs, and asset/scene-object references inline;
        // cap any collection to MaxCollectionElements; and expand a user-defined type's own fields one
        // level deeper while stubbing Unity/BCL "framework" types as { type } (so we don't dump noise
        // like a Matrix4x4's 16 floats). The two caps work on different axes - MaxCollectionElements
        // bounds breadth, `depth`/MaxDepth bounds nesting - so even a deep, wide graph stays bounded.
        private object SummarizeValue(object val, int depth = 0)
        {
            if (val == null) return null;

            if (val is Vector2 v2) return new { x = v2.x, y = v2.y };
            if (val is Vector3 v3) return new { x = v3.x, y = v3.y, z = v3.z };
            if (val is Vector4 v4) return new { x = v4.x, y = v4.y, z = v4.z, w = v4.w };
            if (val is Quaternion q) return new { x = q.x, y = q.y, z = q.z, w = q.w };
            if (val is Color c) return new { r = c.r, g = c.g, b = c.b, a = c.a };
            if (val is Rect r) return new { x = r.x, y = r.y, width = r.width, height = r.height };

            var type = val.GetType();
            if (type.IsPrimitive || val is string || type.IsEnum) return val;

            // Asset / scene-object reference: report name + type (reading the ref doesn't instantiate).
            if (val is UnityEngine.Object uo)
            {
                if (uo == null) return null; // destroyed object
                return new { reference = uo.name, type = uo.GetType().Name };
            }

            // Collections: report length + a capped, recursively-summarized preview of elements.
            if (val is System.Collections.IEnumerable enumerable && !(val is string))
            {
                var elements = new List<object>();
                int count = 0;
                foreach (var item in enumerable)
                {
                    count++;
                    if (elements.Count < MaxCollectionElements) elements.Add(SummarizeValue(item, depth + 1));
                }
                return new { count, elements };
            }

            // Plain user data types (classes/structs outside the Unity namespaces): expand their
            // public fields + simple properties one level deeper (bounded by MaxDepth) so list
            // elements and nested data aren't opaque - e.g. a List<Target> shows each Target's fps
            // instead of a bare "Target". Unity engine/editor types we don't special-case above
            // (Matrix4x4, Bounds, ...) stay a compact { type } stub so we don't dump noise like a
            // matrix's 16 floats. UnityEngine.Object values are handled by the reference branch
            // above, so we never instantiate copies (the mesh/material concern) by recursing here.
            if (depth < MaxDepth && !IsFrameworkType(type))
            {
                var data = new Dictionary<string, object>();

                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    try { data[field.Name] = SummarizeValue(field.GetValue(val), depth + 1); }
                    catch { }
                }

                foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!prop.CanRead || prop.GetIndexParameters().Length > 0) continue;
                    if (data.ContainsKey(prop.Name)) continue;
                    try { data[prop.Name] = SummarizeValue(prop.GetValue(val), depth + 1); }
                    catch { }
                }

                if (data.Count > 0) return data;
            }

            return new { type = type.Name };
        }

        // True for engine/BCL framework types (UnityEngine/UnityEditor/System/Microsoft namespaces).
        // We render those as a compact { type } stub rather than expanding their fields - this avoids
        // dumping Matrix4x4's 16 floats or a MonoBehaviour's destroyCancellationToken internals, and
        // keeps DateTime/TimeSpan/Guid/etc. from exploding. User/game types (a game namespace, or no
        // namespace) fall through and get expanded, which is the point.
        private static bool IsFrameworkType(Type type)
        {
            var ns = type.Namespace;
            if (ns == null) return false; // user types frequently have no namespace
            return ns == "UnityEngine" || ns.StartsWith("UnityEngine.") ||
                   ns == "UnityEditor" || ns.StartsWith("UnityEditor.") ||
                   ns == "System" || ns.StartsWith("System.") ||
                   ns == "Microsoft" || ns.StartsWith("Microsoft.");
        }

        private object FormatVector(Vector3 v) => new { x = v.x, y = v.y, z = v.z };

        private object FormatBounds(Bounds b)
        {
            return new
            {
                center = new { x = b.center.x, y = b.center.y, z = b.center.z },
                size = new { x = b.size.x, y = b.size.y, z = b.size.z },
                extents = new { x = b.extents.x, y = b.extents.y, z = b.extents.z }
            };
        }
    }
}
