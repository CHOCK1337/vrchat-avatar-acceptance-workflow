using UnityEngine;
using UnityEditor;
using System;

namespace UnityMCP.Editor
{
    public class UnityMCPWindow : EditorWindow
    {
        private double lastRepaint;
        private Vector2 callsScroll;

        [MenuItem("UnityMCP/Debug Window", false, 1)]
        public static void ShowWindow()
        {
            GetWindow<UnityMCPWindow>("UnityMCP Debug");
        }

        void OnEnable()
        {
            EditorApplication.update += Tick;
        }

        void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        // Repaint a couple of times a second so the live values (uptime, "x ago",
        // queue depth) keep ticking while the window is open.
        void Tick()
        {
            if (EditorApplication.timeSinceStartup - lastRepaint >= 0.5)
            {
                lastRepaint = EditorApplication.timeSinceStartup;
                Repaint();
            }
        }

        void OnGUI()
        {
            try
            {
                EditorGUILayout.Space(10);
                GUILayout.Label("UnityMCP Debug", EditorStyles.boldLabel);
                EditorGUILayout.Space(5);

                DrawServerStatus();
                EditorGUILayout.Space(5);
                DrawAddress();
                EditorGUILayout.Space(5);
                DrawInstance();
                EditorGUILayout.Space(5);
                DrawLastRequest();
                EditorGUILayout.Space(5);
                DrawEditorState();

                EditorGUILayout.Space(8);
                DrawLoggingToggle();

                EditorGUILayout.Space(10);
                DrawRecentCalls();

                EditorGUILayout.Space(10);
                if (GUILayout.Button("Restart Server", GUILayout.Height(30)))
                {
                    UnityMCPConnection.RetryConnection();
                }

                EditorGUILayout.Space(10);
                if (!string.IsNullOrEmpty(UnityMCPConnection.LastErrorMessage))
                {
                    EditorGUILayout.LabelField("Last Error:", EditorStyles.boldLabel);
                    EditorGUILayout.HelpBox(UnityMCPConnection.LastErrorMessage, MessageType.Error);
                }
            }
            catch (Exception e)
            {
                EditorGUILayout.HelpBox($"Error in debug window: {e.Message}", MessageType.Error);
            }
        }

        // The authoritative signal: did the plugin actually bind the port and is it accepting
        // connections? If this is red, another process is likely holding the port (see Last Error).
        private static void DrawServerStatus()
        {
            bool listening = UnityMCPConnection.IsListening;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Server:", GUILayout.Width(110));
            GUI.color = listening ? Color.green : Color.red;
            string text = listening
                ? $"Listening  ·  up {FormatDuration(DateTime.UtcNow - UnityMCPConnection.ServerStartedUtc)}"
                : "NOT listening (port unavailable?)";
            EditorGUILayout.LabelField(text, EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawAddress()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Address:", GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(
                UnityMCPConnection.ServerUri.ToString(), EditorStyles.textField, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();
        }

        // Which instance this Editor advertises to MCP servers (its discovery handle). With several
        // Editors open, this is the name/id a client passes to select_unity_instance.
        private static void DrawInstance()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Instance:", GUILayout.Width(110));
            EditorGUILayout.SelectableLabel(
                $"{UnityMCPConnection.InstanceName}  ·  {UnityMCPConnection.InstanceId}",
                EditorStyles.textField, GUILayout.Height(20));
            EditorGUILayout.EndHorizontal();
        }

        // Confirms the link is actually doing work, not just idle-connected.
        private static void DrawLastRequest()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Last request:", GUILayout.Width(110));
            string text = UnityMCPConnection.TotalRequestCount == 0
                ? "none yet"
                : $"{UnityMCPConnection.LastRequestType}  ·  {FormatDuration(DateTime.UtcNow - UnityMCPConnection.LastRequestUtc)} ago" +
                  $"  ·  {UnityMCPConnection.TotalRequestCount} total";
            EditorGUILayout.LabelField(text);
            EditorGUILayout.EndHorizontal();
        }

        // Surfaces the conditions that make a tool call stall: Unity compiling, or work backed up
        // in the main-thread queue (e.g. the Editor is unfocused and throttled).
        private static void DrawEditorState()
        {
            bool compiling = EditorApplication.isCompiling;
            int queued = EditorUtilities.PendingMainThreadActions;
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Editor:", GUILayout.Width(110));
            GUI.color = (compiling || queued > 0) ? Color.yellow : Color.white;
            EditorGUILayout.LabelField($"{(compiling ? "Compiling…" : "Ready")}  ·  main-thread queue: {queued}");
            GUI.color = Color.white;
            EditorGUILayout.EndHorizontal();
        }

        // A scrollable log of the most recent tool calls (newest first): when it happened, which
        // tool, and the call's comment - so the developer can watch what Claude is doing.
        private void DrawRecentCalls()
        {
            var calls = UnityMCPConnection.GetRecentCalls();
            EditorGUILayout.LabelField($"Recent calls ({calls.Length})", EditorStyles.boldLabel);

            callsScroll = EditorGUILayout.BeginScrollView(callsScroll, GUILayout.Height(360));
            if (calls.Length == 0)
            {
                EditorGUILayout.LabelField("No calls yet.", EditorStyles.miniLabel);
            }
            else
            {
                DateTime now = DateTime.UtcNow;
                foreach (var c in calls)
                {
                    EditorGUILayout.BeginVertical(EditorStyles.helpBox);

                    EditorGUILayout.BeginHorizontal();
                    // Local wall-clock time plus a live "x ago" (the window repaints ~2x/sec).
                    EditorGUILayout.LabelField(
                        $"{c.Utc.ToLocalTime():HH:mm:ss}  ·  {FormatDuration(now - c.Utc)} ago",
                        EditorStyles.miniLabel, GUILayout.Width(150));
                    EditorGUILayout.LabelField(c.Type, EditorStyles.miniBoldLabel);
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.LabelField(
                        string.IsNullOrEmpty(c.Comment) ? "(no comment)" : c.Comment,
                        EditorStyles.wordWrappedMiniLabel);

                    EditorGUILayout.EndVertical();
                }
            }
            EditorGUILayout.EndScrollView();
        }

        private static void DrawLoggingToggle()
        {
            bool current = UnityMCPConnection.IsLoggingEnabled;
            bool updated = EditorGUILayout.ToggleLeft(
                $"Buffer Unity logs for get_logs  (buffered: {UnityMCPConnection.BufferedLogCount})",
                current);
            if (updated != current)
            {
                UnityMCPConnection.IsLoggingEnabled = updated;
            }
        }

        private static string FormatDuration(TimeSpan ts)
        {
            if (ts.TotalSeconds < 0) ts = TimeSpan.Zero;
            if (ts.TotalHours >= 1) return $"{(int)ts.TotalHours}h {ts.Minutes:00}m";
            if (ts.TotalMinutes >= 1) return $"{ts.Minutes}m {ts.Seconds:00}s";
            return $"{ts.Seconds}s";
        }
    }
}
