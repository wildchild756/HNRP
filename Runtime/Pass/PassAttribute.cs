using System;

namespace HN.HNRP
{
    /// <summary>
    /// 标记一个 <see cref="Pass"/> 子类，供 <see cref="PassRegistry"/> 自动发现。
    /// 任意需要注册的具体 Pass 子类都应用本特性：
    /// 启动时通过反射（Editor）或代码生成（Player）完成注册。
    /// </summary>
    /// <remarks>
    /// <see cref="DisplayName"/> 在全部已注册 pass 中必须唯一。
    /// </remarks>
    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class PassAttribute : Attribute
    {
        /// <summary>
        /// 用于 pass 发现与序列化的显示名。所有已注册 pass 中必须唯一。
        /// </summary>
        public string DisplayName { get; }

        /// <summary>
        /// 初始化 <see cref="PassAttribute"/> 的新实例。
        /// </summary>
        /// <param name="displayName">
        /// 该 pass 的唯一显示名，用作 <see cref="PassRegistry"/> 中的键。
        /// </param>
        public PassAttribute(string displayName)
        {
            DisplayName = displayName;
        }
    }
}
