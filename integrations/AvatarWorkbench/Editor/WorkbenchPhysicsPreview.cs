using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace AvatarWorkbench
{
    // The SDK's own PhysBone jobs, restricted to the manager and avatar in our
    // temporary scene. This adapter never advances the global dynamics scheduler.
    // The reflected interface is validated before any registration or simulation.
    internal sealed class WorkbenchPhysicsPreview : IDisposable
    {
        const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        readonly GameObject root;
        readonly Component manager;
        readonly List<Component> bodies = new List<Component>();
        readonly List<Component> colliders = new List<Component>();
        readonly HashSet<Component> registeredBodies = new HashSet<Component>();
        readonly HashSet<Component> registeredColliders = new HashSet<Component>();
        readonly List<Component> removed = new List<Component>();
        FieldInfo instance, isSDK, hasInit, fixedTime, realTime, fullFrameTime, timingOverride, globalPendingShapes;
        FieldInfo pendingBodies, removedBodies, pendingColliders, removedColliders, pendingRoots;
        FieldInfo bodyRemovalComponent, colliderRemovalComponent, chainIndex;
        FieldInfo bodyRoot, bodyIgnores, bodyWorldImmobile, bodyColliders, bodyDefinition, colliderRoot;
        PropertyInfo definitionTransform;
        MethodInfo bodyInit, bodyReestablish, colliderEnable, removeBody, removeCollider;
        MethodInfo hasBody, getChains, getChainComponent, getColliders, schedule, complete, clearTiming;
        object defaultJob;
        float elapsed;
        int chainCount;
        bool disposed, compatible;

        public bool Running { get; private set; }
        public bool Simulated { get; private set; }
        public string Error { get; private set; } = "";

        // The caller additionally verifies that manager belongs to its ownedManagers
        // list. A name is never used as evidence of manager ownership.
        public WorkbenchPhysicsPreview(GameObject root, Component manager)
        {
            this.root = root;
            this.manager = manager;
            try
            {
                CacheInterface();
                compatible = true;
                ValidateOwnership();
                if (bodies.Count == 0) throw new InvalidOperationException("这个候选没有可预览的 PhysBone。");
            }
            catch (Exception e) { StopWithError(e); }
        }

        static FieldInfo Field(Type type, string name, BindingFlags flags = InstanceFlags)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var field = current.GetField(name, flags | BindingFlags.DeclaredOnly);
                if (field != null) return field;
            }
            throw new InvalidOperationException("当前 SDK 的 PhysBone 接口不兼容（" + name + "）。");
        }

        static MethodInfo Method(Type type, string name, params Type[] arguments)
        {
            var method = type.GetMethod(name, InstanceFlags, null, arguments, null);
            if (method == null) throw new InvalidOperationException("当前 SDK 的 PhysBone 接口不兼容（" + name + "）。");
            return method;
        }

        void CacheInterface()
        {
            if (!root || !manager) throw new InvalidOperationException("临时角色或物理管理器已经关闭。");
            Type managerType = manager.GetType();
            if (managerType.FullName != "VRC.Dynamics.PhysBoneManager")
                throw new InvalidOperationException("没有可用的原生 PhysBone 管理器。");
            var bodyType = WorkbenchGesturePreview.FindType("VRC.Dynamics.VRCPhysBoneBase");
            var colliderType = WorkbenchGesturePreview.FindType("VRC.Dynamics.VRCPhysBoneColliderBase");
            var definitionType = WorkbenchGesturePreview.FindType("VRC.Dynamics.PhysBoneRootDefinition");
            if (bodyType == null || colliderType == null || definitionType == null)
                throw new InvalidOperationException("当前 SDK 缺少原生 PhysBone 接口。");

            instance = Field(managerType, "Inst", StaticFlags);
            timingOverride = Field(managerType, "DisableTiming", StaticFlags);
            globalPendingShapes = Field(colliderType, "CollidersPendingShapeUpdate", StaticFlags);
            isSDK = Field(managerType, "IsSDK"); hasInit = Field(managerType, "hasInit");
            fixedTime = Field(managerType, "fixedTimeElapsed"); realTime = Field(managerType, "realTimeElapsed");
            fullFrameTime = Field(managerType, "fullFrameTimeElapsed");
            if (fixedTime.FieldType != typeof(float) || realTime.FieldType != typeof(float) || fullFrameTime.FieldType != typeof(float))
                throw new InvalidOperationException("当前 SDK 的物理计时接口不兼容。");
            pendingBodies = Field(managerType, "compsToAdd"); removedBodies = Field(managerType, "compsToRemove");
            pendingColliders = Field(managerType, "collidersToAdd"); removedColliders = Field(managerType, "collidersToRemove");
            pendingRoots = Field(managerType, "rootsToUpdate");
            bodyRemovalComponent = Field(removedBodies.FieldType.GetGenericArguments()[0], "comp");
            colliderRemovalComponent = Field(removedColliders.FieldType.GetGenericArguments()[0], "comp");

            bodyRoot = Field(bodyType, "rootTransform"); bodyIgnores = Field(bodyType, "ignoreTransforms");
            bodyWorldImmobile = Field(bodyType, "worldImmobileTransform"); bodyColliders = Field(bodyType, "colliders");
            bodyDefinition = Field(bodyType, "root"); colliderRoot = Field(colliderType, "rootTransform");
            definitionTransform = definitionType.GetProperty("Transform", InstanceFlags);
            if (definitionTransform == null || definitionTransform.PropertyType != typeof(Transform))
                throw new InvalidOperationException("当前 SDK 的物理根接口不兼容。");
            bodyInit = Method(bodyType, "Init"); bodyReestablish = Method(bodyType, "ReEstablishPhysBoneRoot");
            colliderEnable = Method(colliderType, "OnEnable");
            removeBody = Method(managerType, "RemovePhysBone", bodyType);
            removeCollider = Method(managerType, "RemoveCollider", colliderType);
            hasBody = Method(managerType, "HasPhysBone", bodyType);
            getChains = Method(managerType, "GetChains"); getChainComponent = Method(managerType, "GetChainComponent", typeof(int));
            getColliders = Method(managerType, "GetColliderComponents"); clearTiming = Method(managerType, "ClearRootTiming");
            chainIndex = Field(getChains.ReturnType.GetGenericArguments()[0], "Item1");

            foreach (var method in managerType.GetMethods(InstanceFlags))
            {
                var args = method.GetParameters();
                if (method.Name != "ScheduleExecutionJob" || args.Length != 1 ||
                    args[0].ParameterType.FullName != "Unity.Jobs.JobHandle" || method.ReturnType != args[0].ParameterType) continue;
                schedule = method; complete = Method(method.ReturnType, "Complete");
                defaultJob = Activator.CreateInstance(method.ReturnType); break;
            }
            if (schedule == null) throw new InvalidOperationException("当前 SDK 没有兼容的原生物理求值接口。");

            // This is the only hierarchy scan. Later steps inspect these cached components
            // and this owned manager's bounded lists, never the project or other scenes.
            foreach (var component in root.GetComponentsInChildren<Component>(true))
            {
                if (!component) continue;
                if (bodyType.IsInstanceOfType(component)) bodies.Add(component);
                else if (colliderType.IsInstanceOfType(component)) colliders.Add(component);
            }
        }

        void RequireOwned(object reference)
        {
            if (reference == null) return;
            if (reference is Object asset && !asset) return;
            Transform transform = reference as Transform;
            if (reference is Component component) transform = component.transform;
            else if (reference is GameObject gameObject) transform = gameObject.transform;
            if (!transform || !root || (transform != root.transform && !transform.IsChildOf(root.transform)))
                throw new InvalidOperationException("检测到临时角色之外的物理引用，已停止以保护正式角色。");
        }

        void CheckReferences(FieldInfo field, object owner)
        {
            var value = field.GetValue(owner);
            if (value is IEnumerable list)
            {
                foreach (var item in list) RequireOwned(item);
            }
            else RequireOwned(value);
        }

        void CheckDefinition(object definition)
        {
            if (definition == null) return;
            var transform = definitionTransform.GetValue(definition) as Transform;
            if (!transform) throw new InvalidOperationException("物理根已经关闭，已停止预览。");
            RequireOwned(transform);
        }

        void CheckPending(FieldInfo field, FieldInfo nestedComponent = null)
        {
            if (!(field.GetValue(manager) is IEnumerable list))
                throw new InvalidOperationException("当前 SDK 的物理队列接口不兼容。");
            foreach (var item in list) RequireOwned(nestedComponent == null ? item : nestedComponent.GetValue(item));
        }

        int ValidateChains()
        {
            var iterator = getChains.Invoke(manager, null) as IEnumerator;
            if (iterator == null) throw new InvalidOperationException("当前 SDK 的物理链接口不兼容。");
            int count = 0;
            try
            {
                while (iterator.MoveNext())
                {
                    int index = (int)chainIndex.GetValue(iterator.Current);
                    var component = getChainComponent.Invoke(manager, new object[] { index }) as Component;
                    if (!component) throw new InvalidOperationException("物理管理器含有无法确认归属的物理链，已停止预览。");
                    RequireOwned(component); count++;
                }
            }
            finally { (iterator as IDisposable)?.Dispose(); }
            return count;
        }

        void ValidateOwnership()
        {
            if (!root || !manager || !root.scene.IsValid() || !EditorSceneManager.IsPreviewScene(root.scene) ||
                manager.gameObject.scene != root.scene)
                throw new InvalidOperationException("物理预览必须位于独立的临时场景。");
            if (!ReferenceEquals(instance.GetValue(null), manager))
                throw new InvalidOperationException("其他 Unity 预览正在占用 PhysBone 管理器；本次物理预览已暂停。");
            if (!(bool)isSDK.GetValue(manager) || !(bool)hasInit.GetValue(manager))
                throw new InvalidOperationException("临时 PhysBone 管理器尚未初始化。");
            if ((bool)timingOverride.GetValue(null))
                throw new InvalidOperationException("Unity 的其他物理调试计时正在运行，本次预览已暂停。");
            // SDK 3.10.5 ScheduleExecutionJob indirectly drains this *global* queue.
            // UpdateShape can then call the global scheduler. Leave the queue intact
            // and refuse the step whenever anything is pending, including our clone.
            if (!(globalPendingShapes.GetValue(null) is IEnumerable shapeQueue))
                throw new InvalidOperationException("当前 SDK 的碰撞体形状队列接口不兼容。");
            foreach (var unused in shapeQueue)
                throw new InvalidOperationException("Unity 有待处理的碰撞体形状编辑；请完成该编辑后重新开启物理预览。");

            foreach (var body in bodies)
            {
                if (!body) continue;
                RequireOwned(body); CheckReferences(bodyRoot, body); CheckReferences(bodyIgnores, body);
                CheckReferences(bodyWorldImmobile, body); CheckReferences(bodyColliders, body);
                CheckDefinition(bodyDefinition.GetValue(body));
            }
            foreach (var collider in colliders)
                if (collider) { RequireOwned(collider); CheckReferences(colliderRoot, collider); }
            ValidateChains();
            if (!(getColliders.Invoke(manager, null) is IEnumerable colliderList))
                throw new InvalidOperationException("当前 SDK 的碰撞体接口不兼容。");
            foreach (var collider in colliderList) RequireOwned(collider);
            CheckPending(pendingBodies); CheckPending(removedBodies, bodyRemovalComponent);
            CheckPending(pendingColliders); CheckPending(removedColliders, colliderRemovalComponent);
            if (!(pendingRoots.GetValue(manager) is IEnumerable roots))
                throw new InvalidOperationException("当前 SDK 的物理根队列接口不兼容。");
            foreach (var definition in roots) CheckDefinition(definition);
        }

        static bool Active(Component component) => component && component.gameObject.activeInHierarchy &&
            (!(component is Behaviour behaviour) || behaviour.enabled);

        static bool ContainsComponent(object list, Component component)
        {
            if (!(list is IEnumerable items)) return false;
            foreach (var item in items) if (ReferenceEquals(item, component)) return true;
            return false;
        }

        void UpdateRegistration()
        {
            // Visibility changes come from the actual native animation graph. Only our
            // own components are registered or removed; no wardrobe is emulated here.
            removed.Clear();
            foreach (var body in registeredBodies) if (!Active(body)) removed.Add(body);
            foreach (var body in removed)
            {
                if (body) removeBody.Invoke(manager, new object[] { body });
                registeredBodies.Remove(body);
            }
            removed.Clear();
            foreach (var collider in registeredColliders) if (!Active(collider)) removed.Add(collider);
            foreach (var collider in removed)
            {
                if (collider) removeCollider.Invoke(manager, new object[] { collider });
                registeredColliders.Remove(collider);
            }
            foreach (var collider in colliders)
            {
                if (!Active(collider) || registeredColliders.Contains(collider)) continue;
                // Native OnEnable may already have queued this collider. AddCollider
                // itself does not deduplicate its queue in SDK 3.10.5.
                if (!ContainsComponent(getColliders.Invoke(manager, null), collider) &&
                    !ContainsComponent(pendingColliders.GetValue(manager), collider)) colliderEnable.Invoke(collider, null);
                registeredColliders.Add(collider);
            }
            foreach (var body in bodies)
            {
                if (!Active(body) || registeredBodies.Contains(body)) continue;
                if (!(bool)hasBody.Invoke(manager, new object[] { body })) bodyInit.Invoke(body, null);
                bodyReestablish.Invoke(body, null); registeredBodies.Add(body);
            }
        }

        public void SetRunning(bool running)
        {
            Running = false;
            if (!running || disposed || !compatible || !string.IsNullOrEmpty(Error)) return;
            try { ValidateOwnership(); Running = true; }
            catch (Exception e) { StopWithError(e); }
        }

        public bool Step(float delta)
        {
            if (!Running || disposed || !compatible) return false;
            if (delta <= 0) return false;
            try
            {
                if (float.IsNaN(delta) || float.IsInfinity(delta)) throw new InvalidOperationException("物理预览收到无效的时间步长。");
                delta = Mathf.Min(delta, .05f);
                ValidateOwnership(); UpdateRegistration(); ValidateOwnership();
                fixedTime.SetValue(manager, delta); realTime.SetValue(manager, delta); fullFrameTime.SetValue(manager, delta);
                // Complete this returned handle directly. PhysBoneManager.CompleteJob
                // delegates to the global scheduler and is deliberately never called.
                var job = schedule.Invoke(manager, new[] { defaultJob });
                complete.Invoke(job, null); clearTiming.Invoke(manager, null);
                chainCount = ValidateChains();
                if (chainCount == 0) return false;
                elapsed += delta; Simulated = true; return true;
            }
            catch (Exception e) { StopWithError(e); return false; }
        }

        void StopWithError(Exception exception)
        {
            Running = false;
            while (exception is TargetInvocationException && exception.InnerException != null) exception = exception.InnerException;
            Error = "Unity 物理预览已暂停：" + exception.Message;
        }

        public JObject State()
        {
            var state = new JObject
            {
                ["physics_simulated"] = Simulated, ["physics_running"] = Running,
                ["physics_sample_time"] = elapsed, ["physbone_chains"] = chainCount,
                ["physics_scope"] = "temporary_preview_only", ["physics_engine"] = "vrchat_sdk_native_physbone"
            };
            if (!string.IsNullOrEmpty(Error)) state["physics_error"] = Error;
            return state;
        }

        public void Dispose()
        {
            Running = false; disposed = true;
            // Every step completed its own job synchronously. The Gesture Manager
            // adapter owns and destroys the manager and temporary scene afterwards.
            registeredBodies.Clear(); registeredColliders.Clear(); removed.Clear();
            bodies.Clear(); colliders.Clear();
        }
    }
}
