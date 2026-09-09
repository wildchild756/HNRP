using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace HN.HNRP.Editor
{
    /// <summary>
    /// 构建期代码生成器：扫描所有程序集中带 <see cref="PassAttribute"/> 的类型，
    /// 在 <c>PassRegistryGenerated.cs</c> 生成硬编码注册表，
    /// 使 Player 构建实现零反射的 pass 注册。
    /// </summary>
    /// <remarks>
    /// 触发时机：
    /// <list type="bullet">
    /// <item><see cref="InitializeOnLoadAttribute"/> —— Editor 中脚本编译后运行</item>
    /// <item><see cref="IPreprocessBuildWithReport"/> —— 每次 Player 构建前运行</item>
    /// </list>
    /// </remarks>
    [InitializeOnLoad]
    public sealed class PassRegistryGenerator : IPreprocessBuildWithReport
    {
        /// <summary>
        /// 生成文件相对项目根目录的路径。
        /// </summary>
        private const string GeneratedFilePath = "Assets/HNRP/Runtime/Core/Generated/PassRegistryGenerated.cs";

        /// <summary>
        /// 扫描期间跳过的程序集名称前缀（Unity 内部、系统库）。
        /// </summary>
        private static readonly HashSet<string> SkippedAssemblyPrefixes = new()
        {
            "System", "System.", "Microsoft.", "mscorlib", "netstandard",
            "Unity", "UnityEngine", "UnityEditor", "Unity.",
            "Mono.", "nunit.", "Newtonsoft.", "ExCSS",
            // 测试程序集绝不能泄漏进生成的注册表，
            // 否则 Player 构建会因引用仅测试侧类型而编译失败。
            "HN.HNRP.Tests",
        };

        /// <summary>
        /// 通过 <see cref="InitializeOnLoadAttribute"/> 注册的静态构造函数。
        /// 在 Editor 完全初始化后调度代码生成。
        /// </summary>
        static PassRegistryGenerator()
        {
            EditorApplication.delayCall += Generate;
        }

        /// <inheritdoc />
        public int callbackOrder => -100;

        /// <inheritdoc />
        public void OnPreprocessBuild(BuildReport report)
        {
            Generate();
        }

        /// <summary>
        /// 扫描所有已加载程序集中带 <see cref="PassAttribute"/> 的
        /// <see cref="Pass"/> 子类，并写出生成的注册文件。
        /// </summary>
        [MenuItem("HNRP/Generate Pass Registry")]
        public static void Generate()
        {
            List<(string DisplayName, string FullTypeName)> passes = DiscoverPasses();

            string fileContent = BuildGeneratedFile(passes);
            string fullPath = Path.GetFullPath(GeneratedFilePath);

            string directory = Path.GetDirectoryName(fullPath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            // 仅当内容变化时才写入，避免无谓的重新编译。
            string existingContent = File.Exists(fullPath) ? File.ReadAllText(fullPath) : null;
            if (existingContent == fileContent)
            {
                return;
            }

            File.WriteAllText(fullPath, fileContent, Encoding.UTF8);
            AssetDatabase.Refresh();

            Debug.Log($"[PassRegistryGenerator] Generated {passes.Count} pass registrations → {GeneratedFilePath}");
        }

        /// <summary>
        /// 跨所有已加载程序集发现带 <see cref="PassAttribute"/> 的
        /// <see cref="Pass"/> 子类。
        /// </summary>
        /// <returns>
        /// 所有发现 pass 的按显示名排序的（DisplayName、FullTypeName）元组列表。
        /// </returns>
        private static List<(string DisplayName, string FullTypeName)> DiscoverPasses()
        {
            var result = new List<(string DisplayName, string FullTypeName)>();

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (ShouldSkipAssembly(assembly))
                {
                    continue;
                }

                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    // 跳过抽象类型、非 Pass 类型与嵌套类型。
                    if (type.IsAbstract ||
                        !type.IsSubclassOf(typeof(Pass)) ||
                        type.IsNested)
                    {
                        continue;
                    }

                    PassAttribute attr = type.GetCustomAttribute<PassAttribute>();
                    if (attr == null)
                    {
                        continue;
                    }

                    string fullTypeName = GetQualifiedTypeName(type);

                    result.Add((attr.DisplayName, fullTypeName));
                }
            }

            // 按显示名排序以保证输出确定。
            result.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));

            return result;
        }

        /// <summary>
        /// 判断 pass 发现期间某程序集是否应被跳过。
        /// 跳过系统程序集、Unity 内部与测试程序集。
        /// </summary>
        private static bool ShouldSkipAssembly(Assembly assembly)
        {
            string name = assembly.GetName().Name;

            if (string.IsNullOrEmpty(name))
            {
                return true;
            }

            foreach (string prefix in SkippedAssemblyPrefixes)
            {
                if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 构造供生成代码使用的 C# 安全全限定类型名。
        /// 优雅处理嵌套类型（'+' 替换为 '.'）与泛型类型。
        /// </summary>
        private static string GetQualifiedTypeName(Type type)
        {
            if (type.IsGenericType)
            {
                // 泛型 Pass 类型很罕见；发出警告并使用简单名。
                Debug.LogWarning(
                    $"[PassRegistryGenerator] Generic pass type detected: {type.FullName}. " +
                    "Registration may be incomplete.");
                return type.Name;
            }

            // FullName 对嵌套类型使用 '+';C# 使用 '.'。
            string name = type.FullName ?? type.Name;
            return name.Replace('+', '.');
        }

        /// <summary>
        /// 构建生成注册文件的完整内容。
        /// </summary>
        /// <param name="passes">要注册的已发现 pass。</param>
        /// <returns>完整文件内容字符串。</returns>
        private static string BuildGeneratedFile(List<(string DisplayName, string FullTypeName)> passes)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// 自动生成，请勿手动编辑。");
            sb.AppendLine("// 由 PassRegistryGenerator 在构建过程中生成。");
            sb.AppendLine("// 该文件为 Player 构建提供零反射的 pass 注册。");
            sb.AppendLine();
            sb.AppendLine("namespace HN.HNRP");
            sb.AppendLine("{");
            sb.AppendLine("    static partial class PassRegistry");
            sb.AppendLine("    {");
            sb.AppendLine("        static partial void RegisterGenerated()");
            sb.AppendLine("        {");

            if (passes.Count == 0)
            {
                sb.AppendLine("            // 未发现 pass。请为具体 Pass 子类添加 [Pass(\"Name\")]。");
            }
            else
            {
                foreach ((string displayName, string fullTypeName) in passes)
                {
                    sb.AppendLine($"            Register(\"{displayName}\", typeof({fullTypeName}));");
                }
            }

            sb.AppendLine("        }");
            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }
    }
}
