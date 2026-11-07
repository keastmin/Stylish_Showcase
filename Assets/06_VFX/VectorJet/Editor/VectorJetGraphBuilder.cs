using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ProjectR.VectorJet.Editor
{
    /// <summary>Authors the editable, native VFX Graph for Vector Jet. No scene/gameplay mutations.</summary>
    public static class VectorJetGraphBuilder
    {
        public const string GraphPath = "Assets/06_VFX/VectorJet/Graphs/VectorJet.vfx";
        public const string ShaderGraphPath = "Assets/06_VFX/VectorJet/Shaders/JetVolume.shadergraph";
        const BindingFlags Flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        public static void Build(Texture2D mask, Mesh coreMesh, Mesh shockMesh)
        {
            if (!mask || !coreMesh || !shockMesh) throw new ArgumentException("Vector Jet needs its image mask and both Blender meshes.");
            AssetDatabase.ImportAsset(ShaderGraphPath, ImportAssetOptions.ForceSynchronousImport);
            var volumeShader = AssetDatabase.LoadAssetAtPath(ShaderGraphPath, Type("UnityEditor.ShaderGraph.Internal.ShaderGraphVfxAsset"));
            if (!volumeShader) throw new InvalidOperationException("JetVolume Shader Graph must import with URP Support VFX enabled.");
            if (File.Exists(GraphPath)) throw new InvalidOperationException("VectorJet.vfx already exists. Preserve hand edits; explicitly remove only that asset before rebuilding.");
            Directory.CreateDirectory(Path.GetDirectoryName(GraphPath));
            string package = UnityEditor.PackageManager.PackageInfo.FindForAssembly(Type("UnityEditor.VFX.VFXGraph").Assembly).resolvedPath;
            File.Copy(package + "/Editor/Templates/Empty.vfx", GraphPath);
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport);
            object resource = Call(Type("UnityEditor.VFX.VisualEffectResource"), "GetResourceAtPath", GraphPath);
            object graph = Call(Type("UnityEditor.VFX.VisualEffectResourceExtensions"), "GetOrCreateGraph", resource);
            ((Object)graph).name = "VECTOR JET | Mesh combustion, shock cells, axial wisps, ignition";

            var throttle = Parameter(graph, "Throttle", typeof(float), 1f, 0);
            var length = Parameter(graph, "JetLength", typeof(float), 1.7f, 1);
            var width = Parameter(graph, "JetWidth", typeof(float), 1f, 2);
            var coreTint = Parameter(graph, "CoreTint", typeof(Vector3), new Vector3(.35f, 1.2f, 4.4f), 3);
            var shockTint = Parameter(graph, "ShockTint", typeof(Vector3), new Vector3(3.8f, 2.6f, 2f), 4);
            var fringeTint = Parameter(graph, "FringeTint", typeof(Vector3), new Vector3(.18f, .55f, 2.4f), 5);
            var flameMask = Parameter(graph, "FlameMask", typeof(Texture2D), mask, 6);
            var core = Parameter(graph, "CoreMesh", typeof(Mesh), coreMesh, 7);
            var shocks = Parameter(graph, "ShockMesh", typeof(Mesh), shockMesh, 8);

            MeshSystem(graph, "01 | Blue-violet combustion envelope", 100, core, flameMask, coreTint, throttle, length, width, false, volumeShader);
            MeshSystem(graph, "02 | Pale compression diamonds", 800, shocks, flameMask, shockTint, throttle, length, width, true, volumeShader);
            WispSystem(graph, flameMask, fringeTint, throttle, length, width, false);
            WispSystem(graph, flameMask, coreTint, throttle, length, width, true);

            Call(graph, "UpdateSubAssets");
            Call(resource, "WriteAsset");
            AssetDatabase.ImportAsset(GraphPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            Debug.Log("Vector Jet: 4 native GPU systems authored with mesh outputs, animated UVs, exposed thrust controls and an Ignite event.");
        }

        static void MeshSystem(object graph, string title, float x, object mesh, object mask, object tint, object throttle, object length, object width, bool shocks, Object shader)
        {
            var nodes = System(graph, title, x, "VFXComposedParticleOutput", 8u, shader);
            var spawn = nodes[0]; var init = nodes[1]; var update = nodes[2]; var output = nodes[3];
            var burst = DirectBlock(spawn, "VFXSpawnerBurst");
            Value(burst, "Count", 1f); Value(burst, "Delay", 0f);
            Set(init, "lifetime", 1000000f);
            Set(init, "size", 1f);
            Set(init, "position", Vector3.zero);
            Setting(update, "ageParticles", false);
            Setting(update, "reapParticles", false);
            Setting(update, "integration", "None");
            Setting(update, "angularIntegration", "None");
            Link(tint, Slot(output, "_Tint"));
            var opacity = Binary(graph, "Multiply", throttle, shocks ? .12f : .24f, x - 130, 760);
            Link(opacity, Slot(output, "_Opacity"));
            Link(length, Input(Set(output, "scaleZ", 1f), 0));

            // A small native GPU pressure flutter; no script is needed to keep the graph alive.
            var time = Node(graph, "Operator.PeriodicTotalTime", x - 280, 200);
            Value(time, "Period", shocks ? .091f : .073f);
            Value(time, "Range", new Vector2(-Mathf.PI, Mathf.PI));
            var sine = Node(graph, "Operator.Sine", x - 280, 350);
            Link(time, Input(sine, 0));
            var flutter = Binary(graph, "Multiply", sine, .035f, x - 280, 470);
            var bias = Binary(graph, "Add", flutter, .965f, x - 280, 590);
            var pulsedWidth = Binary(graph, "Multiply", width, bias, x - 280, 700);
            Link(pulsedWidth, Input(Set(output, "scaleX", 1f), 0));
            Link(pulsedWidth, Input(Set(output, "scaleY", 1f), 0));

            Link(mask, Slot(output, "_FlameMask"));
            Link(mesh, Slot(output, "mesh"));
            Value(output, "_UVScale", new Vector2(shocks ? .18f : .26f, .84f));
            Value(output, "_UVBias", new Vector2(shocks ? .41f : .37f, .08f));
            Value(output, "_ShockMode", shocks ? 1f : 0f);
            // Circumferential drift preserves the mask's longitudinal tail fade.
            var roll = Node(graph, "Operator.PeriodicTotalTime", x - 280, 1000);
            Value(roll, "Period", shocks ? .37f : .24f);
            Value(roll, "Range", shocks ? new Vector2(.39f, .43f) : new Vector2(.35f, .39f));
            var uvChildren = Children(Slot(output, "_UVBias"));
            Link(roll, uvChildren[0]);
        }

        static void WispSystem(object graph, object mask, object tint, object throttle, object length, object width, bool ignition)
        {
            float x = ignition ? 2200 : 1500;
            var nodes = System(graph, ignition ? "04 | Ignite: short pressure flash" : "03 | Advected axial flame filaments", x, "VFXPlanarPrimitiveOutput", ignition ? 128u : 512u);
            var spawn = nodes[0]; var init = nodes[1]; var update = nodes[2]; var output = nodes[3];
            if (ignition)
            {
                var evt = Node(graph, "VFXBasicEvent", x, -180);
                Setting(evt, "eventName", "Ignite");
                Call(evt, "LinkTo", spawn);
                var burst = DirectBlock(spawn, "VFXSpawnerBurst");
                Value(burst, "Count", 18f); Value(burst, "Delay", 0f);
            }
            else
            {
                var rate = DirectBlock(spawn, "VFXSpawnerConstantRate");
                Link(Binary(graph, "Multiply", throttle, 135f, x - 220, -30), Input(rate, 0));
            }
            Set(init, "lifetime", ignition ? .08f : .12f, ignition ? .19f : .27f);
            Set(init, "position", new Vector3(-.045f, -.045f, .025f), new Vector3(.045f, .045f, .10f));
            var velocity = Set(init, "velocity", new Vector3(-.16f, -.16f, ignition ? 2.8f : 2.9f), new Vector3(.16f, .16f, ignition ? 5.7f : 5.0f));
            var stretchVelocity = Set(init, "velocity", Vector3.one);
            Setting(stretchVelocity, "Composition", "Multiply");
            // Multiplying the entire velocity by a scalar is allowed: scalar expands to Vector3.
            Link(length, Input(stretchVelocity, 0));
            Set(init, "size", ignition ? .09f : .07f, ignition ? .19f : .14f);
            Set(init, "scaleX", ignition ? .65f : .42f);
            Set(init, "scaleY", ignition ? 2.4f : 3.6f);
            Set(init, "alpha", ignition ? .23f : .13f, ignition ? .48f : .32f);
            Link(tint, Input(Set(init, "color", Vector3.one), 0));
            var turbulence = Block(update, "Turbulence");
            Setting(turbulence, "NoiseType", "Perlin");
            Value(turbulence, "Intensity", ignition ? .28f : .50f);
            Value(turbulence, "frequency", 4.5f);
            Value(turbulence, "octaves", 2);
            var drag = Block(update, "Drag"); Value(drag, "dragCoefficient", .35f);
            Curve(output, "alpha", new AnimationCurve(new Keyframe(0, 0), new Keyframe(.10f, 1), new Keyframe(.37f, .72f), new Keyframe(1, 0)), "Multiply");
            Curve(output, "size", new AnimationCurve(new Keyframe(0, .65f), new Keyframe(.15f, 1), new Keyframe(1, .23f)), "Multiply");
            var size = Set(output, "size", 1f); Setting(size, "Composition", "Multiply"); Link(width, Input(size, 0));
            var opacity = Set(output, "alpha", 1f); Setting(opacity, "Composition", "Multiply"); Link(throttle, Input(opacity, 0));
            var orient = Block(output, "Orient"); Setting(orient, "mode", "AlongVelocity");
            Output(output, mask);
            Setting(output, "useSoftParticle", true);
            Value(output, "softParticleFadeDistance", .035f);
        }

        static object[] System(object graph, string title, float x, string outputType, uint capacity, Object shader = null)
        {
            var spawn = Node(graph, "VFXBasicSpawner", x, 0);
            var init = Node(graph, "VFXBasicInitialize", x, 220);
            var update = Node(graph, "VFXBasicUpdate", x, 830);
            var output = Node(graph, outputType, x, 1160);
            if (shader != null)
            {
                Setting(output, "m_Topology", Activator.CreateInstance(Type("UnityEditor.VFX.ParticleTopologyMesh")));
                Setting(output, "shaderGraph", shader);
            }
            Property(spawn, "label", title);
            Call(spawn, "LinkTo", init); Call(init, "LinkTo", update); Call(update, "LinkTo", output);
            Setting(init, "capacity", capacity);
            var data = Call(init, "GetData");
            Property(data, "title", title);
            var space = FindProperty(data.GetType(), "space");
            space.SetValue(data, Enum.Parse(space.PropertyType, "Local"));
            Setting(data, "boundsMode", "Manual");
            var bounds = Children(Slot(init, "bounds"));
            SlotValue(bounds[0], new Vector3(0, 0, 3)); SlotValue(bounds[1], new Vector3(5, 5, 10));
            return new[] { spawn, init, update, output };
        }

        static void Output(object output, object mask)
        {
            Setting(output, "blendMode", "Additive");
            Setting(output, "useBaseColorMap", "ColorAndAlpha");
            Setting(output, "zWriteMode", "Off");
            Setting(output, "castShadows", false);
            Link(mask, Slot(output, "mainTexture"));
        }

        static object Binary(object graph, string type, object a, object b, float x, float y)
        {
            var op = Node(graph, "Operator." + type, x, y);
            Call(op, "SetOperandType", 0, typeof(float));
            Call(op, "SetOperandType", 1, typeof(float));
            if (a is float) SlotValue(Input(op, 0), a); else Link(a, Input(op, 0));
            if (b is float) SlotValue(Input(op, 1), b); else Link(b, Input(op, 1));
            return op;
        }

        static Type Type(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).First(t => t != null);
        static PropertyInfo FindProperty(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType) { var p = t.GetProperty(name, Flags | BindingFlags.DeclaredOnly); if (p != null) return p; }
            throw new MissingMemberException(type.FullName, name);
        }
        static FieldInfo FindField(Type type, string name)
        {
            for (var t = type; t != null; t = t.BaseType) { var f = t.GetField(name, Flags | BindingFlags.DeclaredOnly); if (f != null) return f; }
            return null;
        }
        static object Get(object o, string name)
        {
            var f = FindField(o.GetType(), name);
            return f != null ? f.GetValue(o) : FindProperty(o.GetType(), name).GetValue(o);
        }
        static void Property(object o, string name, object value)
        {
            var f = FindField(o.GetType(), name);
            if (f != null) f.SetValue(o, value); else FindProperty(o.GetType(), name).SetValue(o, value);
        }
        static object Call(object o, string name, params object[] args)
        {
            Type type = o as Type ?? o.GetType();
            foreach (var m in type.GetMethods(Flags).Where(m => m.Name == name && !m.IsGenericMethodDefinition))
            {
                var ps = m.GetParameters();
                if (ps.Length < args.Length || ps.Skip(args.Length).Any(p => !p.IsOptional)) continue;
                if (args.Where((a, i) => a != null && !ps[i].ParameterType.IsInstanceOfType(a)).Any()) continue;
                var all = new object[ps.Length]; Array.Copy(args, all, args.Length);
                for (int i = args.Length; i < ps.Length; i++) all[i] = ps[i].DefaultValue;
                try { return m.Invoke(o is Type ? null : o, all); }
                catch (TargetInvocationException e) { throw new InvalidOperationException(type.Name + "." + name + ": " + e.InnerException?.Message, e.InnerException); }
            }
            throw new MissingMethodException(type.FullName, name + "(" + string.Join(",", args.Select(a => a?.GetType().Name)) + ")");
        }
        static object Make(string type) => ScriptableObject.CreateInstance(Type("UnityEditor.VFX." + type));
        static object Node(object graph, string type, float x, float y)
        {
            var n = Make(type); Call(graph, "AddChild", n); Property(n, "position", new Vector2(x, y)); return n;
        }
        static object Block(object context, string type) => DirectBlock(context, "Block." + type);
        static object DirectBlock(object context, string type) { var b = Make(type); Call(context, "AddChild", b); return b; }
        static object[] Children(object o) => ((IEnumerable)Get(o, "children")).Cast<object>().ToArray();
        static object Slot(object o, string name) => ((IEnumerable)Get(o, "inputSlots")).Cast<object>().First(s => (string)Get(s, "name") == name);
        static object Input(object o, int i) => Call(o, "GetInputSlot", i);
        static void Value(object o, string name, object value) => SlotValue(Slot(o, name), value);
        static void SlotValue(object slot, object value)
        {
            // VFX position/vector wrappers carry space information in addition to their Vector3 value.
            object current = Get(slot, "value");
            if (value is Vector3 && current != null && current.GetType() != typeof(Vector3))
            {
                var field = current.GetType().GetFields(Flags).FirstOrDefault(f => f.FieldType == typeof(Vector3));
                if (field != null) { field.SetValue(current, value); value = current; }
            }
            Property(slot, "value", value);
        }
        static void Link(object source, object slot)
        {
            if (!(bool)Call(slot, "Link", Call(source, "GetOutputSlot", 0)))
                throw new InvalidOperationException("VFX slot link rejected: " + source.GetType().Name + " -> " + Get(slot, "name"));
        }
        static void Setting(object o, string name, object value)
        {
            var setting = Call(o, "GetSetting", name);
            var field = (FieldInfo)Get(setting, "field");
            if (field == null) throw new InvalidOperationException(o.GetType().Name + " has no setting " + name);
            Type t = field.FieldType;
            if (t.IsEnum && value is string) value = Enum.Parse(t, (string)value);
            else if (value != null && !t.IsInstanceOfType(value)) value = Convert.ChangeType(value, t);
            Call(o, "SetSettingValue", name, value);
        }
        static object Parameter(object graph, string name, Type type, object value, int order)
        {
            var p = Make("VFXParameter"); Call(p, "Init", type); Call(graph, "AddChild", p);
            Setting(p, "m_ExposedName", name); Setting(p, "m_Exposed", true);
            Property(p, "value", value); Property(p, "position", new Vector2(-720, order * 145));
            return p;
        }
        static object Set(object context, string attribute, object min, object max = null)
        {
            var b = Block(context, "SetAttribute"); Setting(b, "attribute", attribute);
            if (max != null) Setting(b, "Random", min is Vector3 ? "PerComponent" : "Uniform");
            SlotValue(Input(b, 0), min); if (max != null) SlotValue(Input(b, 1), max); return b;
        }
        static void Curve(object context, string attribute, AnimationCurve curve, string composition)
        {
            var b = Block(context, "AttributeFromCurve"); Setting(b, "attribute", attribute);
            Setting(b, "Mode", "Uniform"); Setting(b, "Composition", composition);
            SlotValue(Input(b, 0), curve);
        }
    }
}
