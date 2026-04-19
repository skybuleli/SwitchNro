using SwitchNro.Cpu;

namespace SwitchNro.Tests.TestUtilities;

/// <summary>
/// 轻量级 IExecutionEngine 存根，用于单元测试
/// 避免对 Apple Hypervisor.framework 的依赖
/// </summary>
public sealed class StubExecutionEngine : IExecutionEngine
{
    private ulong _pc;
    private ulong _sp;
    private ulong _tpidrEl0;
    private ulong _tpidrroEl0;
    private readonly ulong[] _regs = new ulong[31];

    public ExecutionMode Mode => ExecutionMode.Hypervisor;
    public bool IsRunning => false;

    public ExecutionResult Execute(ulong entryPoint) => ExecutionResult.NormalExit;
    public ExecutionResult RunNext() => ExecutionResult.NormalExit;
    public void Pause() { }
    public void RequestExit() { }

    public ulong GetRegister(int index) => index >= 0 && index < 31 ? _regs[index] : 0;
    public void SetRegister(int index, ulong value) { if (index >= 0 && index < 31) _regs[index] = value; }

    public ulong GetPC() => _pc;
    public void SetPC(ulong value) => _pc = value;

    public ulong GetSP() => _sp;
    public void SetSP(ulong value) => _sp = value;

    public SvcInfo GetLastSvcInfo() => default;

    public void SetSvcResult(ulong returnValue) { }
    public void SetSvcResult(ulong returnValue0, ulong returnValue1) { }
    public void SetSvcResult(ulong returnValue0, ulong returnValue1, ulong returnValue2) { }

    public ulong GetPstate() => 0;
    public void SetPstate(ulong value) { }

    public ulong GetTpidrEl0() => _tpidrEl0;
    public void SetTpidrEl0(ulong value) => _tpidrEl0 = value;

    public ulong GetTpidrroEl0() => _tpidrroEl0;
    public void SetTpidrroEl0(ulong value) => _tpidrroEl0 = value;

    public void Dispose() { }
}
