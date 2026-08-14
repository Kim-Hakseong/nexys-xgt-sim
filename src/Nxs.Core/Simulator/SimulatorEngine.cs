using System.Net;
using Nxs.Core.Automation;
using Nxs.Core.Configuration;
using Nxs.Core.Diagnostics;
using Nxs.Core.Fixtures;
using Nxs.Core.Memory;
using Nxs.Core.Protocol;
using Nxs.Core.Server;
using Nxs.Core.Time;

namespace Nxs.Core.Simulator;

/// <summary>
/// 시뮬레이터 엔진 — 메모리 + I/O 구성 + 서버를 묶는 UI 무관 계층.
/// </summary>
/// <remarks>
/// <para>
/// 코덱은 팩토리로 주입된다. XGT FEnet 코덱은 spec 근거가 없어 존재하지 않으므로(⛔ M2 blocked-part)
/// 팩토리가 null이면 <see cref="CanStartServer"/>가 false 이고 <see cref="ServerUnavailableReason"/>이
/// 이유를 설명한다. **없는 프로토콜을 흉내내지 않는다** — 켜지지 않는 이유를 정확히 말하는 것이
/// 그럴듯한 가짜 응답보다 낫다.
/// </para>
/// </remarks>
public sealed class SimulatorEngine : IDisposable
{
    private readonly Func<PlcMemory, IFrameCodec>? _codecFactory;
    private readonly ITimeSource _time;
    private readonly ITrafficSink? _traffic;
    private readonly FrameRecorder? _recorder;

    private PlcTcpServer? _server;
    private ServerSettings _serverSettings;
    private CancellationTokenSource? _automationCts;
    private Task? _automationLoop;

    /// <summary>엔진을 만든다.</summary>
    /// <param name="project">프로젝트(구성·초기값·서버 설정).</param>
    /// <param name="codecFactory">
    /// 메모리를 받아 코덱을 만드는 팩토리. null이면 서버를 켤 수 없다 (⛔ spec 게이트).
    /// </param>
    /// <param name="timeSource">시간 원천. 기본은 시스템 시계.</param>
    /// <param name="trafficSink">트래픽 기록처.</param>
    /// <param name="frameRecorder">수신 프레임 자동 캡처기(마스터 실제 프레임을 근거로 남긴다).</param>
    /// <exception cref="IoConfigurationException">구성이 주소 산법과 모순될 때.</exception>
    public SimulatorEngine(
        NxpProject project,
        Func<PlcMemory, IFrameCodec>? codecFactory,
        ITimeSource? timeSource = null,
        ITrafficSink? trafficSink = null,
        FrameRecorder? frameRecorder = null)
    {
        ArgumentNullException.ThrowIfNull(project);

        Project = project;
        _codecFactory = codecFactory;
        _time = timeSource ?? new SystemTimeSource();
        _traffic = trafficSink;
        _recorder = frameRecorder;
        _serverSettings = project.Server;

        // 구성이 성립하지 않으면 여기서 실패한다 — 반쯤 열린 프로젝트를 만들지 않는다.
        Map = project.Io.BuildMap();
        Memory = new PlcMemory(new PlcMemoryOptions { Addressing = project.Io.Addressing });
        project.ApplyInitialValues(Memory);

        // 초기값을 넣은 **뒤에** 묶음을 건다 — 먼저 걸면 초기값 하나가 묶음 전체를 덮어쓴다.
        Memory.Links = project.BuildLinks(out var linkProblems);
        LinkProblems = linkProblems;
        Automation = new AutomationEngine(Memory, _time, project.BuildAutomationRules());
        TimeSource = _time;
    }

    /// <summary>원본 프로젝트.</summary>
    public NxpProject Project { get; }

    /// <summary>PLC 메모리. UI와 서버가 공유한다.</summary>
    public PlcMemory Memory { get; }

    /// <summary>이 엔진이 쓰는 시간원. 화면 갱신 로직이 같은 시계를 공유하게 한다.</summary>
    public ITimeSource TimeSource { get; }

    /// <summary>열 때 건너뛴 묶음의 사유. 비어 있으면 전부 정상이다.</summary>
    public IReadOnlyList<string> LinkProblems { get; private set; } = [];

    /// <summary>모듈 → 메모리 매핑.</summary>
    public IReadOnlyList<ModuleMapping> Map { get; }

    /// <summary>I/O 구성.</summary>
    public IoConfiguration Io => Project.Io;

    /// <summary>자동화 룰 엔진 (PRD X-06).</summary>
    public AutomationEngine Automation { get; }

    /// <summary>자동화 루프가 돌고 있는지.</summary>
    public bool IsAutomationRunning => _automationLoop is { IsCompleted: false };

    /// <summary>자동화 루프를 시작한다. 룰이 없으면 아무것도 하지 않는다.</summary>
    /// <param name="resolution">tick 검사 간격.</param>
    public void StartAutomation(TimeSpan? resolution = null)
    {
        if (IsAutomationRunning || Automation.Rules.Count == 0)
        {
            return;
        }

        _automationCts?.Dispose();
        _automationCts = new CancellationTokenSource();
        var token = _automationCts.Token;
        var step = resolution ?? TimeSpan.FromMilliseconds(50);
        _automationLoop = Task.Run(() => Automation.RunAsync(step, token), CancellationToken.None);
    }

    /// <summary>자동화 루프를 멈춘다.</summary>
    public async Task StopAutomationAsync()
    {
        if (_automationCts is not null)
        {
            await _automationCts.CancelAsync().ConfigureAwait(false);
        }

        if (_automationLoop is not null)
        {
            await _automationLoop.ConfigureAwait(false);
            _automationLoop = null;
        }

        _automationCts?.Dispose();
        _automationCts = null;
    }

    /// <summary>서버를 켤 수 있는지.</summary>
    public bool CanStartServer => _codecFactory is not null;

    /// <summary>서버를 켤 수 없는 이유. 켤 수 있으면 null.</summary>
    public string? ServerUnavailableReason => CanStartServer
        ? null
        : "FEnet 프레임 코덱이 없습니다 — spec/xgt-fenet-reference.md 에 프로토콜 근거(헤더 레이아웃·명령 코드·" +
          "에러 코드 표·예제 프레임)가 기재되지 않아 구현이 보류되었습니다. " +
          "매뉴얼 발췌를 채우거나 fixtures/labview-capture/ 에 캡처 프레임을 넣으면 활성화됩니다.";

    /// <summary>서버 수신 중인지.</summary>
    public bool IsServerRunning => _server?.IsRunning ?? false;

    /// <summary>바인딩된 종점. 미시작이면 null.</summary>
    public IPEndPoint? LocalEndPoint => _server?.LocalEndPoint;

    /// <summary>현재 접속 수.</summary>
    public int ConnectedClientCount => _server?.ConnectedClientCount ?? 0;

    /// <summary>서버 설정. 서버가 켜져 있는 동안에는 바꿀 수 없다.</summary>
    /// <exception cref="InvalidOperationException">서버가 켜져 있을 때.</exception>
    public ServerSettings ServerSettings
    {
        get => _serverSettings;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (IsServerRunning)
            {
                throw new InvalidOperationException("서버가 실행 중입니다 — 정지 후 설정을 바꾸십시오");
            }

            value.ToServerOptions();
            _serverSettings = value;
        }
    }

    /// <summary>서버를 켠다.</summary>
    /// <exception cref="InvalidOperationException">코덱이 없거나 이미 실행 중일 때.</exception>
    public async Task StartServerAsync(CancellationToken cancellationToken = default)
    {
        if (_codecFactory is null)
        {
            throw new InvalidOperationException(ServerUnavailableReason);
        }

        if (IsServerRunning)
        {
            throw new InvalidOperationException("서버가 이미 실행 중입니다");
        }

        if (_server is not null)
        {
            await _server.DisposeAsync().ConfigureAwait(false);
        }

        _server = new PlcTcpServer(
            _codecFactory(Memory), _serverSettings.ToServerOptions(), _time, _traffic, _recorder);
        await _server.StartAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>서버를 끈다.</summary>
    public async Task StopServerAsync()
    {
        if (_server is not null)
        {
            await _server.StopAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        StopAutomationAsync().GetAwaiter().GetResult();

        if (_server is not null)
        {
            _server.DisposeAsync().AsTask().GetAwaiter().GetResult();
            _server = null;
        }
    }
}
