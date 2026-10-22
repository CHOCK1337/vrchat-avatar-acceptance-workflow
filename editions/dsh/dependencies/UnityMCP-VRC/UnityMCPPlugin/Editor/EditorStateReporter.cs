using UnityEngine;
using UnityEditor;
using System;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Linq;

namespace UnityMCP.Editor
{
    public class EditorStateReporter
    {
        private string lastErrorMessage = "";

        // Bounds so a large scene/project can't return a multi-megabyte state dump.
        private const int MaxFlatList = 300;       // activeGameObjects / asset paths
        private const int MaxHierarchyNodes = 500; // total nodes walked in the hierarchy
        private const int MaxHierarchyDepth = 8;   // max recursion depth
        private int hierNodeCount;

        private static List<string> Cap(IEnumerable<string> src)
        {
            var list = src != null ? src.ToList() : new List<string>();
            if (list.Count <= MaxFlatList) return list;
            var capped = list.Take(MaxFlatList).ToList();
            capped.Add($"...(+{list.Count - MaxFlatList} more, {list.Count} total)");
            return capped;
        }

        // Gathers editor state and returns it as the payload for an "editorState" response. The
        // connection layer serializes it (echoing the request id) back to the requesting client.
        // Gathering touches Unity APIs, so it runs on the main thread via RunOnMainThread, which
        // defers while the Editor is compiling and only times out if the Editor stays unfocused
        // past its limit. On failure we return an { error } payload so the client fails fast with
        // a useful message rather than hanging.
        public async Task<object> GetEditorStateData()
        {
            try
            {
                return await EditorUtilities.RunOnMainThread(() => GetEditorState()).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[UnityMCP] Could not get editor state: {ex.Message}");
                return new { error = ex.Message };
            }
        }

        public object GetEditorState()
        {
            try
            {
                var activeGameObjects = new List<string>();
                var selectedObjects = new List<string>();

                // FindObjectsByType with SortMode.None is the modern, unsorted (so cheaper)
                // replacement for the now-deprecated FindObjectsOfType.
                var foundObjects = GameObject.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
                if (foundObjects != null)
                {
                    foreach (var obj in foundObjects)
                    {
                        if (obj != null && !string.IsNullOrEmpty(obj.name))
                        {
                            activeGameObjects.Add(obj.name);
                        }
                    }
                }

                var selection = Selection.gameObjects;
                if (selection != null)
                {
                    foreach (var obj in selection)
                    {
                        if (obj != null && !string.IsNullOrEmpty(obj.name))
                        {
                            selectedObjects.Add(obj.name);
                        }
                    }
                }

                var currentScene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
                var sceneHierarchy = currentScene.IsValid() ? GetSceneHierarchy() : new List<object>();

                var projectStructure = new
                {
                    scenes = GetSceneNames() ?? new string[0],
                    assets = Cap(GetAssetPaths())
                };

                return new
                {
                    activeGameObjects = Cap(activeGameObjects),
                    activeGameObjectCount = activeGameObjects.Count,
                    selectedObjects,
                    playModeState = EditorApplication.isPlaying ? "Playing" : "Stopped",
                    sceneHierarchy,
                    projectStructure
                };
            }
            catch (Exception e)
            {
                lastErrorMessage = $"Error getting editor state: {e.Message}";
                Debug.LogError(lastErrorMessage);
                return new
                {
                    activeGameObjects = new List<string>(),
                    selectedObjects = new List<string>(),
                    playModeState = "Unknown",
                    sceneHierarchy = new List<object>(),
                    projectStructure = new { scenes = new string[0], assets = new string[0] }
                };
            }
        }

        private object GetSceneHierarchy()
        {
            try
            {
                var roots = new List<object>();
                var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();

                if (scene.IsValid())
                {
                    hierNodeCount = 0;
                    var rootObjects = scene.GetRootGameObjects();
                    if (rootObjects != null)
                    {
                        foreach (var root in rootObjects)
                        {
                            if (root != null)
                            {
                                try
                                {
                                    roots.Add(GetGameObjectHierarchy(root, 0));
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"[UnityMCP] Failed to get hierarchy for {root.name}: {e.Message}");
                                }
                            }
                        }
                    }
                }

                return roots;
            }
            catch (Exception e)
            {
                lastErrorMessage = $"Error getting scene hierarchy: {e.Message}";
                Debug.LogError(lastErrorMessage);
                return new List<object>();
            }
        }

        private object GetGameObjectHierarchy(GameObject obj, int depth)
        {
            try
            {
                if (obj == null) return null;
                hierNodeCount++;

                var children = new List<object>();
                var transform = obj.transform;

                if (transform != null && depth < MaxHierarchyDepth && hierNodeCount < MaxHierarchyNodes)
                {
                    for (int i = 0; i < transform.childCount; i++)
                    {
                        if (hierNodeCount >= MaxHierarchyNodes)
                        {
                            children.Add($"...(hierarchy truncated at {MaxHierarchyNodes} nodes)");
                            break;
                        }
                        try
                        {
                            var childTransform = transform.GetChild(i);
                            if (childTransform != null && childTransform.gameObject != null)
                            {
                                var childHierarchy = GetGameObjectHierarchy(childTransform.gameObject, depth + 1);
                                if (childHierarchy != null)
                                {
                                    children.Add(childHierarchy);
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[UnityMCP] Failed to process child {i} of {obj.name}: {e.Message}");
                        }
                    }
                }

                return new
                {
                    name = obj.name ?? "Unnamed",
                    components = GetComponentNames(obj),
                    childCount = transform != null ? transform.childCount : 0,
                    children = children
                };
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityMCP] Failed to get hierarchy for {(obj != null ? obj.name : "null")}: {e.Message}");
                return null;
            }
        }

        private string[] GetComponentNames(GameObject obj)
        {
            try
            {
                if (obj == null) return new string[0];

                var components = obj.GetComponents<Component>();
                if (components == null) return new string[0];

                var validComponents = new List<string>();
                foreach (var component in components)
                {
                    try
                    {
                        if (component != null)
                        {
                            validComponents.Add(component.GetType().Name);
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"[UnityMCP] Failed to get component name: {e.Message}");
                    }
                }

                return validComponents.ToArray();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityMCP] Failed to get component names for {(obj != null ? obj.name : "null")}: {e.Message}");
                return new string[0];
            }
        }

        private static string[] GetSceneNames()
        {
            var scenes = new List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        private static string[] GetAssetPaths()
        {
            var guids = AssetDatabase.FindAssets("", new[] { "Assets/" });
            var paths = new string[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            }
            return paths;
        }
    }
}
