# Switch NRO 模拟器 — 技术规格说明书

> **项目代号**: switch-nro  
> **目标平台**: macOS M1 (Apple Silicon ARM64), 8GB RAM  
> **技术栈**: C# 14 + .NET 10 + Avalonia UI  
> **创建日期**: 2025-07  

---

## 1. 项目概述

### 1.1 目标

从零构建一个专门在 Mac M1 8GB 设备上模拟运行 Nintendo Switch NRO（自制软件）的模拟器。NRO 是 Switch 自制社区的标准可执行格式，包含 `.text`、`.rodata`、`.data` 等段，类似于简化的 ELF 格式。

### 1.2 核心约束

| 约束项 | 说明 |
|--------|------|
| **目标硬件** | Mac M1 (ARM64), 8GB 统一内存 |
| **内存上限** | 模拟器自身 + 虚拟地址空间总计 < 4GB 常驻内存 |
| **性能目标** | NRO 程序流畅运行 @ 60fps |
| **兼容范围** | 尽可能广泛的 NRO 自制软件兼容性 |
| **开发方式** | 从零构建全新项目（可参考 ryujinx-1.3.3 架构思路） |
| **时间约束** | 无严格时间限制，质量优先 |

### 1.3 非目标

- 不以模拟商业 NSP/NSO 游戏为主要目标（虽然架构上不排除）
- 不追求跨平台（仅 Mac M1）
- 不追求 NativeAOT 编译（Avalonia 当前不支持 macOS ARM64 NativeAOT）

---

## 2. 系统架构

### 2.1 顶层架构图

```
┌──────────────────────────────────────────────────────┐
│                    Avalonia UI 层                     │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐ ┌─────────┐ │
│  │ 主窗口    │ │ 调试面板  │ │ 设置面板  │ │ 状态栏  │ │
│  └──────────┘ └──────────┘ └──────────┘ └─────────┘ │
│         │                                              │
│    Metal 渲染控件 (嵌入游戏画面)                        │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│                   App 层                              │
│  ┌──────────┐ ┌──────────┐ ┌──────────┐             │
│  │ InputMgr │ │ AudioMgr │ │ ConfigMgr │             │
│  └──────────┘ └──────────┘ └──────────┘             │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│                  Core 层                              │
│  ┌─────────────────────────────────────────────────┐ │
│  │                 HLE 子系统                        │ │
│  │  ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐ ┌─────┐      │ │
│  │  │ sm  │ │ fs  │ │ am  │ │ vi  │ │ hid │ ...  │ │
│  │  └─────┘ └─────┘ └─────┘ └─────┘ └─────┘      │ │
│  └─────────────────────────────────────────────────┘ │
│  ┌──────────┐ ┌──────────────┐ ┌──────────────┐      │
│  │ NRO 加载器│ │ Horizon 进程  │ │ IPC 调度器   │      │
│  └──────────┘ └──────────────┘ └──────────────┘      │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│               CPU / Memory 层                         │
│  ┌──────────────────┐  ┌────────────────────────┐    │
│  │ Hypervisor 执行引擎│  │ 虚拟内存管理器 (按需分页) │    │
│  │ (ARM64 → ARM64)  │  │ 页表 / TLB / 内存映射   │    │
│  └──────────────────┘  └────────────────────────┘    │
│  ┌──────────────────┐                                 │
│  │ JIT 回退引擎      │ (Hypervisor 不可用时的降级方案) │
│  └──────────────────┘                                 │
└──────────────────────┬───────────────────────────────┘
                       │
┌──────────────────────┼───────────────────────────────┐
│               Graphics 层                             │
│  ┌──────────────────┐  ┌────────────────────────┐    │
│  │ Metal 渲染后端    │  │ Vulkan/MoltenVK 备选后端  │    │
│  │ (首选，M1 原生)   │  │ (兼容性回退)            │    │
│  └──────────────────┘  └────────────────────────┘    │
│  ┌──────────────────┐  ┌────────────────────────┐    │
│  │ Shader 编译器     │  │ 纹理管理器              │    │
│  │ (Maxwell→MSL)    │  │ (ASTC 硬解优先)         │    │
│  └──────────────────┘  └────────────────────────┘    │
└───────────────────────────────────────────────────────┘
```

### 2.2 项目模块划分

采用完整模块化设计（类似 ryujinx），每个模块为独立 C# 项目：

| 项目名 | 职责 | 依赖 |
|--------|------|------|
| `SwitchNro.Cpu` | CPU 模拟（Hypervisor + JIT 回退） | 无 |
| `SwitchNro.Memory` | 虚拟内存管理、页表、按需分页 | 无 |
| `SwitchNro.Horizon` | Horizon OS 模拟、进程管理、IPC 调度 | Cpu, Memory |
| `SwitchNro.HLE` | 系统服务 HLE 实现（sm/fs/vi/am/hid/...） | Horizon |
| `SwitchNro.NroLoader` | NRO 文件解析、加载、重定位、动态模块 | Memory |
| `SwitchNro.Graphics.GAL` | 图形抽象层（Graphics Abstract Layer） | 无 |
| `SwitchNro.Graphics.Metal` | Metal 渲染后端 | GAL |
| `SwitchNro.Graphics.Vulkan` | Vulkan/MoltenVK 渲染后端 | GAL |
| `SwitchNro.Graphics.Shader` | Shader 反编译 & 编译（Maxwell → MSL/SPIR-V） | GAL |
| `SwitchNro.Graphics.Texture` | 纹理格式转换、ASTC 处理 | GAL |
| `SwitchNro.Input` | 输入管理（键盘/鼠标/蓝牙手柄） | 无 |
| `SwitchNro.Audio` | 音频管理 | 无 |
| `SwitchNro.Audio.SDL2` | SDL2 音频后端 | Audio |
| `SwitchNro.Audio.CoreAudio` | CoreAudio 低延迟后端 | Audio |
| `SwitchNro.UI` | Avalonia UI 主应用 | 所有模块 |
| `SwitchNro.Debugger` | 调试器（断点/内存查看/GPU Profiler） | Cpu, Memory, Graphics.GAL |
| `SwitchNro.Common` | 通用工具、日志、配置 | 无 |
| `SwitchNro.Tests` | 单元测试 & 集成测试 | 按需 |

**解决方案文件**: `SwitchNro.sln`

---

## 3. CPU 模拟子系统

### 3.1 策略：Hypervisor 直接执行优先

M1 和 Switch 均为 ARM64 架构，利用 macOS `Hypervisor.framework` (HVF) 可将 NRO 的 ARM64 代码直接映射到虚拟 CPU 执行，性能接近原生。

### 3.2 Hypervisor 执行引擎

**核心原理**：
- 创建 HVF 虚拟机实例，分配虚拟 CPU (vCPU)
- 将 NRO 的 `.text` 段直接映射到 vCPU 的物理内存区
- 设置虚拟异常处理器拦截系统调用 (SVC) 和内存故障
- SVC 拦截后，转交 HLE 子系统处理，处理完毕后恢复 vCPU 执行

**关键实现**：

```csharp
// 伪代码 - Hypervisor 执行引擎核心接口
public interface IExecutionEngine : IDisposable
{
    /// <summary>将代码段映射到虚拟地址空间并标记可执行</summary>
    void MapCode(ulong vaddr, ReadOnlySpan<byte> code, bool executable = true);
    
    /// <summary>将数据段映射到虚拟地址空间</summary>
    void MapMemory(ulong vaddr, ulong size, MemoryPermissions perms);
    
    /// <summary>从入口点开始执行，直到遇到 SVC 或异常</summary>
    ExecutionResult Execute(ulong entryPoint);
    
    /// <summary>获取/设置通用寄存器值</summary>
    ulong GetRegister(int index);
    void SetRegister(int index, ulong value);
    
    /// <summary>读取虚拟内存</summary>
    Span<byte> ReadVirtualMemory(ulong vaddr, ulong size);
    void WriteVirtualMemory(ulong vaddr, ReadOnlySpan<byte> data);
}

public enum ExecutionResult
{
    SVC,              // 系统调用，需 HLE 处理
    Breakpoint,       // 触发断点
    MemoryFault,      // 内存访问异常
    Undefined,        // 未定义指令
    NormalExit,       // 正常退出
}
```

**HVF 桥接**：
- 使用 P/Invoke 调用 `Hypervisor.framework` 的 C API
- 关键 API：`hv_vm_create`, `hv_vm_map`, `hv_vcpu_create`, `hv_vcpu_run`, `hv_vcpu_get_reg`
- vCPU 退出时检查退出原因（SVC / 故障 / 断点），分发到对应处理器

### 3.3 JIT 回退引擎

当 Hypervisor 不可用时（例如未来 macOS 版本限制或调试需求），降级到 JIT 软件翻译：

- 参考 ARMeilleure 的架构：ARM64 → IR → 寄存器分配 → 本地代码生成
- 利用 .NET 10 的 `System.Runtime.Intrinsics` 实现 SIMD 加速
- JIT 模式性能约为 Hypervisor 的 40-60%，作为兼容性备选

### 3.4 系统调用拦截与分发

```
vCPU 执行 → 遇到 SVC #N → HVF 退出 → 读取 SVC 编号和参数
→ HLE SVC 表查找 → 调用对应 C# 处理函数 → 写回返回值
→ 恢复 vCPU 执行
```

需拦截的主要 SVC：
- `SVC 0x01` (SetHeapSize) — 内存分配
- `SVC 0x03` (MapMemory) — 内存映射
- `SVC 0x0D` (WaitSynchronization) — 同步等待
- `SVC 0x0E` (CancelSynchronization) — 取消同步
- `SVC 0x1F` (ConnectToNamedPort) — 连接命名端口 (IPC)
- `SVC 0x21` (SendSyncRequest) — 发送同步 IPC 请求
- `SVC 0x22` (SendSyncRequestWithUserBuffer) — 带用户缓冲区的 IPC
- `SVC 0x26` (OutputDebugString) — 调试输出
- `SVC 0x29` (GetInfo) — 获取系统信息
- `SVC 0x50-0x6F` — 内存管理相关
- 其他约 80 个 SVC 按需实现

---

## 4. 虚拟内存管理

### 4.1 策略：按需分页，懒加载

M1 8GB 硬约束下，不能完整映射 Switch 的 4GB 地址空间。采用按需分页策略，只映射 NRO 实际使用的内存页。

### 4.2 虚拟地址空间布局

参考 Horizon OS 的地址空间布局：

```
0x0000_0000_0000 - 0x0000_007F_FFFF   // .text 段 (NRO 代码, RX)
0x0000_0080_0000 - 0x0000_00BF_FFFF   // .rodata 段 (只读数据, R)
0x0000_00C0_0000 - 0x0000_00FF_FFFF   // .data 段 (可写数据, RW)
0x0000_0100_0000 - 0x0000_01FF_FFFF   // 动态 NRO 模块区
0x0000_0200_0000 - 0x0000_02FF_FFFF   // 主线程栈
0x0000_1000_0000 - 0x0000_1FFF_FFFF   // 堆内存 (按需扩展)
0x0000_2000_0000 - 0x0000_2FFF_FFFF   // IPC 缓冲区
0x0000_3000_0000 - 0x0000_3FFF_FFFF   // GPU 显存映射
0xFFFF_0000_0000 - 0xFFFF_FFFF_FFFF   // 内核区 (不可访问)
```

**ASLR**：NRO 基地址在 bits 37-12 随机化（对齐 4KB），与真实 Horizon 行为一致。

### 4.3 页表与 TLB

```csharp
public class VirtualMemoryManager
{
    // 两级页表：PGD → PTE，每页 4KB
    private readonly PageTable _pageTable;
    
    // 软件 TLB 缓存（热点虚拟地址 → 物理页映射）
    private readonly TlbCache _tlb;
    
    /// <summary>按需映射：首次访问触发缺页中断，分配物理页后映射</summary>
    public void HandlePageFault(ulong vaddr, MemoryPermissions requiredPerms);
    
    /// <summary>批量映射 NRO 段</summary>
    public void MapNroSegments(NroHeader header, Stream nroStream);
    
    /// <summary>释放 NRO 模块占用的页</summary>
    public void UnmapNroModules(ulong baseAddr, ulong size);
}
```

### 4.4 物理内存分配

- 使用 .NET 10 的 `NativeMemory.Alloc` / `NativeMemory.Free` 管理非托管物理页
- 每个物理页 4KB，通过位图跟踪使用状态
- 堆内存采用 Bump Allocator + Free List 混合策略
- 内存压力监测：当常驻内存接近 3.5GB 时触发 GC 和页面回收

---

## 5. NRO 加载器

### 5.1 NRO 文件格式

```
NRO Header (0x70 bytes):
  +0x00: Magic "NRO0"
  +0x04: Version (0)
  +0x08: Size (整个 NRO 大小)
  +0x10: .text 段偏移 & 大小
  +0x20: .rodata 段偏移 & 大小  
  +0x30: .data 段偏移 & 大小
  +0x40: .bss 大小
  +0x48: 模块名偏移
  +0x50: 各段页对齐大小

Asset Section (可选，追加在 NRO 主体之后):
  +0x00: Magic "ASET"
  +0x04: Asset Header 版本
  +0x08: Icon 偏移 & 大小
  +0x18: Screenshot 偏移 & 大小
  +0x28: RomFS 偏移 & 大小
```

### 5.2 加载流程

```
1. 读取 NRO 文件头，验证 Magic "NRO0"
2. 提取 .text / .rodata / .data / .bss 段信息
3. 为 ASLR 随机化分配基地址
4. 将各段映射到虚拟地址空间（按需分页）
5. 执行重定位：处理 MOD0 段中的重定位表
    - R_ARM_RELATIVE: 基地址 + Addend
    - R_ARM_GLOB_DAT: 符号地址解析
6. 解析 Asset Section（如有），提取图标、RomFS 偏移
7. 设置入口点：base_addr + header.EntryPointOffset
8. 构造 Homebrew ABI 环境：
    - X0 = loader_config 结构指针
    - X1 = argv 数组指针
    - 设置线程局部存储 (TLS)
9. 跳转到入口点执行
```

### 5.3 动态 NRO 模块加载

NRO 程序可在运行时通过 `envGetModuleLoadHandle` 和动态加载器加载额外的 NRO 模块：

```csharp
public class NroDynamicLoader
{
    /// <summary>加载嵌入式 NRO 模块（dlr 格式）</summary>
    public ulong LoadEmbeddedModule(ulong parentBase, string moduleName);
    
    /// <summary>解析模块间符号依赖</summary>
    public void ResolveImports(ModuleEntry importer, ModuleEntry exporter);
    
    /// <summary>卸载动态模块并回收内存</summary>
    public void UnloadModule(ulong moduleBase);
}
```

- 支持模块间导入/导出符号解析
- 每个动态模块在独立地址区域加载
- 模块卸载时释放占用页并清除符号表

---

## 6. HLE 子系统（高层模拟）

### 6.1 实现策略：核心必选 + 按需扩展

先实现 NRO 运行的最小必选服务集，遇到新 NRO 需要时再补充。模拟最新固件 (19.x+) API。

### 6.2 核心必选服务（Phase 1 — 必须实现）

| 服务 | 端口名 | 职责 | 优先级 |
|------|--------|------|--------|
| **Service Manager** | `sm:` | 服务发现，其他服务的入口 | P0 |
| **File System** | `fs:` | 文件系统访问（RomFS / SDCard） | P0 |
| **Video** | `vi:` | 显示输出管理 | P0 |
| **Application** | `am:` | 应用生命周期 | P0 |
| **HID** | `hid:` | 输入设备（手柄/触屏） | P0 |
| **Audio** | `audout:` / `audren:` | 音频输出/渲染 | P0 |
| **Process** | `pm:` | 进程管理 | P0 |

### 6.3 扩展服务（Phase 2 — 按需实现）

| 服务 | 端口名 | 职责 | 典型使用场景 |
|------|--------|------|-------------|
| **Network** | `nifm:` | 网络接口管理 | NRO 检查网络状态 |
| **Time** | `time:` | 系统时间 | 存档时间戳 |
| **Settings** | `set:` | 系统设置 | 语言/区域检测 |
| **SSL** | `ssl:` | TLS/SSL | HTTPS 通信 |
| **NSD** | `nsd:` | 网络服务发现 | 在线服务 |
| **Socket** | `bsd:` | BSD Socket API | 实际网络通信 |
| **LCD** | `lcd:` | 屏幕亮度控制 | 亮度调节 |

### 6.4 完整服务列表（Phase 3 — 长期目标）

参考 Switchbrew wiki，约 60+ 个 IPC 服务接口。包括：
- `fatal:` (错误处理), `ldr:` (加载器), `ro:` (可重定位对象)
- `nvdrv:` (NVIDIA 驱动), `nvnflinger:` (显示合成)
- `jpegdec:` (JPEG 解码), `capsrv:` (截图服务)
- `mmnv:` (媒体播放), `friends:` (好友系统) 等

### 6.5 IPC 通信框架

```csharp
public class IpcServer
{
    /// <summary>注册服务实现</summary>
    public void RegisterService<TInterface>(string portName, TInterface implementation) where TInterface : class;
    
    /// <summary>处理来自 guest 的 IPC 请求</summary>
    public IpcResponse HandleRequest(IpcMessage request);
}

public interface IIpcService
{
    /// <summary>服务的命令 ID → 方法映射表</summary>
    IReadOnlyDictionary<uint, ServiceMethod> CommandTable { get; }
}

public delegate ServiceResult ServiceMethod(IpcRequest request, ref IpcResponse response);
```

IPC 消息格式遵循 Horizon 规范：
- 请求头包含 type、cmd、pid、已拷贝/移动句柄数、数据大小
- 支持缓冲区传递（A/B/C/X/W 类型描述符）
- 自动序列化/反序列化 C# 结构体 ↔ IPC 二进制格式

### 6.6 Horizon 进程模型

支持多进程和 Applet 机制：

```csharp
public class HorizonSystem
{
    /// <summary>创建新进程</summary>
    public Process CreateProcess(ProcessInfo info);
    
    /// <summary>启动 Library Applet</summary>
    public AppletSession StartLibraryApplet(AppletId id, AppletLaunchParam param);
    
    /// <summary>进程间通信 (Applet ↔ 主进程)</summary>
    public IAppletCommunicator GetAppletCommunicator(AppletId id);
}
```

Applet 类型支持：
- `LibraryApplet` (如软键盘 Swkbd、错误显示 Error、网页 Web)
- `OverlayApplet` (如通知覆盖层)
- `SystemApplet` (如主菜单 Home Menu)

---

## 7. 图形渲染子系统

### 7.1 后端策略：Metal 优先 + MoltenVK 备选

**首选：Metal 原生后端**
- M1 GPU 原生 API，无转换层开销
- 利用 Apple Silicon 统一内存架构（CPU/GPU 共享内存），减少数据拷贝
- 支持 ASTC 硬件解码（M1 GPU 原生支持）
- 通过 Metal Performance HUD 监控 GPU 性能

**备选：Vulkan (MoltenVK)**
- 作为兼容性回退，主要在 Metal 后端遇到兼容问题时切换
- 通过 MoltenVK 将 Vulkan 调用转译为 Metal
- 可复用 ryujinx 的 Vulkan 后端代码作为起点

### 7.2 图形抽象层 (GAL)

```csharp
public interface IRenderer : IDisposable
{
    /// <summary>初始化渲染器</summary>
    void Initialize(IRendererCreateInfo info);
    
    /// <summary>提交 GPU 命令缓冲区</summary>
    void SubmitCommands(IGpuCommandBuffer cmds);
    
    /// <summary>呈现帧到目标表面</summary>
    void Present();
    
    /// <summary>创建纹理</summary>
    ITexture CreateTexture(TextureCreateInfo info);
    
    /// <summary>创建着色器程序</summary>
    IShaderProgram CreateProgram(ShaderSource[] sources);
    
    /// <summary>创建渲染目标</summary>
    IRenderTarget CreateRenderTarget(RenderTargetCreateInfo info);
    
    /// <summary>获取帧缓冲用于嵌入 Avalonia</summary>
    IntPtr GetFrameBufferHandle();
}
```

### 7.3 Shader 编译器

**策略：移植 ryujinx shader 编译器架构 + Metal MSL 输出**

```
Maxwell GLSL/NVShader → Decompiler → IR (Intermediate Representation) 
    ├── MSL CodeGen → Metal 后端使用
    └── SPIR-V CodeGen → Vulkan 后端使用
```

- Maxwell GPU (GM20B) 使用自定义 shader 格式，需先反编译为 IR
- 从 IR 生成 MSL (Metal Shading Language) 或 SPIR-V
- 实现磁盘缓存：首次编译后存储到 `~/.switchnro/shader_cache/`
- 缓存键 = shader binary hash + 后端类型
- M1 SSD 速度快，二次加载几乎无感

```csharp
public class ShaderCompiler
{
    /// <summary>编译 Maxwell shader 为目标后端格式</summary>
    public CompiledShader Compile(ShaderBinary binary, ShaderTarget target);
    
    /// <summary>从磁盘缓存加载已编译 shader</summary>
    public CompiledShader? LoadFromCache(Hash128 key);
    
    /// <summary>将编译结果保存到磁盘缓存</summary>
    public void SaveToCache(Hash128 key, CompiledShader shader);
}

public enum ShaderTarget
{
    Metal_MSL,
    Vulkan_SPIRV
}
```

### 7.4 纹理处理

**策略：M1 ASTC 硬件解码优先，软件解码兜底**

```csharp
public class TextureManager
{
    /// <summary>上传纹理到 GPU</summary>
    public ITexture UploadTexture(TextureDescriptor desc, Span<byte> data);
    
    /// <summary>检测 M1 ASTC 硬件支持</summary>
    public bool IsAstcHardwareSupported { get; }
}
```

- Switch 的 ASTC 纹理：检测 M1 GPU ASTC 支持 → 硬解直通（零 CPU 开销）
- 不支持时：CPU 软件解码 ASTC → 转换为 BCn 格式 → 上传到 GPU
- 其他格式：BCn (硬件支持)、RGB565/RGBA4444/RGBA8888 (直接映射)
- M1 统一内存：纹理数据可零拷贝在 CPU/GPU 间共享

### 7.5 渲染输出嵌入 Avalonia

**策略：Metal 渲染嵌入 Avalonia 自定义控件**

```csharp
public class MetalGameControl : Avalonia.Controls.Control
{
    private MTKView? _metalView;
    private IRenderer _renderer;
    
    /// <summary>将 Metal 帧缓冲渲染到此 Avalonia 控件</summary>
    protected override void OnRender(DrawingContext context)
    {
        // 通过 CAMetalLayer 与 Avalonia 的渲染循环同步
        _renderer.Present();
    }
}
```

实现方案：
- 使用 `CAMetalLayer` 作为 Avalonia 自定义控件的渲染目标
- Metal 渲染帧通过 `presentDrawable:` 提交到 layer
- 与 Avalonia 的 Skia/Metal 渲染管线协同（共用同一个 MTLDevice）
- 帧率同步：使用 `CVDisplayLink` 或 `MTLDrawable.presented` 事件

---

## 8. 输入子系统

### 8.1 输入源支持

| 输入源 | 映射目标 | 实现方式 |
|--------|---------|---------|
| **键盘** | Switch 手柄按键 | SDL2 键盘事件 → HID 状态更新 |
| **鼠标** | Switch 触屏 | SDL2 鼠标事件 → TouchScreen 状态 |
| **蓝牙手柄** | Switch Pro Controller | CoreBluetooth 框架 / SDL2 游戏手柄 API |

### 8.2 默认键盘映射

```
Switch 按键    ←  键盘
─────────────────────────
A              ←  X
B              ←  Z
X              ←  S
Y              ←  A
L              ←  Q
R              ←  E
ZL             ←  1
ZR             ←  3
Plus (+)       ←  Enter
Minus (-)      ←  Backspace
D-Pad Up       ←  Arrow Up
D-Pad Down     ←  Arrow Down
D-Pad Left     ←  Arrow Left
D-Pad Right    ←  Arrow Right
Left Stick     ←  WASD
Right Stick    ←  IJKL
Home           ←  Escape
Screenshot     ←  F12
Touch          ←  鼠标左键 (点击游戏区域)
```

用户可在设置面板自定义映射。

### 8.3 蓝牙手柄支持

- 通过 SDL2 游戏手柄 API 检测连接
- 支持 Pro Controller 映射（标准游戏手柄布局）
- 支持通用 HID 游戏手柄
- 振动反馈：通过 SDL2 haptic API
- Joy-Con 暂不支持（需要特定的蓝牙配对协议，复杂度高）

---

## 9. 音频子系统

### 9.1 后端策略：SDL2 优先 + CoreAudio 备选

**SDL2 音频后端**（默认）：
- 简单可靠，跨平台兼容
- 通过 SDL_OpenAudioDevice 打开音频设备
- 回调模式：模拟器填充音频采样缓冲区 → SDL 播放
- 延迟约 20-50ms（对大多数 NRO 自制软件足够）

**CoreAudio 后端**（优化选项）：
- macOS 原生低延迟音频
- 使用 AUAudioUnit / AVAudioEngine
- 延迟可降至 < 10ms
- 实现复杂度较高，作为后续优化目标

### 9.2 音频格式支持

| Switch 格式 | 说明 | 处理方式 |
|------------|------|---------|
| PCM16 | 16-bit PCM，最常见 | 直接输出 |
| PCM Float | 32-bit float PCM | 转换为 PCM16 输出 |
| ADPCM | 自适应差分 PCM | 软件解码为 PCM16 |
| AAC | Advanced Audio Coding | FFmpeg 解码（可选依赖） |

---

## 10. 文件系统模拟

### 10.1 策略：RomFS + SDCard 模拟

```csharp
public class VirtualFileSystem
{
    /// <summary>从 NRO 文件加载 RomFS</summary>
    public void MountRomFs(string nroPath, ulong romFsOffset, ulong romFsSize);
    
    /// <summary>将本地目录映射为虚拟 SDCard</summary>
    public void MountSdCard(string hostDirectory);
    
    /// <summary>打开虚拟文件</summary>
    public IFile OpenFile(string virtualPath, OpenMode mode);
    
    /// <summary>列出目录</summary>
    public IEnumerable<DirectoryEntry> ReadDirectory(string virtualPath);
}
```

### 10.2 虚拟文件系统布局

```
/ (虚拟根)
├── switch/           ← SDCard 映射 (主机目录: ~/.switchnro/sdcard/switch/)
│   ├── app.nro       ← 用户放置的 NRO 文件
│   └── ...
├── RomFS/            ← 当前 NRO 的 RomFS (从 NRO Asset Section 加载)
│   ├── assets/
│   └── ...
├── save/             ← 存档目录 (主机目录: ~/.switchnro/save/)
└── system/           ← 最小系统文件模拟
    ├── contents/
    └── ...
```

### 10.3 RomFS 解析

NRO 的 Asset Section 包含 RomFS 偏移，RomFS 格式：

```
RomFS Header:
  +0x00: HeaderSize
  +0x04: FileHashTableOffset / FileHashTableSize
  +0x0C: FileTableOffset / FileTableSize
  +0x14: DirHashTableOffset / DirHashTableSize
  +0x1C: DirTableOffset / DirTableSize
  +0x24: DataOffset

File Entry:  (20 bytes)
  ParentDir, Sibling, Offset, Size, NameOffset, NameSize

Dir Entry:   (24 bytes)
  ParentDir, Sibling, ChildDir, File, Offset, NameOffset, NameSize
```

---

## 11. Avalonia UI 设计

### 11.1 整体风格

遵循现有项目的「竹风美学 (Bamboo-Console)」UI/UX 设计规范 V1.0：
- Glassmorphism 2.0：背景模糊、1px 半透明白色边缘光、阴影深度感
- 动态氛围光：分层径向渐变，"呼吸"循环动画
- 色彩体系：竹绿 (#4A90E2 → #2D8C5A)、翡翠色、深绿

### 11.2 主窗口布局

```
┌─────────────────────────────────────────────────────────────┐
│  🎮 SwitchNro    │  全局搜索框  │  📂打开NRO │ ⚙️设置 │ 🐛调试 │
├─────────────────────────────────────────────────────────────┤
│                                        │                    │
│                                        │  线程/进程面板     │
│       Metal 游戏渲染区域                │  ├ 进程列表       │
│       (嵌入 Avalonia 控件)              │  ├ 线程状态       │
│                                        │  └ 内存占用       │
│                                        ├───────────────────│
│                                        │  服务状态面板      │
│                                        │  ├ 已注册服务      │
│                                        │  └ SVC 调用日志   │
├─────────────────────────────────────────────────────────────┤
│  底部状态栏: FPS:60 │ 内存:2.1GB │ 后端:Metal │ 输入:键盘+鼠标 │
└─────────────────────────────────────────────────────────────┘
```

### 11.3 核心控件

| 控件 | 功能 | 备注 |
|------|------|------|
| `MetalGameControl` | 嵌入 Metal 渲染的游戏画面 | 自定义控件 |
| `NroFileOpener` | 文件选择对话框，支持拖拽打开 | Avalonia StorageProvider |
| `InputConfigPanel` | 输入映射配置 | 可自定义按键绑定 |
| `DebugPanel` | 调试面板：断点/内存/寄存器 | 可折叠侧边栏 |
| `GpuProfilerPanel` | GPU 性能分析面板 | 显示 draw call、纹理占用等 |
| `ServiceLogPanel` | HLE 服务调用日志 | 实时滚动日志 |
| `StatusBar` | 底部状态栏 | FPS/内存/后端/输入状态 |

### 11.4 调试器 UI

```
┌─ 调试面板 ──────────────────────────────────┐
│  ▶ 继续  ⏸ 暂停  ⏭ 单步  ⏭⏭ 跳过          │
├─────────────────────────────────────────────┤
│  断点列表                                    │
│  ├ 0x00080000: svcConnectToNamedPort         │
│  ├ 0x00081234: main + 0x34                   │
│  └ [添加断点]                                 │
├─────────────────────────────────────────────┤
│  寄存器视图                                   │
│  X0=0x00000000  X1=0x00080000  ...           │
│  SP=0x00020000  PC=0x00081234                │
├─────────────────────────────────────────────┤
│  内存查看器 (Hex Editor 风格)                 │
│  0x00080000: 48 89 5C 24 10 57 48 83 ...     │
│  0x00080010: E4 F0 0F 1E 44 89 44 24 ...     │
├─────────────────────────────────────────────┤
│  GPU Profiler                                │
│  ├ Draw Calls: 120/frame                     │
│  ├ Texture Memory: 48MB                       │
│  └ Shader Cache: 23/24 hit                    │
└─────────────────────────────────────────────┘
```

---

## 12. 调试器子系统

### 12.1 断点管理

```csharp
public class BreakpointManager
{
    /// <summary>添加地址断点</summary>
    void AddBreakpoint(ulong address, BreakpointType type = BreakpointType.Execute);
    
    /// <summary>添加条件断点（表达式求值）</summary>
    void AddConditionalBreakpoint(ulong address, string condition);
    
    /// <summary>添加内存访问断点（读/写）</summary>
    void AddMemoryBreakpoint(ulong address, ulong size, MemoryAccessType access);
    
    /// <summary>断点触发时回调</summary>
    event Action<BreakpointHit> BreakpointHit;
}
```

### 12.2 内存查看器

- 十六进制视图 + ASCII 解码
- 支持搜索字节序列
- 支持编辑内存值（写入后同步到 vCPU 内存）
- 书签功能：标记重要内存区域

### 12.3 GPU Profiler

- Draw Call 统计（每帧 draw call 数、三角形数）
- 纹理内存追踪（当前绑定纹理、总纹理占用）
- Shader 编译统计（缓存命中率、编译耗时）
- 帧时间图（最近 300 帧的帧时间曲线）

---

## 13. 网络模拟

### 13.1 Phase 1：nifm 基础服务

模拟 `nifm:` 服务接口，让 NRO 程序认为设备有网络连接：

```csharp
public class NifmService : IIpcService
{
    // 返回网络状态：已连接、WiFi 模拟
    public ResultCode GetInternetConnectionStatus(out InternetConnectionStatus status);
    
    // 返回模拟 IP 地址 (192.168.1.100)
    public ResultCode GetCurrentIpAddress(out IpAddress addr);
}
```

### 13.2 Phase 2：BSD Socket 转发

将 Switch 的 BSD socket 调用映射到 macOS 真实 socket：
- `bsd_socket` → `socket()`
- `bsd_connect` → `connect()`
- `bsd_send` / `bsd_recv` → `send()` / `recv()`
- DNS 查询转发到 macOS resolver
- NRO 可实际联网（如 FTP 服务器、HTTP 客户端）

---

## 14. 存档状态 (Save State)

### 14.1 功能规格

```csharp
public class SaveStateManager
{
    /// <summary>保存当前完整模拟器状态</summary>
    SaveState CaptureState();
    
    /// <summary>恢复到先前保存的状态</summary>
    void RestoreState(SaveState state);
    
    /// <summary>保存到磁盘</summary>
    void SaveToFile(string path, SaveState state);
    
    /// <summary>从磁盘加载</summary>
    SaveState LoadFromFile(string path);
    
    /// <summary>可用存档位数量</summary>
    const int SlotCount = 10;
}
```

### 14.2 保存内容

Save State 需要捕获的完整状态：

| 组件 | 保存内容 |
|------|---------|
| CPU | 所有通用寄存器 (X0-X30)、SP、PC、PSTATE |
| Memory | 所有已映射页的完整内容 |
| GPU | 当前帧缓冲、纹理状态、渲染管线状态 |
| HLE | IPC 端口状态、服务内部状态 |
| Input | 手柄/触屏当前状态 |
| Audio | 音频缓冲区内容 |
| Timers | 所有内核定时器剩余时间 |

预估单次 Save State 大小：50-200MB（取决于内存占用量）。M1 SSD 写入速度 > 2GB/s，保存/恢复约 0.1-0.5 秒。

---

## 15. 开发阶段规划

### Phase 1：骨架搭建 + Hello NRO

**目标**：能在屏幕上运行最简单的 NRO（如 hello-world）

| 任务 | 预估复杂度 |
|------|-----------|
| 创建解决方案和所有项目骨架 | 低 |
| `SwitchNro.Common`：日志、配置基础 | 低 |
| `SwitchNro.Memory`：虚拟内存管理器 + 按需分页 | 高 |
| `SwitchNro.Cpu`：Hypervisor 执行引擎 (P/Invoke HVF) | 高 |
| `SwitchNro.Cpu`：SVC 拦截和分发框架 | 中 |
| `SwitchNro.NroLoader`：NRO 解析、映射、重定位 | 中 |
| `SwitchNro.Horizon`：进程管理基础 | 中 |
| `SwitchNro.HLE`：sm: 服务（最小实现） | 中 |
| `SwitchNro.UI`：Avalonia 主窗口 + Metal 控件占位 | 中 |
| 集成测试：加载 hello-world NRO 并执行到 SVC | 高 |

### Phase 2：核心服务 + 图形输出

**目标**：能运行带图形输出的 2D NRO

| 任务 | 预估复杂度 |
|------|-----------|
| `SwitchNro.HLE`：fs: (RomFS + SDCard) | 高 |
| `SwitchNro.HLE`：vi: (显示管理) | 中 |
| `SwitchNro.HLE`：am: (应用管理) | 中 |
| `SwitchNro.HLE`：hid: (输入设备) | 中 |
| `SwitchNro.HLE`：audout: (音频输出) | 中 |
| `SwitchNro.Graphics.GAL`：图形抽象层 | 高 |
| `SwitchNro.Graphics.Metal`：Metal 渲染后端 | 高 |
| `SwitchNro.Graphics.Shader`：Shader 编译器移植 + MSL 输出 | 极高 |
| `SwitchNro.Graphics.Texture`：纹理管理 + ASTC | 高 |
| `SwitchNro.Input`：键盘 + 鼠标输入 | 低 |
| `SwitchNro.Audio.SDL2`：SDL2 音频后端 | 低 |
| `SwitchNro.UI`：MetalGameControl 渲染嵌入 | 中 |

### Phase 3：完整体验 + 调试

**目标**：广泛兼容 NRO + 完善调试体验

| 任务 | 预估复杂度 |
|------|-----------|
| `SwitchNro.HLE`：扩展服务 (nifm/time/set/ssl 等) | 高 |
| `SwitchNro.HLE`：多进程和 Applet 支持 | 极高 |
| `SwitchNro.Graphics.Vulkan`：MoltenVK 备选后端 | 高 |
| `SwitchNro.Audio.CoreAudio`：低延迟音频后端 | 中 |
| `SwitchNro.Input`：蓝牙手柄支持 | 中 |
| `SwitchNro.Debugger`：断点/内存查看/GPU Profiler | 高 |
| `SwitchNro.UI`：调试面板 UI | 中 |
| Save State 功能实现 | 高 |
| BSD Socket 转发 | 中 |

### Phase 4：优化 + 稳定性

**目标**：性能和稳定性打磨

| 任务 | 预估复杂度 |
|------|-----------|
| 内存压力优化（< 4GB 常驻目标） | 高 |
| Shader 缓存命中率优化 | 中 |
| Metal 渲染管线优化 | 高 |
| Hypervisor 稳定性加固 | 高 |
| JIT 回退引擎实现 | 极高 |
| 兼容性测试（常见 homebrew 验证集） | 中 |
| 文档和用户指南 | 低 |

---

## 16. 验证策略

### 16.1 验证基准：常见 Homebrew

以下 NRO 程序作为兼容性验证基准：

| 程序 | 类型 | 验证重点 |
|------|------|---------|
| **hbmenu** (Homebrew Menu) | 系统工具 | NRO 加载、文件系统、输入 |
| **hello-world** | 最简程序 | 基础执行、SVC 处理 |
| **switch-webkit** | Web 浏览器 | 网络服务、图形渲染 |
| **Switch-Fi** | 网络工具 | nifm 服务、BSD socket |
| **FTPD** | FTP 服务器 | 网络通信、文件系统 |
| **Checkpoint** | 存档管理 | 文件系统、UI 渲染 |
| **Homebrew App Store** | 应用商店 | 网络下载、RomFS |
| **2048-game** | 2D 游戏 | 触屏输入、图形渲染 |
| **vapecar** | 3D 游戏原型 | GPU 渲染、Shader 编译 |

### 16.2 性能指标

| 指标 | 目标 | 可接受 |
|------|------|--------|
| 帧率 (2D NRO) | 60fps | ≥ 30fps |
| 帧率 (3D NRO) | 30fps | ≥ 15fps |
| 常驻内存 | < 3GB | < 4GB |
| Swap 使用 | 0 GB | < 0.5GB |
| NRO 启动时间 | < 2s | < 5s |
| Shader 缓存命中率 | > 95% | > 80% |
| 音频延迟 | < 50ms | < 100ms |
| Save State 时间 | < 0.5s | < 2s |

---

## 17. 技术依赖

### 17.1 NuGet 包依赖

| 包 | 用途 | 版本 |
|----|------|------|
| `Avalonia` | UI 框架 | 11.x / 12.x |
| `Avalonia.Desktop` | 桌面平台支持 | 匹配 Avalonia |
| `Avalonia.Themes.Fluent` | Fluent 主题 | 匹配 Avalonia |
| `Silk.NET.SDL` | SDL2 绑定 (输入/音频) | 最新 |
| `Silk.NET.Metal` | Metal API 绑定 | 最新 |
| `Silk.NET.Vulkan` | Vulkan API 绑定 (MoltenVK) | 最新 |
| `System.Runtime.Intrinsics` | SIMD/硬件内联函数 | .NET 10 内置 |
| `CommunityToolkit.HighPerformance` | 高性能工具 (Span2D 等) | 最新 |
| `SharpZipLib` | 压缩/解压 (LZ4 等) | 最新 |

### 17.2 系统框架依赖

| 框架 | 用途 | 备注 |
|------|------|------|
| `Hypervisor.framework` | CPU 直接执行 | macOS 内置，M1 原生支持 |
| `Metal.framework` | GPU 渲染 | macOS 内置，M1 原生支持 |
| `CoreBluetooth.framework` | 蓝牙手柄 | 可选，SDL2 替代 |
| `CoreAudio.framework` | 低延迟音频 | 可选，SDL2 替代 |
| `MoltenVK` | Vulkan → Metal 转译 | 需单独安装或嵌入 |

### 17.3 开发工具

| 工具 | 用途 |
|------|------|
| .NET 10 SDK | 编译和构建 |
| devkitPro / devkitA64 | 编译测试用 NRO |
| Metal Performance HUD | GPU 性能监控 |
| Instruments (Xcode) | CPU/内存 Profiling |
| Tracy Profiler | 帧级性能分析（可选） |

---

## 18. 风险与缓解

| 风险 | 影响 | 缓解措施 |
|------|------|---------|
| Hypervisor.framework 限制 | macOS 更新可能限制 HVF 的非虚拟机使用 | 维护 JIT 回退引擎 |
| Shader 兼容性 | 部分 Maxwell shader 特性难以转译为 MSL | 逐个攻克 + 着色器缓存避免重复编译 |
| 8GB 内存瓶颈 | 复杂 NRO 可能内存不足 | 按需分页 + 积极页面回收 + 内存压力监测 |
| HLE 工作量巨大 | 60+ 个 IPC 服务接口 | 核心必选先行 + 按需扩展 + stub 未实现服务 |
| Metal 嵌入 Avalonia 复杂 | 两个渲染管线协同可能有冲突 | 共享 MTLDevice + 帧率同步 |
| NRO 动态加载模块 | 少见但有，实现复杂 | Phase 3 实现，先支持标准 NRO |
| Applet 多进程 | 架构复杂度高 | 大多数 NRO 不需要，Phase 3 实现 |

---

## 19. 配置文件格式

模拟器配置存储在 `~/.switchnro/config.json`：

```json
{
  "version": 1,
  "graphics": {
    "backend": "Metal",
    "vsync": true,
    "astcHardwareDecode": true,
    "shaderCachePath": "~/.switchnro/shader_cache"
  },
  "audio": {
    "backend": "SDL2",
    "volume": 1.0,
    "latencyMs": 50
  },
  "input": {
    "keyboardMapping": { "A": "X", "B": "Z", ... },
    "mouseAsTouch": true,
    "bluetoothGamepad": true
  },
  "filesystem": {
    "sdCardPath": "~/.switchnro/sdcard",
    "saveDataPath": "~/.switchnro/save"
  },
  "debug": {
    "logLevel": "Info",
    "svcLogging": false,
    "ipcLogging": false
  },
  "memory": {
    "maxResidentGb": 3.5,
    "enablePageReclaim": true
  }
}
```

---

## 20. 总结

本项目旨在从零构建一个专为 Mac M1 8GB 优化的 Switch NRO 模拟器，核心亮点：

1. **Hypervisor 直接执行**：利用 ARM64 → ARM64 同构优势，M1 上接近原生速度
2. **Metal 原生渲染**：零拷贝统一内存 + ASTC 硬解，最大化 M1 GPU 性能
3. **按需分页内存管理**：8GB 约束下的最优内存策略
4. **渐进式 HLE**：核心服务优先 + 按需扩展，控制开发风险
5. **完整调试体验**：断点/内存/GPU Profiler 一体化
6. **Save State**：随时保存和恢复模拟器完整状态
7. **模块化架构**：CPU/GPU/HLE/Memory 独立项目，清晰可维护
