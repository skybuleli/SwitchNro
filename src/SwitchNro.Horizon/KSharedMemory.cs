using System;
using SwitchNro.Common;
using SwitchNro.Memory;

namespace SwitchNro.Horizon;

/// <summary>
/// 共享内存内核对象
/// 用于进程间或进程与服务（如图形服务）之间的内存共享
/// </summary>
public sealed class KSharedMemory : KObject
{
    public override string ObjectType => "SharedMemory";

    /// <summary>共享内存的大小（字节）</summary>
    public ulong Size { get; }

    /// <summary>允许共享内存拥有者的权限</summary>
    public MemoryPermissions OwnerPermission { get; }

    /// <summary>允许共享内存使用者的权限</summary>
    public MemoryPermissions RemotePermission { get; }

    /// <summary>分配在虚拟机物理内存中的基地址</summary>
    public ulong PhysicalAddress { get; }

    public KSharedMemory(ulong size, MemoryPermissions ownerPerm, MemoryPermissions remotePerm, ulong physicalAddress)
    {
        Size = size;
        OwnerPermission = ownerPerm;
        RemotePermission = remotePerm;
        PhysicalAddress = physicalAddress;
    }
}