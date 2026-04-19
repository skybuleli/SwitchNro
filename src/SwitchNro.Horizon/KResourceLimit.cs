using System;

namespace SwitchNro.Horizon;

/// <summary>
/// 资源限制内核对象 (Resource Limit)
/// 简单存根，用于满足 libnx 获取资源限制句柄的需求
/// </summary>
public sealed class KResourceLimit : KObject
{
    public override string ObjectType => "KResourceLimit";
}