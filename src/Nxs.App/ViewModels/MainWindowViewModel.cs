using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Diagnostics;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Simulator;

namespace Nxs.App.ViewModels;

/// <summary>메인 윈도우 뷰모델 — 랙 패널 + 서버 제어 + 프로젝트 입출력.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Func<PlcMemory, IFrameCodec>? _codecFactory;
    private readonly TrafficLog _trafficLog;

    [ObservableProperty]
    private bool _showErrorsOnly;

    [ObservableProperty]
    private bool _isTrafficPaused;

    [ObservableProperty]
    private string _bindAddressText = "0.0.0.0";

    [ObservableProperty]
    private string _portText = "2004";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _projectPath = string.Empty;

    /// <summary>뷰모델을 만든다.</summary>
    /// <param name="project">초기 프로젝트. null이면 CONTEXT 기재 기본 랙.</param>
    /// <param name="codecFactory">
    /// FEnet 코덱 팩토리. 운영에서는 null이다 — spec 근거가 없어 코덱이 존재하지 않기 때문(⛔ M2).
    /// 테스트는 합성 코덱을 주입해 e2e 스모크를 수행한다.
    /// </param>
    /// <param name="trafficLog">트래픽 로그. null이면 새로 만든다.</param>
    public MainWindowViewModel(
        NxpProject? project = null,
        Func<PlcMemory, IFrameCodec>? codecFactory = null,
        TrafficLog? trafficLog = null)
    {
        _codecFactory = codecFactory;
        _trafficLog = trafficLog ?? new TrafficLog();
        LoadProject(project ?? NxpProject.CreateDefault(port: 2004));
    }

    /// <summary>트래픽 로그 (PRD X-07).</summary>
    public TrafficLog TrafficLog => _trafficLog;

    /// <summary>현재 엔진.</summary>
    public SimulatorEngine Engine { get; private set; } = null!;

    /// <summary>랙 슬롯 목록(빈 슬롯·통신 모듈 포함).</summary>
    public ObservableCollection<SlotViewModel> Slots { get; } = [];

    /// <summary>사용자가 조작하는 디지털 입력 슬롯.</summary>
    public ObservableCollection<SlotViewModel> InputSlots { get; } = [];

    /// <summary>마스터가 쓰는 디지털 출력 슬롯.</summary>
    public ObservableCollection<SlotViewModel> OutputSlots { get; } = [];

    /// <summary>AD 슬롯.</summary>
    public ObservableCollection<SlotViewModel> AnalogSlots { get; } = [];

    /// <summary>자동화 룰 목록.</summary>
    public ObservableCollection<AutomationRuleViewModel> AutomationRules { get; } = [];

    /// <summary>트래픽 로그 행.</summary>
    public ObservableCollection<TrafficRowViewModel> TrafficRows { get; } = [];

    /// <summary>자동화 루프 실행 여부.</summary>
    public bool IsAutomationRunning => Engine.IsAutomationRunning;

    /// <summary>자동화 시작/정지 버튼 문구.</summary>
    public string AutomationToggleLabel => Engine.IsAutomationRunning ? "자동화 정지" : "자동화 시작";

    /// <summary>자동화 룰이 하나라도 있는지.</summary>
    public bool HasAutomationRules => AutomationRules.Count > 0;

    /// <summary>트래픽 로그 요약.</summary>
    public string TrafficSummary
    {
        get
        {
            var dropped = _trafficLog.DroppedCount;
            var text = $"{_trafficLog.Count}건 · 오류 {_trafficLog.ErrorCount}건";
            return dropped > 0 ? $"{text} · 용량 초과로 {dropped}건 버림" : text;
        }
    }

    /// <summary>서버 실행 여부.</summary>
    public bool IsServerRunning => Engine.IsServerRunning;

    /// <summary>서버를 켤 수 있는지.</summary>
    public bool CanStartServer => Engine.CanStartServer;

    /// <summary>서버를 켤 수 없는 이유(⛔ spec 게이트 안내). 켤 수 있으면 null.</summary>
    public string? ServerUnavailableReason => Engine.ServerUnavailableReason;

    /// <summary>게이트 안내 배너를 보일지.</summary>
    public bool ShowServerUnavailableNotice => !CanStartServer;

    /// <summary>접속 상태 필 문구.</summary>
    public string ServerStatusText => Engine.IsServerRunning
        ? $"수신 중 {Engine.LocalEndPoint} · 접속 {Engine.ConnectedClientCount}"
        : CanStartServer ? "정지" : "사용 불가";

    /// <summary>시작/정지 버튼 문구.</summary>
    public string StartStopLabel => Engine.IsServerRunning ? "정지" : "시작";

    /// <summary>랙 요약 문구.</summary>
    public string RackSummary
        => $"XGI · 베이스 0 · 슬롯당 {Engine.Io.Addressing.SlotPoints}점 · 매핑 {Engine.Map.Count}개 모듈";

    /// <summary>서버를 시작하거나 정지한다.</summary>
    [RelayCommand]
    private async Task ToggleServerAsync()
    {
        ErrorMessage = null;
        try
        {
            if (Engine.IsServerRunning)
            {
                await Engine.StopServerAsync();
                StatusMessage = "서버를 정지했습니다.";
            }
            else
            {
                if (!int.TryParse(PortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
                {
                    ErrorMessage = "포트가 숫자가 아닙니다.";
                    return;
                }

                Engine.ServerSettings = new ServerSettings { BindAddress = BindAddressText, Port = port };
                await Engine.StartServerAsync();
                StatusMessage = $"수신 시작 {Engine.LocalEndPoint}";
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or FormatException or ArgumentException)
        {
            ErrorMessage = ex.Message;
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            ErrorMessage = $"바인딩 실패: {ex.Message}";
        }

        NotifyServerState();
    }

    /// <summary>자동화 루프를 시작하거나 정지한다.</summary>
    [RelayCommand]
    private async Task ToggleAutomationAsync()
    {
        ErrorMessage = null;
        if (Engine.IsAutomationRunning)
        {
            await Engine.StopAutomationAsync();
            StatusMessage = "자동화를 정지했습니다.";
        }
        else if (AutomationRules.Count == 0)
        {
            ErrorMessage = "자동화 룰이 없습니다 — .nxp 의 automationRules 절에 룰을 추가하십시오.";
        }
        else
        {
            Engine.StartAutomation();
            StatusMessage = $"자동화를 시작했습니다 ({AutomationRules.Count}개 룰).";
        }

        OnPropertyChanged(nameof(IsAutomationRunning));
        OnPropertyChanged(nameof(AutomationToggleLabel));
    }

    /// <summary>트래픽 로그를 비운다.</summary>
    [RelayCommand]
    private void ClearTraffic()
    {
        _trafficLog.Clear();
        TrafficRows.Clear();
        OnPropertyChanged(nameof(TrafficSummary));
        StatusMessage = "트래픽 로그를 비웠습니다.";
    }

    /// <summary>트래픽 로그를 파일로 저장한다.</summary>
    public void SaveTraffic(string path)
    {
        ErrorMessage = null;
        try
        {
            _trafficLog.Save(path, ShowErrorsOnly);
            StatusMessage = $"트래픽 로그를 저장했습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ErrorMessage = $"로그 저장 실패: {ex.Message}";
        }
    }

    /// <summary>프로젝트를 파일에서 불러온다.</summary>
    public void OpenProject(string path)
    {
        ErrorMessage = null;
        try
        {
            var project = NxpProjectFile.Load(path);
            LoadProject(project);
            ProjectPath = path;
            StatusMessage = $"프로젝트를 불러왔습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is NxpFormatException or IOException or IoConfigurationException or FormatException)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>현재 상태를 프로젝트 파일로 저장한다.</summary>
    public void SaveProject(string path)
    {
        ErrorMessage = null;
        try
        {
            NxpProjectFile.Save(path, BuildProjectSnapshot());
            ProjectPath = path;
            StatusMessage = $"프로젝트를 저장했습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex) when (ex is IOException or IoConfigurationException or FormatException)
        {
            ErrorMessage = ex.Message;
        }
    }

    /// <summary>
    /// 현재 메모리 상태를 표시에 반영한다. UI 타이머와 테스트가 호출한다.
    /// 메모리 읽기만 하므로 블로킹하지 않는다.
    /// </summary>
    public void Refresh()
    {
        foreach (var slot in Slots)
        {
            slot.Refresh();
        }

        RefreshTraffic();
        NotifyServerState();
        OnPropertyChanged(nameof(IsAutomationRunning));
        OnPropertyChanged(nameof(AutomationToggleLabel));
    }

    /// <summary>트래픽 로그 표시를 갱신한다.</summary>
    public void RefreshTraffic()
    {
        if (IsTrafficPaused)
        {
            return;
        }

        // 로그는 링 버퍼이므로 전체 스냅샷을 다시 투사한다 — 표시 상한을 두어 UI 부담을 막는다.
        const int displayLimit = 500;
        var events = _trafficLog.Snapshot(ShowErrorsOnly);
        var visible = events.Count > displayLimit ? events.Skip(events.Count - displayLimit) : events;

        TrafficRows.Clear();
        foreach (var e in visible)
        {
            TrafficRows.Add(new TrafficRowViewModel(e));
        }

        OnPropertyChanged(nameof(TrafficSummary));
    }

    /// <summary>종료 시 서버를 정리한다.</summary>
    public void Shutdown() => Engine.Dispose();

    /// <summary>현재 UI 상태를 프로젝트 문서로 스냅샷한다(입력 점·AD 채널 값을 초기값으로 담는다).</summary>
    public NxpProject BuildProjectSnapshot()
    {
        var initial = new List<InitialValue>();

        foreach (var slot in InputSlots)
        {
            foreach (var point in slot.Points.Where(p => p.IsOn))
            {
                initial.Add(new InitialValue { Address = point.AddressText, Value = 1 });
            }
        }

        foreach (var slot in AnalogSlots)
        {
            foreach (var channel in slot.Channels)
            {
                var raw = Engine.Memory.ReadScalar(channel.Address);
                if (raw != 0)
                {
                    initial.Add(new InitialValue { Address = channel.AddressText, Value = raw });
                }
            }
        }

        return Engine.Project with
        {
            Server = new ServerSettings
            {
                BindAddress = BindAddressText,
                Port = int.TryParse(PortText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p)
                    ? p
                    : Engine.Project.Server.Port,
            },
            InitialValues = initial,
            AutomationRules = AutomationRules
                .Select(vm => AutomationRuleSettings.FromRule(vm.Rule with { IsEnabled = vm.IsEnabled }))
                .ToArray(),
        };
    }

    private void LoadProject(NxpProject project)
    {
        Engine?.Dispose();
        Engine = new SimulatorEngine(project, _codecFactory, trafficSink: _trafficLog);

        BindAddressText = project.Server.BindAddress;
        PortText = project.Server.Port.ToString(CultureInfo.InvariantCulture);

        Slots.Clear();
        InputSlots.Clear();
        OutputSlots.Clear();
        AnalogSlots.Clear();

        var declared = project.Io.Bases
            .SelectMany(b => b.Slots.Select(s => (Base: b.BaseNumber, Slot: s)))
            .OrderBy(x => x.Base)
            .ThenBy(x => x.Slot.SlotNumber);

        foreach (var (baseNumber, slot) in declared)
        {
            var mapping = Engine.Map.FirstOrDefault(
                m => m.BaseNumber == baseNumber && m.SlotNumber == slot.SlotNumber);

            var vm = mapping is null
                ? new SlotViewModel(slot.SlotNumber, slot.Module)
                : new SlotViewModel(Engine.Memory, mapping, project);

            Slots.Add(vm);
            if (vm.IsInputSlot)
            {
                InputSlots.Add(vm);
            }
            else if (vm.IsOutputSlot)
            {
                OutputSlots.Add(vm);
            }
            else if (vm.HasChannels)
            {
                AnalogSlots.Add(vm);
            }
        }

        AutomationRules.Clear();
        for (var i = 0; i < Engine.Automation.Rules.Count; i++)
        {
            var index = i;
            var ruleVm = new AutomationRuleViewModel(Engine.Automation.Rules[index]);
            ruleVm.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AutomationRuleViewModel.IsEnabled))
                {
                    Engine.Automation.SetEnabled(index, ruleVm.IsEnabled);
                }
            };
            AutomationRules.Add(ruleVm);
        }

        OnPropertyChanged(nameof(RackSummary));
        OnPropertyChanged(nameof(HasAutomationRules));
        OnPropertyChanged(nameof(IsAutomationRunning));
        OnPropertyChanged(nameof(AutomationToggleLabel));
        NotifyServerState();
    }

    private void NotifyServerState()
    {
        OnPropertyChanged(nameof(IsServerRunning));
        OnPropertyChanged(nameof(CanStartServer));
        OnPropertyChanged(nameof(ServerUnavailableReason));
        OnPropertyChanged(nameof(ShowServerUnavailableNotice));
        OnPropertyChanged(nameof(ServerStatusText));
        OnPropertyChanged(nameof(StartStopLabel));
    }
}
