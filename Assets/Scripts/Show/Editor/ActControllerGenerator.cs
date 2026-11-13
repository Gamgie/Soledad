using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace Soledad
{
    /// <summary>
    /// Writes Assets/Scripts/Show/Generated/ActNController.cs for each act folder Assets/Acts/ActN_Name/ from the
    /// [Animate] members of its scripts: one flat public field per animated field (named Type_field, or the fixed
    /// name given to the attribute, same type, same default, same Range and Tooltip), one forwarding method per
    /// animated method, and plain assignment code to push the values into the act (see ActController).
    ///
    /// Runs after every compile and from Soledad > Generate Act Controllers. A file is only rewritten when its
    /// content changes, so the compile it triggers ends the loop. Once the code is stable it also adds the missing
    /// controllers to Show (Soledad_Main must be open) and copies the values of new controller fields from the act
    /// scene when it is open, so the controller starts with the scene's values rather than the script defaults.
    ///
    /// If an act field is renamed or removed, the generated file stops compiling. The generator then writes a stub
    /// that compiles whatever the act scripts are (same fields, enums as int, no binding), and the next compile
    /// writes the full controller again.
    /// </summary>
    [InitializeOnLoad]
    static class ActControllerGenerator
    {
        const string ActsFolder = "Assets/Acts";
        const string OutputFolder = "Assets/Scripts/Show/Generated";
        const string PendingPullKey = "Soledad.ActControllerGenerator.PendingPull";
        static readonly Regex ActFolderPattern = new Regex(@"^Act(\d+)_");
        static readonly Regex IdentifierPattern = new Regex(@"^[A-Za-z_][A-Za-z0-9_]*$");
        static readonly Regex ActNamePattern = new Regex("ActName => \"([^\"]*)\"");

        class Member
        {
            public Type type;
            public FieldInfo field;
            public MethodInfo method;
            public string name;
        }

        class ActModel
        {
            public int number;
            public string folder;
            public string className;
            public readonly List<Member> members = new List<Member>();
            public string Path => $"{OutputFolder}/{className}.cs";
        }

        static bool _scheduled;

        static ActControllerGenerator()
        {
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
            Schedule();
        }

        [MenuItem("Soledad/Generate Act Controllers")]
        static void GenerateMenu() => Run(true);

        [MenuItem("Soledad/Pull Act Controller Values From Open Acts")]
        static void PullMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            var pending = new HashSet<string>();
            foreach (ActModel act in CollectActs(false))
                foreach (Member m in act.members)
                    if (m.field != null) pending.Add(act.className + "." + m.name);
            int pulled = Pull(CollectActs(false), pending);
            Debug.Log($"Act controllers: {pulled} values copied from the open act scenes.");
        }

        static void Schedule()
        {
            if (_scheduled) return;
            _scheduled = true;
            EditorApplication.delayCall += () =>
            {
                _scheduled = false;
                Run(false);
            };
        }

        static void Run(bool verbose)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                Schedule();
                return;
            }

            List<ActModel> acts = CollectActs(true);
            var pending = new HashSet<string>(ReadPending());
            var written = new List<string>();

            foreach (ActModel act in acts)
            {
                string code = WriteController(act);
                string existing = File.Exists(act.Path) ? File.ReadAllText(act.Path).Replace("\r\n", "\n") : null;
                if (existing == code) continue;

                // Fields that do not exist yet on the compiled controller get the act scene's value once compiled
                Type compiled = ControllerType(act.className);
                foreach (Member m in act.members)
                    if (m.field != null && (compiled == null || compiled.GetField(m.name) == null))
                        pending.Add(act.className + "." + m.name);

                Directory.CreateDirectory(OutputFolder);
                File.WriteAllText(act.Path, code, new UTF8Encoding(false));
                written.Add($"{act.className} ({act.members.Count} parameters)");
            }

            WritePending(pending);

            if (written.Count > 0)
            {
                Debug.Log("Act controllers written: " + string.Join(", ", written) + ". Compiling.");
                AssetDatabase.Refresh();
                return;
            }

            AddMissingControllers(acts);
            if (pending.Count > 0)
            {
                int pulled = Pull(acts, pending);
                if (pulled > 0) Debug.Log($"Act controllers: {pulled} new values copied from the open act scenes.");
            }
            WritePending(pending);
            if (verbose) Debug.Log($"Act controllers up to date ({acts.Count} acts).");
        }

        // ---------------------------------------------------------------- collect

        static List<ActModel> CollectActs(bool warn)
        {
            var acts = new SortedDictionary<int, ActModel>();
            foreach (string dir in AssetDatabase.GetSubFolders(ActsFolder))
            {
                string folder = System.IO.Path.GetFileName(dir);
                Match match = ActFolderPattern.Match(folder);
                if (!match.Success) continue;
                int number = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
                if (acts.ContainsKey(number))
                {
                    if (warn) Debug.LogError($"Act controllers: two folders for act {number} ({acts[number].folder}, {folder}).");
                    continue;
                }
                acts[number] = new ActModel { number = number, folder = folder, className = $"Act{number}Controller" };
            }

            var processed = new HashSet<Type>();
            var scripts = AssetDatabase.FindAssets("t:MonoScript", new[] { ActsFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(p => p, StringComparer.Ordinal);

            foreach (string path in scripts)
            {
                string folder = path.Substring(ActsFolder.Length + 1).Split('/')[0];
                ActModel act = acts.Values.FirstOrDefault(a => a.folder == folder);
                Type type = AssetDatabase.LoadAssetAtPath<MonoScript>(path)?.GetClass();
                if (act == null || type == null || type.IsAbstract || !typeof(Component).IsAssignableFrom(type)) continue;
                processed.Add(type);
                CollectMembers(act, type, warn);
            }

            if (warn)
            {
                foreach (FieldInfo f in TypeCache.GetFieldsWithAttribute<AnimateAttribute>())
                    WarnIfOutside(f, f.DeclaringType, processed);
                foreach (MethodInfo m in TypeCache.GetMethodsWithAttribute<AnimateAttribute>())
                    WarnIfOutside(m, m.DeclaringType, processed);
            }

            // Acts without [Animate] members only keep a controller if one was already generated (Show uses it)
            return acts.Values.Where(a => a.members.Count > 0 || File.Exists(a.Path)).ToList();
        }

        static void WarnIfOutside(MemberInfo member, Type declaring, HashSet<Type> processed)
        {
            if (processed.Any(t => declaring.IsAssignableFrom(t))) return;
            Debug.LogWarning($"Act controllers: [Animate] on {declaring.Name}.{member.Name} is ignored, the script is not a component under {ActsFolder}/ActN_Name/.");
        }

        static void CollectMembers(ActModel act, Type type, bool warn)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

            foreach (FieldInfo f in type.GetFields(flags).OrderBy(f => f.MetadataToken))
            {
                var attr = f.GetCustomAttribute<AnimateAttribute>(true);
                if (attr == null) continue;
                string where = $"{type.Name}.{f.Name}";
                if (!f.IsPublic || f.IsInitOnly)
                {
                    if (warn) Debug.LogWarning($"Act controllers: [Animate] {where} must be a public, writable field.");
                    continue;
                }
                if (!IsSupported(f.FieldType))
                {
                    if (warn) Debug.LogWarning($"Act controllers: [Animate] {where} has an unsupported type ({f.FieldType.Name}). Use float, int, bool, an enum, Color or Vector3.");
                    continue;
                }
                AddMember(act, new Member { type = type, field = f, name = attr.Name ?? $"{type.Name}_{f.Name}" }, where, warn);
            }

            foreach (MethodInfo m in type.GetMethods(flags).OrderBy(m => m.MetadataToken))
            {
                var attr = m.GetCustomAttribute<AnimateAttribute>(true);
                if (attr == null) continue;
                string where = $"{type.Name}.{m.Name}()";
                if (!m.IsPublic || m.IsGenericMethod || m.GetParameters().Length > 0)
                {
                    if (warn) Debug.LogWarning($"Act controllers: [Animate] {where} must be a public method without parameters.");
                    continue;
                }
                AddMember(act, new Member { type = type, method = m, name = attr.Name ?? $"{type.Name}_{m.Name}" }, where, warn);
            }
        }

        static void AddMember(ActModel act, Member member, string where, bool warn)
        {
            if (!IdentifierPattern.IsMatch(member.name))
            {
                if (warn) Debug.LogError($"Act controllers: [Animate] {where}: \"{member.name}\" is not a valid name.");
                return;
            }
            Member clash = act.members.FirstOrDefault(m => m.name == member.name);
            if (clash != null)
            {
                if (warn) Debug.LogError($"Act controllers: [Animate] {where} and {clash.type.Name}.{(clash.field != null ? clash.field.Name : clash.method.Name)} both map to {act.className}.{member.name}. Give one a fixed name: [Animate(\"otherName\")].");
                return;
            }
            act.members.Add(member);
        }

        static bool IsSupported(Type t) =>
            t == typeof(float) || t == typeof(int) || t == typeof(bool) || t.IsEnum || t == typeof(Color) || t == typeof(Vector3);

        // ---------------------------------------------------------------- write

        static string WriteController(ActModel act)
        {
            var sb = new StringBuilder();
            List<Type> types = act.members.Select(m => m.type).Distinct().ToList();
            string Target(Type t) => "_target" + types.IndexOf(t);

            sb.Append("// <auto-generated>\n");
            sb.Append($"// Written by Soledad > Generate Act Controllers from the [Animate] members of {ActsFolder}/{act.folder}.\n");
            sb.Append("// Do not edit: it is rewritten after every compile.\n");
            sb.Append("// </auto-generated>\n");
            sb.Append("using UnityEngine;\n\n");
            sb.Append("namespace Soledad\n{\n");
            sb.Append($"    /// <summary>Stable show parameters of {act.folder}. See ActController.</summary>\n");
            sb.Append("    [DefaultExecutionOrder(-100)]\n");
            sb.Append($"    public class {act.className} : ActController\n    {{\n");

            foreach (Type type in types)
            {
                bool header = false;
                foreach (Member m in act.members)
                {
                    if (m.type != type || m.field == null) continue;
                    if (!header)
                    {
                        sb.Append($"        [Header(\"{type.Name}\")]\n");
                        header = true;
                    }
                    var tooltip = m.field.GetCustomAttribute<TooltipAttribute>(true);
                    if (tooltip != null) sb.Append($"        [Tooltip({Quote(tooltip.tooltip)})]\n");
                    var range = m.field.GetCustomAttribute<RangeAttribute>(true);
                    string rangeText = range != null ? $"[Range({Float(range.min)}, {Float(range.max)})] " : "";
                    sb.Append($"        {rangeText}public {TypeName(m.field.FieldType)} {m.name} = {Literal(DefaultValue(m.type, m.field), m.field.FieldType)};\n");
                }
                if (header) sb.Append('\n');
            }

            foreach (Type type in types)
                sb.Append($"        {TypeName(type)} {Target(type)};\n");
            if (types.Count > 0) sb.Append('\n');

            sb.Append($"        protected override string ActName => \"{act.folder}\";\n\n");

            sb.Append("        protected override void Bind(Transform root)\n        {\n");
            foreach (Type type in types)
                sb.Append($"            {Target(type)} = Find<{TypeName(type)}>(root);\n");
            sb.Append("        }\n\n");

            sb.Append("        protected override void Unbind()\n        {\n");
            foreach (Type type in types)
                sb.Append($"            {Target(type)} = null;\n");
            sb.Append("        }\n\n");

            sb.Append("        protected override void Push()\n        {\n");
            foreach (Type type in types)
            {
                List<Member> fields = act.members.Where(m => m.type == type && m.field != null).ToList();
                if (fields.Count == 0) continue;
                sb.Append($"            if ({Target(type)} != null)\n            {{\n");
                foreach (Member m in fields)
                    sb.Append($"                {Target(type)}.{m.field.Name} = {m.name};\n");
                sb.Append("            }\n");
            }
            sb.Append("        }\n");

            foreach (Member m in act.members.Where(m => m.method != null))
            {
                sb.Append($"\n        public void {m.name}()\n        {{\n");
                sb.Append($"            if ({Target(m.type)} != null) {Target(m.type)}.{m.method.Name}();\n");
                sb.Append("        }\n");
            }

            sb.Append("    }\n}\n");
            return sb.ToString();
        }

        // Default of a field as written in its script (field initializer), read from a hidden, inactive instance
        static readonly Dictionary<Type, Component> _defaultsCache = new Dictionary<Type, Component>();

        static object DefaultValue(Type type, FieldInfo field)
        {
            if (!_defaultsCache.TryGetValue(type, out Component instance) || instance == null)
            {
                var go = EditorUtility.CreateGameObjectWithHideFlags("Act controller defaults", HideFlags.HideAndDontSave);
                go.SetActive(false);    // no Awake / OnEnable on the instance
                instance = go.AddComponent(type);
                _defaultsCache[type] = instance;
                EditorApplication.delayCall += () => { if (go != null) Object.DestroyImmediate(go); };
            }
            if (instance != null) return field.GetValue(instance);
            return field.FieldType.IsValueType ? Activator.CreateInstance(field.FieldType) : null;
        }

        static string TypeName(Type t)
        {
            if (t == typeof(float)) return "float";
            if (t == typeof(int)) return "int";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(Color)) return "Color";
            if (t == typeof(Vector3)) return "Vector3";
            return "global::" + t.FullName.Replace('+', '.');
        }

        static string Literal(object value, Type t)
        {
            if (t == typeof(float)) return Float((float)value);
            if (t == typeof(int)) return ((int)value).ToString(CultureInfo.InvariantCulture);
            if (t == typeof(bool)) return (bool)value ? "true" : "false";
            if (t == typeof(Color))
            {
                var c = (Color)value;
                return $"new Color({Float(c.r)}, {Float(c.g)}, {Float(c.b)}, {Float(c.a)})";
            }
            if (t == typeof(Vector3))
            {
                var v = (Vector3)value;
                return $"new Vector3({Float(v.x)}, {Float(v.y)}, {Float(v.z)})";
            }
            if (t.IsEnum)
            {
                if (Enum.IsDefined(t, value)) return $"{TypeName(t)}.{value}";
                return $"({TypeName(t)}){Convert.ToInt64(value, CultureInfo.InvariantCulture)}";
            }
            throw new ArgumentException(t.Name);
        }

        static string Float(float x)
        {
            if (float.IsNaN(x)) return "float.NaN";
            if (float.IsPositiveInfinity(x)) return "float.PositiveInfinity";
            if (float.IsNegativeInfinity(x)) return "float.NegativeInfinity";
            return x.ToString("R", CultureInfo.InvariantCulture) + "f";
        }

        static string Quote(string s) =>
            "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\"";

        static Type ControllerType(string className) => typeof(ActController).Assembly.GetType("Soledad." + className);

        // ---------------------------------------------------------------- compile errors in generated code

        static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            bool broken = messages.Any(m => m.type == CompilerMessageType.Error && m.file != null
                && m.file.Replace('\\', '/').Contains(OutputFolder));
            if (broken) EditorApplication.delayCall += WriteSafeStubs;
        }

        // The last compiled controllers are still loaded (the failed compile did not reload the domain): rewrite them
        // with the same fields but nothing that refers to the act scripts, so the project compiles again.
        static void WriteSafeStubs()
        {
            if (!Directory.Exists(OutputFolder)) return;
            bool changed = false;
            foreach (string path in Directory.GetFiles(OutputFolder, "*.cs"))
            {
                string className = System.IO.Path.GetFileNameWithoutExtension(path);
                string text = File.ReadAllText(path);
                Match actName = ActNamePattern.Match(text);
                Type compiled = ControllerType(className);

                var sb = new StringBuilder();
                sb.Append("// <auto-generated>\n");
                sb.Append("// Stub written by Soledad > Generate Act Controllers after a compile error in the generated code\n");
                sb.Append("// (an [Animate] member was renamed or removed). Rewritten in full after the next compile.\n");
                sb.Append("// </auto-generated>\n");
                sb.Append("using UnityEngine;\n\nnamespace Soledad\n{\n");
                sb.Append($"    public class {className} : ActController\n    {{\n");
                if (compiled != null)
                {
                    foreach (FieldInfo f in compiled.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
                    {
                        if (!IsSupported(f.FieldType)) continue;
                        // An enum is serialized as an int: declaring it int keeps its value
                        string typeName = f.FieldType.IsEnum ? "int" : TypeName(f.FieldType);
                        sb.Append($"        public {typeName} {f.Name};\n");
                    }
                }
                sb.Append($"        protected override string ActName => \"{(actName.Success ? actName.Groups[1].Value : "")}\";\n");
                sb.Append("        protected override void Bind(Transform root) { }\n");
                sb.Append("        protected override void Unbind() { }\n");
                sb.Append("        protected override void Push() { }\n");
                sb.Append("    }\n}\n");

                string stub = sb.ToString();
                if (text.Replace("\r\n", "\n") == stub) continue;
                File.WriteAllText(path, stub, new UTF8Encoding(false));
                changed = true;
            }
            if (!changed) return;
            Debug.LogWarning("Act controllers: the generated code no longer compiled, stubs written. The full controllers come back after this compile.");
            AssetDatabase.Refresh();
        }

        // ---------------------------------------------------------------- Show and values

        static ShowController FindShow()
        {
            foreach (ShowController show in Object.FindObjectsByType<ShowController>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (!EditorUtility.IsPersistent(show) && show.gameObject.scene.IsValid())
                    return show;
            return null;
        }

        static void AddMissingControllers(List<ActModel> acts)
        {
            ShowController show = FindShow();
            if (show == null) return;
            foreach (ActModel act in acts)
            {
                Type type = ControllerType(act.className);
                if (type == null || show.GetComponent(type) != null) continue;
                Undo.AddComponent(show.gameObject, type);
                EditorSceneManager.MarkSceneDirty(show.gameObject.scene);
                Debug.Log($"Act controllers: {act.className} added to {show.name}. Save {show.gameObject.scene.name}.", show);
            }
        }

        // Copies act scene values into the listed controller fields ("Act1Controller.GridTunnel_railsReveal"), for
        // the acts whose scene is open. Removes what it copied, or what no longer exists, from the set.
        static int Pull(List<ActModel> acts, HashSet<string> pending)
        {
            ShowController show = FindShow();
            if (show == null) return 0;
            int pulled = 0;
            var done = new List<string>();

            foreach (string key in pending)
            {
                int dot = key.IndexOf('.');
                string className = key.Substring(0, dot), fieldName = key.Substring(dot + 1);
                ActModel act = acts.FirstOrDefault(a => a.className == className);
                Member member = act?.members.FirstOrDefault(m => m.name == fieldName && m.field != null);
                Type type = ControllerType(className);
                FieldInfo controllerField = type?.GetField(fieldName);
                if (member == null || controllerField == null || controllerField.FieldType != member.field.FieldType)
                {
                    if (act == null || member == null) done.Add(key);   // gone: forget it
                    continue;
                }

                Component controller = show.GetComponent(type);
                Component target = FindInScene(member.type, act.folder);
                if (controller == null || target == null) continue;     // act scene not open: try again later

                Undo.RecordObject(controller, "Pull act values");
                controllerField.SetValue(controller, member.field.GetValue(target));
                EditorUtility.SetDirty(controller);
                EditorSceneManager.MarkSceneDirty(show.gameObject.scene);
                done.Add(key);
                pulled++;
            }

            foreach (string key in done) pending.Remove(key);
            return pulled;
        }

        static Component FindInScene(Type type, string sceneName)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                if (scene.name != sceneName || !scene.isLoaded) continue;
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    Component c = root.GetComponentInChildren(type, true);
                    if (c != null) return c;
                }
            }
            return null;
        }

        static IEnumerable<string> ReadPending() =>
            SessionState.GetString(PendingPullKey, "").Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);

        static void WritePending(HashSet<string> pending) =>
            SessionState.SetString(PendingPullKey, string.Join("\n", pending.OrderBy(k => k, StringComparer.Ordinal)));
    }
}
