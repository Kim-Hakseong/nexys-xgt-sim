using System.Net;
using Nxs.Core.Memory;
using Nxs.Core.Server;

namespace Nxs.Core.Configuration;

/// <summary>서버 접속 설정(직렬화용 — IP는 문자열).</summary>
public sealed record ServerSettings
{
    /// <summary>바인딩 IP 문자열. <c>0.0.0.0</c>이면 모든 NIC.</summary>
    public string BindAddress { get; init; } = "0.0.0.0";

    /// <summary>접속 포트. [미정] spec 미기재이므로 프로젝트가 값을 갖는다.</summary>
    public required int Port { get; init; }

    /// <summary>서버 옵션으로 변환한다.</summary>
    /// <exception cref="FormatException">IP 문자열을 해석할 수 없을 때.</exception>
    public PlcTcpServerOptions ToServerOptions()
    {
        if (!IPAddress.TryParse(BindAddress, out var ip))
        {
            throw new FormatException($"바인딩 IP를 해석할 수 없습니다: '{BindAddress}'");
        }

        var options = new PlcTcpServerOptions { BindAddress = ip, Port = Port };
        options.Validate();
        return options;
    }
}

/// <summary>초기값 한 건 — 주소와 스칼라 값.</summary>
public sealed record InitialValue
{
    /// <summary>IEC 주소 표기.</summary>
    public required string Address { get; init; }

    /// <summary>쓸 값. 비트 주소는 0/1.</summary>
    public required uint Value { get; init; }
}

/// <summary>AD 채널 하나의 설정.</summary>
public sealed record AnalogChannelSettings
{
    /// <summary>베이스 번호.</summary>
    public int BaseNumber { get; init; }

    /// <summary>슬롯 번호.</summary>
    public required int SlotNumber { get; init; }

    /// <summary>채널 번호.</summary>
    public required int Channel { get; init; }

    /// <summary>스케일.</summary>
    public AnalogChannelScale Scale { get; init; } = AnalogChannelScale.Default;
}

/// <summary>
/// 프로젝트 문서 (PRD X-08 — .nxp JSON). I/O 구성 + 초기값 + 서버 설정을 담는다.
/// </summary>
/// <remarks>
/// 자동화 룰 절은 M6에서 추가된다. JSON 은 가산적이므로 키가 없는 기존 파일도 그대로 로드된다
/// (포맷 버전 상승 불필요).
/// </remarks>
public sealed record NxpProject
{
    /// <summary>현재 파일 포맷 버전.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>파일 포맷 버전.</summary>
    public int FormatVersion { get; init; } = CurrentFormatVersion;

    /// <summary>I/O 구성.</summary>
    public IoConfiguration Io { get; init; } = new();

    /// <summary>서버 설정.</summary>
    public required ServerSettings Server { get; init; }

    /// <summary>초기값 목록.</summary>
    public IReadOnlyList<InitialValue> InitialValues { get; init; } = [];

    /// <summary>
    /// 랙 슬롯 기준 AD 채널 설정. 자동화 룰의 공학단위 스케일 공유에 쓰인다
    /// (<see cref="BuildAutomationRules"/>). UI 의 A/D 탭은 <see cref="AnalogPoints"/> 를 쓴다.
    /// </summary>
    public IReadOnlyList<AnalogChannelSettings> AnalogChannels { get; init; } = [];

    /// <summary>사용자 지정 A/D 채널 — 임의 주소 + 스케일.</summary>
    public IReadOnlyList<AnalogPointEntry> AnalogPoints { get; init; } = [];

    /// <summary>자동화 룰 목록 (PRD X-06).</summary>
    public IReadOnlyList<Automation.AutomationRuleSettings> AutomationRules { get; init; } = [];

    /// <summary>
    /// 사용자 지정 워치 목록 — 랙 매핑 밖의 임의 주소(%MW320, %MD422 …)를 직접 보고 쓴다.
    /// </summary>
    public IReadOnlyList<WatchEntry> Watches { get; init; } = [];

    /// <summary>
    /// 사용자 지정 디지털 점 — 임의 주소를 비트 배열로 펼쳐 양방향으로 읽고 쓴다.
    /// </summary>
    public IReadOnlyList<DigitalPointEntry> DigitalPoints { get; init; } = [];

    /// <summary>CONTEXT 기재 랙 기반 기본 프로젝트를 만든다.</summary>
    public static NxpProject CreateDefault(int port) => new()
    {
        Io = IoConfiguration.CreateDefaultRack(),
        Server = new ServerSettings { Port = port },
    };

    /// <summary>초기값을 메모리에 적용한다.</summary>
    /// <exception cref="FormatException">주소 표기를 해석할 수 없을 때.</exception>
    public void ApplyInitialValues(PlcMemory memory)
    {
        ArgumentNullException.ThrowIfNull(memory);

        foreach (var initial in InitialValues)
        {
            memory.WriteScalar(IecAddress.Parse(initial.Address, Io.Addressing), initial.Value);
        }
    }

    /// <summary>지정 채널의 스케일을 찾는다. 설정이 없으면 <see cref="AnalogChannelScale.Default"/>.</summary>
    public AnalogChannelScale ScaleFor(int baseNumber, int slotNumber, int channel)
        => AnalogChannels.FirstOrDefault(
            c => c.BaseNumber == baseNumber && c.SlotNumber == slotNumber && c.Channel == channel)?.Scale
            ?? AnalogChannelScale.Default;

    /// <summary>
    /// 자동화 룰을 실행 가능한 형태로 만든다. 공학단위 룰은 매핑에서 해당 AD 채널의 스케일을 찾아 공유한다
    /// (DESIGN — 채널 설정의 스케일 공유).
    /// </summary>
    /// <exception cref="FormatException">룰 주소 표기를 해석할 수 없을 때.</exception>
    /// <exception cref="ArgumentException">룰 파라미터가 유효하지 않을 때.</exception>
    public IReadOnlyList<Automation.AutomationRule> BuildAutomationRules()
    {
        if (AutomationRules.Count == 0)
        {
            return [];
        }

        var map = Io.BuildMap();

        AnalogChannelScale? LookupScale(IecAddress address)
        {
            foreach (var mapping in map.Where(m => m.Module.Kind == ModuleKind.AnalogInput))
            {
                for (var channel = 0; channel < mapping.Module.ChannelCount; channel++)
                {
                    if (mapping.ChannelAddress(channel).Offset == address.Offset
                        && mapping.Area == address.Area
                        && address.Size == DataSize.Word)
                    {
                        return ScaleFor(mapping.BaseNumber, mapping.SlotNumber, channel);
                    }
                }
            }

            return null;
        }

        return AutomationRules.Select(s => s.ToRule(Io.Addressing, LookupScale)).ToArray();
    }
}
