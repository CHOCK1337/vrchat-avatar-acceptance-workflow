using UnityEngine;
using UnityEditor;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.ExceptionServices;
using Newtonsoft.Json;
using Microsoft.CSharp;
using System.CodeDom.Compiler;

namespace UnityMCP.Editor
{
    public class EditorCommandExecutor
    {
        public class EditorCommandData
        {
            public string code { get; set; }
        }

        // Compiles and runs the supplied C# on Unity's main thread and returns the result
        // payload ({ result, logs, errors, warnings, executionSuccess, [errorDetails] }). The
        // connection layer serializes this into a "commandResult" message (echoing the request
        // id) and sends it back to the requesting client. Running on the main thread is required
        // because the code touches Unity APIs; RunOnMainThread also defers while the Editor is
        // compiling, so the snippet executes against stable post-compile state.
        public static Task<object> ExecuteAndGetResult(string commandData)
        {
            var commandObj = JsonConvert.DeserializeObject<EditorCommandData>(commandData);
            var code = commandObj?.code;

            return EditorUtilities.RunOnMainThread<object>(() =>
            {
                var logs = new List<string>();
                var errors = new List<string>();
                var warnings = new List<string>();

                void LogHandler(string message, string stackTrace, LogType type)
                {
                    switch (type)
                    {
                        case LogType.Log:
                            logs.Add(message);
                            break;
                        case LogType.Warning:
                            warnings.Add(message);
                            break;
                        case LogType.Error:
                        case LogType.Exception:
                            var stackLine = stackTrace?.Split('\n')?.FirstOrDefault() ?? "";
                            errors.Add($"{message}\n{stackLine}");
                            break;
                    }
                }

                // Capture logs for the duration of this command only. Because the main-thread
                // queue drains sequentially, commands never execute concurrently, so this scopes
                // cleanly to the single snippet.
                Application.logMessageReceived += LogHandler;
                try
                {
                    Debug.Log("[UnityMCP] Executing code...");
                    var result = CompileAndExecute(code);
                    Debug.Log("[UnityMCP] Code executed");

                    return (object)new
                    {
                        result = result,
                        logs = logs,
                        errors = errors,
                        warnings = warnings,
                        executionSuccess = true
                    };
                }
                catch (Exception e)
                {
                    var firstStackLine = e.StackTrace?.Split('\n')?.FirstOrDefault() ?? "";
                    var error = $"[UnityMCP] Failed to execute editor command: {e.Message}\n{firstStackLine}";
                    Debug.LogError(error);

                    return (object)new
                    {
                        result = (object)null,
                        logs = logs,
                        errors = new List<string>(errors) { error },
                        warnings = warnings,
                        executionSuccess = false,
                        errorDetails = new
                        {
                            message = e.Message,
                            stackTrace = firstStackLine,
                            type = e.GetType().Name
                        }
                    };
                }
                finally
                {
                    Application.logMessageReceived -= LogHandler;
                }
            });
        }

        // Two compile backends share one auto-discovered reference list:
        //  - Mono's in-process CodeDom compiler, on projects whose API compatibility level is
        //    .NET Framework (e.g. Unity 2022-era VRChat projects).
        //  - The Roslyn csc bundled inside the Editor installation, run out of process, on
        //    projects whose API level is .NET Standard (the Unity 6 default) - there the
        //    CodeDom provider is a stub whose compile methods throw PlatformNotSupportedException.
        // The first PlatformNotSupportedException flips the choice to Roslyn. It's cached in
        // SessionState (not just a static), so the probe + its log line happen once per Editor
        // *session* rather than once per domain reload - statics reset on reload, SessionState
        // survives it. A full Editor restart re-probes once, which is correct: the project's API
        // profile can change between runs. CodeDom behavior is untouched on projects where it works.
        const string UseBundledRoslynKey = "UnityMCP.UseBundledRoslyn";
        static int s_useBundledRoslyn = -1; // -1 = not yet resolved in this domain
        static string s_bundledDotnet;
        static string s_bundledCsc;

        static bool UseBundledRoslyn
        {
            get
            {
                if (s_useBundledRoslyn < 0)
                    s_useBundledRoslyn = SessionState.GetBool(UseBundledRoslynKey, false) ? 1 : 0;
                return s_useBundledRoslyn == 1;
            }
            set
            {
                s_useBundledRoslyn = value ? 1 : 0;
                SessionState.SetBool(UseBundledRoslynKey, value);
            }
        }

        public static object CompileAndExecute(string code)
        {
            // No blocking wait-for-compile here. Callers route through
            // EditorUtilities.RunOnMainThread, whose queue already defers while the Editor is
            // compiling, so by the time this runs the domain is stable. (The Script Tester window
            // calls this directly from a user click, which is also never mid-compile.)
            var references = GatherReferenceAssemblies();

            Assembly assembly;
            if (UseBundledRoslyn)
            {
                assembly = CompileWithBundledRoslyn(code, references);
            }
            else
            {
                try
                {
                    assembly = CompileWithCodeDom(code, references);
                }
                catch (PlatformNotSupportedException)
                {
                    UseBundledRoslyn = true;
                    Debug.Log("[UnityMCP] CodeDom compilation is unavailable under this project's API profile; using the Editor's bundled Roslyn compiler from now on.");
                    assembly = CompileWithBundledRoslyn(code, references);
                }
            }

            return InvokeEditorCommand(assembly);
        }

        // Builds the reference list both backends compile against. Since compilation runs with
        // /nostdlib+ (no implicit references), this list is the complete universe the snippet
        // can see.
        static List<string> GatherReferenceAssemblies()
        {
            var references = new List<string>();
            var added = new HashSet<string>();

            void AddAssemblyReference(string assemblyPath)
            {
                if (!string.IsNullOrEmpty(assemblyPath) && added.Add(assemblyPath))
                {
                    references.Add(assemblyPath);
                }
            }

            try
            {
                // Add engine/editor core references
                AddAssemblyReference(typeof(UnityEngine.Object).Assembly.Location);
                AddAssemblyReference(typeof(UnityEditor.Editor).Assembly.Location);

                AddAssemblyReference(typeof(System.Linq.Enumerable).Assembly.Location); // Add System.Core for LINQ
                AddAssemblyReference(typeof(object).Assembly.Location); // Add mscorlib

                // Add this assembly so script can use utilities we provide
                AddAssemblyReference(typeof(UnityMCP.Editor.EditorCommandExecutor).Assembly.Location);

                // Add Newtonsoft.Json so snippets can (de)serialize JSON like the plugin does. The
                // name-based loop below won't catch it (its assembly name is "Newtonsoft.Json"), and
                // referencing the plugin assembly doesn't transitively expose Newtonsoft's types - the
                // snippet needs the defining assembly referenced directly. Pinning it via JsonConvert's
                // assembly guarantees we hand callers the exact Newtonsoft the plugin compiled against.
                AddAssemblyReference(typeof(Newtonsoft.Json.JsonConvert).Assembly.Location);

                // Add netstandard assembly
                var netstandardAssembly = AppDomain.CurrentDomain.GetAssemblies()
                    .FirstOrDefault(a => a.GetName().Name == "netstandard");
                if (netstandardAssembly != null)
                {
                    AddAssemblyReference(netstandardAssembly.Location);
                }

                // Reference every loaded UnityEngine/Unity module + the project's own
                // scripts + VRChat assemblies + the BCL facade assemblies, so snippets can use
                // any engine API (e.g. ImageConversion.EncodeToPNG, JsonUtility, ScreenCapture),
                // call project/editor types (Assembly-CSharp[-Editor], UdonSharp behaviours), and
                // use the full base class library without this list needing manual upkeep.
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (asm.IsDynamic) continue;
                    string loc;
                    try { loc = asm.Location; } catch { continue; }
                    if (string.IsNullOrEmpty(loc)) continue;

                    var name = asm.GetName().Name;
                    bool include =
                        name.StartsWith("UnityEngine") ||  // all engine modules incl. ImageConversion
                        name.StartsWith("Unity.") ||        // packages (TextMeshPro, Burst, ...)
                        name.StartsWith("VRC") ||           // VRCSDK3, VRCSDKBase, VRC.Udon, ...
                        name.StartsWith("UdonSharp") ||
                        name.StartsWith("UnityMCP") ||      // the plugin's own helpers (UdonSharpHelper, ...)
                        name.StartsWith("Basis") ||         // Basis framework (BasisFramework, BasisNetworkCore, ...)
                        name == "Assembly-CSharp" ||        // project runtime scripts
                        name == "Assembly-CSharp-Editor" || // project editor scripts (e.g. SsxLevelImporter)
                        // BCL facades. Because we compile with /nostdlib+, the compiler adds no
                        // implicit references, so types whose interfaces are type-forwarded through a
                        // facade fail with a cryptic CS1070 unless that facade is referenced - e.g.
                        // HashSet<T> (mscorlib) implements ISet<T>, forwarded via System.Collections.
                        // Referencing the System.* facades + mscorlib/netstandard covers the BCL.
                        // Forwarders only redirect (they don't redefine types), so this won't trip the
                        // "predefined type defined multiple times" error that /nostdlib+ guards against.
                        name == "System" ||                 // System.dll (Uri, regex, ...)
                        name.StartsWith("System.") ||        // System.Collections, System.Runtime, System.Core, ...
                        name == "mscorlib" ||
                        name == "netstandard";

                    if (include) AddAssemblyReference(loc);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[UnityMCP] Assembly reference setup issue: {e.Message}");
            }

            return references;
        }

        static Assembly CompileWithCodeDom(string code, List<string> references)
        {
            // Use Mono's built-in compiler
            var options = new System.CodeDom.Compiler.CompilerParameters
            {
                GenerateInMemory = true,
                // Fixes error: The predefined type 'xxx' is defined multiple times. Using definition from 'mscorlib.dll'
                CompilerOptions = "/nostdlib+ /noconfig",
                CoreAssemblyFileName = typeof(object).Assembly.Location
            };
            foreach (var reference in references)
            {
                options.ReferencedAssemblies.Add(reference);
            }

            using (var provider = new Microsoft.CSharp.CSharpCodeProvider())
            {
                var results = provider.CompileAssemblyFromSource(options, code);
                if (results.Errors.HasErrors)
                {
                    foreach (CompilerError error in results.Errors)
                    {
                        Debug.LogError($"Error {error.ErrorNumber}: {error.ErrorText}, Line {error.Line}");
                    }
                    var errors = string.Join("\n", results.Errors.Cast<CompilerError>().Select(e => e.ErrorText));
                    throw new Exception($"Compilation failed:\n{errors}");
                }

                return results.CompiledAssembly;
            }
        }

        // Compiles with the Roslyn csc that ships inside the Editor installation, invoked out of
        // process, then loads the resulting assembly bytes into the Editor domain. Works under
        // any API compatibility level; also lifts the language level from CodeDom's C# 7.0 to
        // whatever the bundled compiler supports.
        static Assembly CompileWithBundledRoslyn(string code, List<string> references)
        {
            ResolveBundledRoslyn();

            var workDir = Path.Combine(Path.GetTempPath(), "UnityMCP", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workDir);
            try
            {
                var sourcePath = Path.Combine(workDir, "EditorCommand.cs");
                var outPath = Path.Combine(workDir, "EditorCommand.dll");
                var rspPath = Path.Combine(workDir, "args.rsp");
                File.WriteAllText(sourcePath, code);

                // Response file to stay clear of the OS command-line length limit (the reference
                // list alone is a few hundred paths). -noconfig can't go in a response file, so it
                // stays on the command line.
                var rsp = new StringBuilder();
                rsp.AppendLine("-nologo");
                rsp.AppendLine("-target:library");
                rsp.AppendLine("-langversion:latest");
                rsp.AppendLine("-nostdlib+"); // same reference discipline as the CodeDom path
                rsp.AppendLine($"-out:\"{outPath}\"");
                foreach (var reference in references)
                {
                    rsp.AppendLine($"-r:\"{reference}\"");
                }
                rsp.AppendLine($"\"{sourcePath}\"");
                File.WriteAllText(rspPath, rsp.ToString());

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = s_bundledDotnet,
                    Arguments = $"exec \"{s_bundledCsc}\" -noconfig @\"{rspPath}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = workDir
                };

                string output;
                int exitCode;
                using (var process = System.Diagnostics.Process.Start(startInfo))
                {
                    var stdout = process.StandardOutput.ReadToEndAsync();
                    var stderr = process.StandardError.ReadToEndAsync();
                    if (!process.WaitForExit(120000))
                    {
                        try { process.Kill(); } catch { }
                        throw new Exception("Compilation timed out after 120s (bundled Roslyn csc)");
                    }
                    output = (stdout.Result + "\n" + stderr.Result).Trim();
                    exitCode = process.ExitCode;
                }

                if (exitCode != 0)
                {
                    // csc emits "path(line,col): error CSxxxx: message" lines; surface those (or
                    // everything, if the failure didn't produce diagnostics in that shape).
                    var errorLines = output.Split('\n')
                        .Select(l => l.Trim())
                        .Where(l => l.Contains("error ")) // ordinal; single-arg overload exists on every API profile
                        .ToList();
                    var errors = errorLines.Count > 0 ? string.Join("\n", errorLines) : output;
                    foreach (var line in errorLines)
                    {
                        Debug.LogError(line);
                    }
                    throw new Exception($"Compilation failed:\n{errors}");
                }

                return Assembly.Load(File.ReadAllBytes(outPath));
            }
            finally
            {
                try { Directory.Delete(workDir, true); } catch { /* temp dir; best effort */ }
            }
        }

        // Locates dotnet + csc.dll inside the running Editor's installation. Layout varies:
        //  - Unity 2022.3 and Unity 6 before 6000.5: Data/DotNetSdkRoslyn/csc.dll, run with
        //    Data/NetCoreRuntime/dotnet.
        //  - Unity 6000.5+: a full .NET SDK at Data/DotNetSdk; csc.dll sits in the versioned
        //    sdk dir (sdk/<version>/Roslyn/bincore/csc.dll), run with the SDK's own dotnet so
        //    the runtime always matches the compiler.
        static void ResolveBundledRoslyn()
        {
            if (s_bundledCsc != null) return;

            var contents = EditorApplication.applicationContentsPath;
            var exe = Application.platform == RuntimePlatform.WindowsEditor ? ".exe" : "";

            var csc = Path.Combine(contents, "DotNetSdkRoslyn", "csc.dll");
            var dotnet = Path.Combine(contents, "NetCoreRuntime", "dotnet" + exe);
            if (File.Exists(csc) && File.Exists(dotnet))
            {
                s_bundledCsc = csc;
                s_bundledDotnet = dotnet;
                return;
            }

            var sdkRoot = Path.Combine(contents, "DotNetSdk");
            dotnet = Path.Combine(sdkRoot, "dotnet" + exe);
            var sdkVersions = Path.Combine(sdkRoot, "sdk");
            if (File.Exists(dotnet) && Directory.Exists(sdkVersions))
            {
                foreach (var versionDir in Directory.GetDirectories(sdkVersions).OrderByDescending(d => d, StringComparer.OrdinalIgnoreCase))
                {
                    csc = Path.Combine(versionDir, "Roslyn", "bincore", "csc.dll");
                    if (File.Exists(csc))
                    {
                        s_bundledCsc = csc;
                        s_bundledDotnet = dotnet;
                        return;
                    }
                }
            }

            throw new Exception(
                "Could not locate the Editor's bundled Roslyn compiler " +
                $"(looked for DotNetSdkRoslyn/csc.dll and DotNetSdk/sdk/*/Roslyn/bincore/csc.dll under {contents})");
        }

        static object InvokeEditorCommand(Assembly assembly)
        {
            var type = assembly.GetType("EditorCommand");
            if (type == null)
            {
                throw new Exception("Compiled code must define a top-level class named 'EditorCommand'");
            }
            var method = type.GetMethod("Execute");
            if (method == null)
            {
                throw new Exception("'EditorCommand' must define a public static method 'Execute'");
            }
            try
            {
                return method.Invoke(null, null);
            }
            catch (TargetInvocationException tie) when (tie.InnerException != null)
            {
                // Reflection wraps whatever EditorCommand.Execute() throws in a
                // TargetInvocationException, whose message ("Exception has been thrown by the
                // target of an invocation") and stack trace are reflection plumbing, not the real
                // failure. Rethrow the inner exception - preserving its original stack - so callers
                // report the actual error (e.g. MissingComponentException) instead of the wrapper.
                ExceptionDispatchInfo.Capture(tie.InnerException).Throw();
                throw; // unreachable: Throw() above always rethrows; satisfies the compiler.
            }
        }
    }
}
