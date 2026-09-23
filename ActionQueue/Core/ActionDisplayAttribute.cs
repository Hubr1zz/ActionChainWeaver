using System;

namespace CardGame.ActionQueue
{
    /// <summary>仅供编辑器类型目录使用，不参与运行时调度或分类。</summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class ActionDisplayAttribute : Attribute
    {
        public ActionDisplayAttribute(string category, string displayName = null)
        {
            Category = category ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
        }

        public string Category { get; }
        public string DisplayName { get; }
    }
}
