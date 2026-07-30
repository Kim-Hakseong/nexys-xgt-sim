using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Diagnostics;
using Nxs.Core.Fixtures;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Protocol.Xgt;
using Nxs.Core.Simulator;

namespace Nxs.App.ViewModels;

/// <summary>메인 윈도우 뷰모델 — 랙 패널 + 서버 제어 + 프로젝트 입출력.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly Func<PlcMemory, IFrameCodec>? _codecFactory;
    private readonly TrafficLog _trafficLog;
    private readonly FrameRecorder? _frameRecorder;

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
    /// <param name="frameRecorder">수신 프레임 자동 캡처기.</param>
    public MainWindowViewModel(
        NxpProject? project = null,
        Func<PlcMemory, IFrameCodec>? codecFactory = null,
        TrafficLog? trafficLog = null,
        FrameRecorder? frameRecorder = null)
    {
        _codecFactory = codecFactory;
        _trafficLog = trafficLog ?? new TrafficLog();
        _frameRecorder = frameRecorder;
        LoadProject(project ?? NxpProject.CreateDefault(port: XgtFenetOptions.DefaultPort));
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

    /// <summary>사용자 지정 워치 목록.</summary>
    public ObservableCollection<WatchRowViewModel> Watches { get; } = [];

    /// <summary>워치가 하나라도 있는지.</summary>
    public bool HasWatches => Watches.Count > 0;

    /// <summary>
    /// 사용자 지정 디지털 그룹. 입력/출력을 나누지 않는다 — 모든 점이 양방향이다.
    /// </summary>
    public ObservableCollection<DigitalPointGroupViewModel> DigitalGroups { get; } = [];

    /// <summary>디지털 그룹이 있는지.</summary>
    public bool HasDigitalGroups => DigitalGroups.Count > 0;

    /// <summary>사용자 지정 A/D 채널.</summary>
    public ObservableCollection<AnalogPointViewModel> AnalogPoints { get; } = [];

    /// <summary>A/D 채널이 있는지.</summary>
    public bool HasAnalogPoints => AnalogPoints.Count > 0;

    /// <summary>
    /// 접속 표시등 상태 — 정지 / 수신 중(미접속) / 접속됨.
    /// </summary>
    /// <remarks>
    /// 마스터가 실제로 붙었는지를 한눈에 보려면 "수신 중"과 "접속됨"을 구분해야 한다.
    /// 접속됨이면 초록불이 들어온다.
    /// </remarks>
    public bool IsClientConnected => Engine.IsServerRunning && Engine.ConnectedClientCount > 0;

    /// <summary>수신 중이지만 아직 접속이 없는 상태.</summary>
    public bool IsListeningWithoutClient => Engine.IsServerRunning && Engine.ConnectedClientCount == 0;

    /// <summary>접속 표시등 옆 문구.</summary>
    public string ConnectionText => !Engine.IsServerRunning
        ? "정지"
        : Engine.ConnectedClientCount > 0
            ? $"접속 {Engine.ConnectedClientCount}"
            : "대기 중";

    /// <summary>자동 캡처된 프레임 수 안내.</summary>
    public string CaptureSummary => _frameRecorder is null
        ? "프레임 자동 캡처 꺼짐"
        : $"프레임 자동 캡처 {_frameRecorder.SavedCount}건 → fixtures/labview-capture/";

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

    /// <summary>새 워치 주소 입력값.</summary>
    [ObservableProperty]
    private string _newWatchAddress = string.Empty;

    /// <summary>새 워치 별칭 입력값.</summary>
    [ObservableProperty]
    private string _newWatchLabel = string.Empty;

    /// <summary>워치 목록에 주소를 추가한다.</summary>
    [RelayCommand]
    private void AddWatch()
    {
        ErrorMessage = null;
        var address = AddressInput.Normalize(NewWatchAddress);

        if (!WatchEntry.IsValid(address))
        {
            ErrorMessage = $"주소를 해석할 수 없습니다 — {AddressInput.Describe(NewWatchAddress)}. " +
                "형식: %<영역 I/Q/M><크기 X/B/W/D/L><번지> (예: %MW0 · %MW320 · %MD422 · %MX801)";
            return;
        }

        var entry = new WatchEntry { Address = address, Label = NewWatchLabel.Trim() };
        var resolved = entry.Resolve(Engine.Io.Addressing);

        if (Watches.Any(w => w.Address.Area == resolved.Area
            && w.Address.Size == resolved.Size
            && w.Address.Offset == resolved.Offset))
        {
            ErrorMessage = $"'{resolved.Text}' 는 이미 목록에 있습니다.";
            return;
        }

        try
        {
            Watches.Add(new WatchRowViewModel(Engine.Memory, entry, Engine.Io.Addressing, RemoveWatchRow));
        }
        catch (AddressRangeException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        NewWatchAddress = string.Empty;
        NewWatchLabel = string.Empty;
        OnPropertyChanged(nameof(HasWatches));
        StatusMessage = $"워치 추가: {resolved.Text}";
    }

    /// <summary>새 디지털 점 주소.</summary>
    [ObservableProperty]
    private string _newDigitalAddress = string.Empty;

    /// <summary>새 디지털 점 별칭.</summary>
    [ObservableProperty]
    private string _newDigitalLabel = string.Empty;

    /// <summary>디지털 그룹을 추가한다. 모든 점이 양방향이다.</summary>
    [RelayCommand]
    private void AddDigitalPoint()
    {
        ErrorMessage = null;
        var address = AddressInput.Normalize(NewDigitalAddress);

        if (!DigitalPointEntry.IsValid(address))
        {
            ErrorMessage = $"주소를 해석할 수 없습니다 — {AddressInput.Describe(NewDigitalAddress)}. " +
                "형식: %<영역 I/Q/M><크기 X/B/W/D/L><번지> (예: %MX801 · %MB40 · %MW0 · %MD422)";
            return;
        }

        var entry = new DigitalPointEntry { Address = address, Label = NewDigitalLabel.Trim() };
        var resolved = entry.Resolve(Engine.Io.Addressing);

        if (DigitalGroups.Any(g => g.Address.Area == resolved.Area
            && g.Address.Size == resolved.Size
            && g.Address.Offset == resolved.Offset))
        {
            ErrorMessage = $"'{resolved.Text}' 는 이미 목록에 있습니다.";
            return;
        }

        DigitalPointGroupViewModel group;
        try
        {
            group = new DigitalPointGroupViewModel(
                Engine.Memory, entry, Engine.Io.Addressing, RemoveDigitalPoint);
        }
        catch (AddressRangeException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        DigitalGroups.Add(group);
        NewDigitalAddress = string.Empty;
        NewDigitalLabel = string.Empty;
        OnPropertyChanged(nameof(HasDigitalGroups));
        StatusMessage = group.IsArray
            ? $"{resolved.Text} 추가 — 비트 {group.BitCount}개로 펼쳤습니다 (양방향)"
            : $"{resolved.Text} 추가 (양방향)";
    }

    /// <summary>디지털 그룹을 제거한다(그룹의 제거 커맨드가 호출한다).</summary>
    public void RemoveDigitalPoint(DigitalPointGroupViewModel? group)
    {
        if (group is not null && DigitalGroups.Remove(group))
        {
            OnPropertyChanged(nameof(HasDigitalGroups));
            StatusMessage = $"제거: {group.AddressText}";
        }
    }

    /// <summary>새 A/D 채널 주소.</summary>
    [ObservableProperty]
    private string _newAnalogAddress = string.Empty;

    /// <summary>새 A/D 채널 별칭.</summary>
    [ObservableProperty]
    private string _newAnalogLabel = string.Empty;

    /// <summary>새 A/D 채널 raw 최소값.</summary>
    [ObservableProperty]
    private string _newAnalogRawMin = "0";

    /// <summary>새 A/D 채널 raw 최대값.</summary>
    [ObservableProperty]
    private string _newAnalogRawMax = "4000";

    /// <summary>새 A/D 채널 공학단위 최소값.</summary>
    [ObservableProperty]
    private string _newAnalogEuMin = "0";

    /// <summary>새 A/D 채널 공학단위 최대값.</summary>
    [ObservableProperty]
    private string _newAnalogEuMax = "10";

    /// <summary>새 A/D 채널 단위 표기.</summary>
    [ObservableProperty]
    private string _newAnalogUnit = "V";

    /// <summary>A/D 채널을 추가한다.</summary>
    [RelayCommand]
    private void AddAnalogPoint()
    {
        ErrorMessage = null;
        var address = AddressInput.Normalize(NewAnalogAddress);

        if (!AnalogPointEntry.IsValid(address))
        {
            ErrorMessage = $"주소를 해석할 수 없습니다 — {AddressInput.Describe(NewAnalogAddress)}. " +
                "비트(%..X)는 아날로그로 쓸 수 없습니다 (예: %IW80 · %MW0 · %MD100)";
            return;
        }

        if (!TryParseScale(out var scale))
        {
            return;
        }

        var entry = new AnalogPointEntry
        {
            Address = address, Label = NewAnalogLabel.Trim(), Scale = scale,
        };
        var resolved = entry.Resolve(Engine.Io.Addressing);

        if (AnalogPoints.Any(p => p.Address.Area == resolved.Area
            && p.Address.Size == resolved.Size
            && p.Address.Offset == resolved.Offset))
        {
            ErrorMessage = $"'{resolved.Text}' 는 이미 목록에 있습니다.";
            return;
        }

        try
        {
            AnalogPoints.Add(new AnalogPointViewModel(
                Engine.Memory, entry, Engine.Io.Addressing, RemoveAnalogPoint));
        }
        catch (AddressRangeException ex)
        {
            ErrorMessage = ex.Message;
            return;
        }

        NewAnalogAddress = string.Empty;
        NewAnalogLabel = string.Empty;
        OnPropertyChanged(nameof(HasAnalogPoints));
        StatusMessage = $"A/D 채널 추가: {resolved.Text}";
    }

    private bool TryParseScale(out AnalogChannelScale scale)
    {
        scale = AnalogChannelScale.Default;

        if (!int.TryParse(NewAnalogRawMin, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawMin)
            || !int.TryParse(NewAnalogRawMax, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawMax))
        {
            ErrorMessage = "raw 범위가 정수가 아닙니다.";
            return false;
        }

        if (!double.TryParse(NewAnalogEuMin, NumberStyles.Float, CultureInfo.InvariantCulture, out var euMin)
            || !double.TryParse(NewAnalogEuMax, NumberStyles.Float, CultureInfo.InvariantCulture, out var euMax))
        {
            ErrorMessage = "공학단위 범위가 숫자가 아닙니다.";
            return false;
        }

        if (rawMin == rawMax)
        {
            ErrorMessage = "raw 범위 폭이 0입니다.";
            return false;
        }

        if (euMin == euMax)
        {
            ErrorMessage = "공학단위 범위 폭이 0입니다.";
            return false;
        }

        scale = new AnalogChannelScale
        {
            RawMin = rawMin,
            RawMax = rawMax,
            EngineeringMin = euMin,
            EngineeringMax = euMax,
            Unit = NewAnalogUnit.Trim(),
        };
        return true;
    }

    /// <summary>A/D 채널을 제거한다.</summary>
    public void RemoveAnalogPoint(AnalogPointViewModel? point)
    {
        if (point is not null && AnalogPoints.Remove(point))
        {
            OnPropertyChanged(nameof(HasAnalogPoints));
            StatusMessage = $"A/D 채널 제거: {point.AddressText}";
        }
    }

    /// <summary>워치 항목을 제거한다(행의 제거 커맨드가 호출한다).</summary>
    public void RemoveWatchRow(WatchRowViewModel? row)
    {
        if (row is not null && Watches.Remove(row))
        {
            OnPropertyChanged(nameof(HasWatches));
            StatusMessage = $"워치 제거: {row.AddressText}";
        }
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

        foreach (var watch in Watches)
        {
            watch.Refresh();
        }

        foreach (var group in DigitalGroups)
        {
            group.Refresh();
        }

        foreach (var point in AnalogPoints)
        {
            point.Refresh();
        }

        RefreshTraffic();
        OnPropertyChanged(nameof(CaptureSummary));
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

        foreach (var bit in DigitalGroups.SelectMany(g => g.Bits).Where(b => b.IsOn))
        {
            initial.Add(new InitialValue { Address = bit.AddressText, Value = 1 });
        }

        foreach (var point in AnalogPoints)
        {
            var raw = Engine.Memory.ReadScalar(point.Address);
            if (raw != 0)
            {
                initial.Add(new InitialValue { Address = point.AddressText, Value = raw });
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
            Watches = Watches.Select(w => w.ToEntry()).ToArray(),
            DigitalPoints = DigitalGroups.Select(g => g.Entry).ToArray(),
            AnalogPoints = AnalogPoints.Select(p => p.ToEntry()).ToArray(),
        };
    }

    private void LoadProject(NxpProject project)
    {
        Engine?.Dispose();
        Engine = new SimulatorEngine(
            project, _codecFactory, trafficSink: _trafficLog, frameRecorder: _frameRecorder);

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

        Watches.Clear();
        foreach (var entry in project.Watches)
        {
            try
            {
                Watches.Add(new WatchRowViewModel(Engine.Memory, entry, project.Io.Addressing, RemoveWatchRow));
            }
            catch (Exception ex) when (ex is FormatException or AddressRangeException)
            {
                // 프로젝트의 워치 하나가 잘못돼도 나머지는 열려야 한다.
                ErrorMessage = $"워치 '{entry.Address}' 를 건너뜀: {ex.Message}";
            }
        }

        DigitalGroups.Clear();
        foreach (var entry in project.DigitalPoints)
        {
            try
            {
                DigitalGroups.Add(new DigitalPointGroupViewModel(
                    Engine.Memory, entry, project.Io.Addressing, RemoveDigitalPoint));
            }
            catch (Exception ex) when (ex is FormatException or AddressRangeException
                or InvalidOperationException)
            {
                ErrorMessage = $"디지털 점 '{entry.Address}' 를 건너뜀: {ex.Message}";
            }
        }

        AnalogPoints.Clear();
        foreach (var entry in project.AnalogPoints)
        {
            try
            {
                AnalogPoints.Add(new AnalogPointViewModel(
                    Engine.Memory, entry, project.Io.Addressing, RemoveAnalogPoint));
            }
            catch (Exception ex) when (ex is FormatException or AddressRangeException
                or InvalidOperationException)
            {
                ErrorMessage = $"A/D 채널 '{entry.Address}' 를 건너뜀: {ex.Message}";
            }
        }

        OnPropertyChanged(nameof(HasWatches));
        OnPropertyChanged(nameof(HasDigitalGroups));
        OnPropertyChanged(nameof(HasAnalogPoints));
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
        OnPropertyChanged(nameof(IsClientConnected));
        OnPropertyChanged(nameof(IsListeningWithoutClient));
        OnPropertyChanged(nameof(ConnectionText));
        OnPropertyChanged(nameof(ServerStatusText));
        OnPropertyChanged(nameof(StartStopLabel));
    }
}
