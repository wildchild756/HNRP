using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace HN.HNRP
{
    /// <summary>
    /// 经 <see cref="PassAttribute"/> 发现的 <see cref="Pass"/> 类型的中央注册表。
    /// 提供基于名称的快速查找与全部已注册渲染 pass 的枚举。
    /// </summary>
    /// <remarks>
    /// Editor 中 <see cref="RegisterAll"/> 扫描已加载程序集寻找带 <c>[Pass]</c>
    /// 的类型。Player 构建优先使用代码生成以避免反射开销 —— 见
    /// <c>PassRegistryGenerated.cs</c>（由 <c>PassRegistryGenerator</c> 在构建期生成）。
    ///
    /// <para>
    /// 注册顺序：
    /// <list type="number">
    /// <item><see cref="RegisterGenerated"/> —— 硬编码表（Player 构建）</item>
    /// <item>反射扫描 —— <c>#if UNITY_EDITOR</c> 下（覆盖新增 pass）</item>
    /// </list>
    /// </para>
    /// </remarks>
    public static partial class PassRegistry
    {
        /// <summary>
        /// 显示名到具体 <see cref="Pass"/> 子类的映射。
        /// 键为 <see cref="PassAttribute.DisplayName"/>，
        /// 值为带该特性的 <see cref="Type"/>。
        /// </summary>
        private static readonly Dictionary<string, Type> RegisteredPasses = new();

        /// <summary>
        /// 把 <see cref="Pass"/> 类型注册到给定显示名下。
        /// 重名静默覆盖旧条目。
        /// </summary>
        /// <param name="name">注册用的显示名。</param>
        /// <param name="type">
        /// 具体 <see cref="Type"/>（必须是 <see cref="Pass"/> 的子类）。
        /// </param>
        public static void Register(string name, Type type)
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            RegisteredPasses[name] = type;
        }

        /// <summary>
        /// 由自动生成的 <c>PassRegistryGenerated.cs</c> 实现的 partial 方法。
        /// 内含硬编码的 <c>Register("Name", typeof(ConcretePass))</c> 调用，
        /// 供 Player 构建零反射使用。
        /// </summary>
        /// <remarks>
        /// Editor 下生成文件可为空（全部经反射注册）；
        /// Player 构建下该方法提供全部注册。
        /// </remarks>
        static partial void RegisterGenerated();

        /// <summary>
        /// 扫描所有已加载程序集中带 <see cref="PassAttribute"/> 的类型并填入注册表。
        /// </summary>
        /// <remarks>
        /// 只注册继承自 <see cref="Pass"/> 且带 <c>[Pass("Name")]</c> 的非抽象类。
        /// 重名静默覆盖旧条目。
        ///
        /// <para>
        /// 注册顺序：
        /// <list type="number">
        /// <item>调用 <see cref="RegisterGenerated"/> —— 来自代码生成的硬编码表</item>
        /// <item>Editor-only 反射扫描 —— 覆盖上次生成后新增的 pass</item>
        /// </list>
        /// </para>
        /// </remarks>
        public static void RegisterAll()
        {
            RegisteredPasses.Clear();

            // 第 1 步：来自生成代码的硬编码注册（Player 零反射）
            RegisterGenerated();

#if UNITY_EDITOR
            // 第 2 步：反射扫描 —— 覆盖上次代码生成后新增的 pass
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch (ReflectionTypeLoadException)
                {
                    // 跳过无法加载的程序集（如引用损坏）
                    continue;
                }

                foreach (Type type in types)
                {
                    if (!type.IsAbstract &&
                        type.IsSubclassOf(typeof(Pass)) &&
                        type.GetCustomAttribute<PassAttribute>() is { } attr)
                    {
                        RegisteredPasses[attr.DisplayName] = type;
                    }
                }
            }
#endif
        }

        /// <summary>
        /// 按显示名查找已注册的 <see cref="Pass"/> 类型。
        /// </summary>
        /// <param name="name">要查找的 <see cref="PassAttribute.DisplayName"/>。</param>
        /// <returns>
        /// 找到时返回 <see cref="Type"/>；否则返回 <c>null</c>。
        /// </returns>
        public static Type GetPassType(string name)
        {
            if (name == null)
            {
                return null;
            }

            RegisteredPasses.TryGetValue(name, out Type type);
            return type;
        }

        /// <summary>
        /// 返回全部已注册 pass 的显示名。
        /// </summary>
        /// <returns>
        /// 当前已注册显示名的可枚举集合。
        /// </returns>
        public static IEnumerable<string> GetAllPassNames()
        {
            return RegisteredPasses.Keys;
        }

        /// <summary>
        /// 按显示名创建新的 <see cref="Pass"/> 实例。
        /// 先查注册表得到 <see cref="Type"/>，再用给定实例名通过
        /// <c>Activator.CreateInstance</c> 实例化。
        /// </summary>
        /// <param name="displayName">
        /// 要创建 pass 的 <see cref="PassAttribute.DisplayName"/>。
        /// </param>
        /// <param name="instanceName">
        /// 新 pass 的实例名，传给其字符串构造函数。
        /// </param>
        /// <returns>
        /// 新创建的 <see cref="Pass"/> 实例；显示名未注册或实例化失败时为
        /// <c>null</c>。
        /// </returns>
        /// <remarks>
        /// <para>
        /// 目标 <see cref="Pass"/> 子类必须提供接受单个 <see cref="string"/>
        /// 参数（实例名）的公开构造函数。不存在时返回 <c>null</c>。
        /// </para>
        /// </remarks>
        public static Pass CreatePass(string displayName, string instanceName)
        {
            if (displayName == null || instanceName == null)
            {
                return null;
            }

            if (!RegisteredPasses.TryGetValue(displayName, out Type type))
            {
                return null;
            }

            try
            {
                return (Pass)Activator.CreateInstance(type, instanceName);
            }
            catch
            {
                return null;
            }
        }
    }
}
