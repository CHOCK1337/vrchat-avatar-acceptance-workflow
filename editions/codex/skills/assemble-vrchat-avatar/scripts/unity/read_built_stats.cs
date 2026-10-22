// Editor command template; no runtime component. Render using optimization.py.
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using UnityEngine;
using UnityEditor;

public class EditorCommand
{
    static Type Find(string name)
    {
        return AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
    }

    static string Hash(string path)
    {
        using (var algorithm = SHA256.Create())
        using (var file = File.OpenRead(path))
            return BitConverter.ToString(algorithm.ComputeHash(file)).Replace("-", "").ToLowerInvariant();
    }

    public static object Execute()
    {
        string project = __PROJECT_PATH_LITERAL__;
        string bundlePath = __BUNDLE_PATH_LITERAL__;
        string output = __OUTPUT_PATH_LITERAL__;
        string live = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        if (!string.Equals(live.TrimEnd(Path.DirectorySeparatorChar), project.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new Exception("Wrong live Unity project");
        if (EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
            throw new Exception("Unity is busy; wait rather than load another bundle");
        if (EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows64 &&
            EditorUserBuildSettings.activeBuildTarget != BuildTarget.StandaloneWindows)
            throw new Exception("This verified stats adapter is for Windows PC; use actual platform SDK statistics for other targets");
        if (File.Exists(output)) throw new Exception("Statistics already exist; read them before retrying");
        AssetBundle bundle = null;
        try
        {
            string bundleSha = Hash(bundlePath);
            bundle = AssetBundle.LoadFromFile(bundlePath);
            if (!bundle) throw new Exception("Built bundle cannot be loaded");
            string assetName = bundle.GetAllAssetNames().Single();
            var root = bundle.LoadAsset<GameObject>(assetName);
            if (!root) throw new Exception("No built Avatar GameObject");
            var statsType = Find("VRC.SDKBase.Validation.Performance.Stats.AvatarPerformanceStats");
            var stats = Activator.CreateInstance(statsType, new object[] { false });
            Find("VRC.SDKBase.Validation.Performance.AvatarPerformance").GetMethod("CalculatePerformanceStats")
                .Invoke(null, new object[] { root.name, root, stats, false });
            var values = statsType.GetFields(BindingFlags.Public | BindingFlags.Instance)
                .Where(f => f.Name != "aabb").ToDictionary(f => f.Name, f => f.GetValue(stats));
            var category = Find("VRC.SDKBase.Validation.Performance.AvatarPerformanceCategory");
            values["rank"] = statsType.GetMethod("GetPerformanceRatingForCategory")
                .Invoke(stats, new[] { Enum.Parse(category, "Overall") }).ToString();
            var descriptor = root.GetComponents<Component>().FirstOrDefault(c => c && c.GetType().Name == "VRCAvatarDescriptor");
            UnityEngine.Object parameters = descriptor ?
                (UnityEngine.Object)descriptor.GetType().GetField("expressionParameters").GetValue(descriptor) : null;
            values["parameterCost"] = parameters ? parameters.GetType().GetMethod("CalcTotalCost").Invoke(parameters, null) : null;
            values["gameObjects"] = root.GetComponentsInChildren<Transform>(true).Length;
            values["blendShapes"] = root.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .Where(renderer => renderer.sharedMesh).Sum(renderer => renderer.sharedMesh.blendShapeCount);
            string sdk = UnityEditor.PackageManager.PackageInfo.GetAllRegisteredPackages()
                .Where(package => package.name == "com.vrchat.avatars").Select(package => package.version).FirstOrDefault();
            var result = new { source = "Unity AssetBundle.LoadAsset + installed SDK CalculatePerformanceStats",
                bundle = bundlePath, bundle_sha256 = bundleSha, compressedBytes = new FileInfo(bundlePath).Length,
                sdk_version = sdk, unity_version = Application.unityVersion,
                platform = EditorUserBuildSettings.activeBuildTarget.ToString(), assetName = assetName,
                metrics = values, captured_at = DateTime.UtcNow.ToString("o"), visual_verified = false };
            string json = (string)Find("Newtonsoft.Json.JsonConvert").GetMethod("SerializeObject", new[] { typeof(object) })
                .Invoke(null, new object[] { result });
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            using (var stream = new FileStream(output, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream, new System.Text.UTF8Encoding(false))) writer.Write(json);
            return new { saved = output, bundle_sha256 = bundleSha, metrics = values, visual_verified = false };
        }
        finally { if (bundle) bundle.Unload(true); }
    }
}
