# RALPH_LOG.md — Build history
Ralph appends one entry per milestone. Do not edit past entries.
---

## M0 — 스캐폴드 · 2026-07-30
Status ✅
Files: NexysXgtSimulator.sln, src/Nxs.Core/Nxs.Core.csproj, tests/Nxs.Core.Tests/{Nxs.Core.Tests.csproj,GlobalUsings.cs}, fixtures/labview-capture/
Tests 0/0 (스캐폴드) · 빌드 경고0/에러0
[결정] csproj 설정은 nexys-modbus-workbench와 동일 조합 이식: net8.0 / LangVersion 12 / Nullable enable / TreatWarningsAsErrors true / GenerateDocumentationFile true. 테스트는 xunit 2.9.2 + Microsoft.NET.Test.Sdk 17.11.1 + coverlet.collector 6.0.2 (CLAUDE.md §2 허용 목록 내).
[미정] fixtures/labview-capture/ 는 빈 디렉터리 — 캡처 픽스처 부재 (M7 회귀 skip 예정).
Next: M1

## M1 — 메모리맵 + IEC 주소 파서 · 2026-07-30
Status ✅
Files: src/Nxs.Core/Memory/{MemoryArea,DataSize,AddressingOptions,IecAddress,AddressRangeException,PlcMemoryOptions,PlcMemory}.cs, tests/Nxs.Core.Tests/Memory/{IecAddressTests,PlcMemoryTests}.cs
Tests 16/16 · 빌드 경고0/에러0
DoD: PRD M1 "파서·접근 벡터 통과" 충족. DESIGN 골든 벡터 전 항목 통과 —
  %MW100→(M,Word,100,byte200..202) · %MX801→(M,bit801,byte100 bit1) · %IX0.2.5→절대비트133 · %IW0.5.0→절대워드20 ·
  %ZW10/%IX0.2 → 파스 실패 · %MW0=0x0001→%MB0=0x01·%MX0=true(리틀엔디안) · 연속워드10개 라운드트립.
[결정] 슬롯 형식 산식을 크기 지정자에 일반화: 절대오프셋 = (base×BasePoints + slot×SlotPoints) / size.BitWidth() + point.
  DESIGN의 %IX0.2.5(=133)·%IW0.5.0(=20) 두 벡터를 동시에 만족하는 단일 산식이며, 산식은 spec 확정 후에도 불변(상수만 갱신).
[결정] AddressingOptions.SlotPoints 기본 64 (DESIGN 가정값). SlotsPerBase는 매뉴얼 미확정이라 기본 12 가정 + 설정 가능,
  Validate()가 SlotPoints를 32의 배수로 강제 — DWord(32비트) 단위 환산이 항상 정수여야 산식이 성립하기 때문.
[결정] 스레드 안전은 전 영역 단일 lock. 비트 쓰기가 읽기-수정-쓰기이므로 바이트를 공유하는 동시 비트 쓰기의
  갱신 유실을 막아야 한다 → 8레인 × 16000비트 동시 쓰기 회귀 테스트로 고정.
[결정] 범위 위반은 AddressRangeException(영역/시작/길이/영역크기)으로 통일하고 위반 시 메모리를 건드리지 않는다
  (연속 쓰기 부분 적용 금지). PRD X-04의 에러 응답 변환 지점 — 와이어 에러 코드 매핑은 spec 게이트 대상.
[미정] spec/xgi-addressing.md 4개 항목(슬롯당 점수·베이스당 슬롯 수·워드 환산·%M 크기) 전부 미기재.
  현재는 기본 가정값 + 설정으로 조정 가능. 확정 시 AddressingOptions 상수만 갱신하면 된다.
Next: M2

## M2 — FEnet 프레임 코덱 (spec 게이트) · 2026-07-30
Status ⛔ (blocked-part) / 나머지 ✅
Files: src/Nxs.Core/Protocol/{IFrameLengthRule,FramingException,StreamFrameAssembler,Hex,PlcRequest,PlcErrorReason,PlcResponse,PlcRequestLimits,PlcRequestExecutor,FrameExchange,IFrameCodec}.cs,
  tests/Nxs.Core.Tests/Protocol/{TestOnlyLengthPrefixFraming,StreamFrameAssemblerTests,HexTests,PlcRequestExecutorTests}.cs
Tests 49/49 · 빌드 경고0/에러0

⛔ blocked-part: **XGT FEnet 프레임 레이아웃 미구현.**
  근거: spec/xgt-fenet-reference.md 는 "채울 내용" 목차만 있고 6개 항목(①헤더 레이아웃 ②명령 코드·데이터 타입 코드 표
  ③변수명 표기 규칙 ④에러 상태 코드 표 ⑤예제 프레임 2쌍 ⑥접속 포트·프로토콜 기본값) 전부 미기재.
  fixtures/labview-capture/ 도 비어 있어 캡처 근거도 없음. CLAUDE.md §3(조작 제로) 에 따라 기억/추측 구현 금지 → 미구현.
  영향 범위: PRD X-03(FEnet 전용 프로토콜 프레임)·X-04(실장비 동일 에러 프레임)의 **와이어 인코딩 절반**,
  DESIGN "골든 벡터 § 프레임"(spec 예제 프레임 대조) — 근거 도착 시 확정.
  해제 조건: spec 6항목 기재 **또는** fixtures/labview-capture/ 에 req_*.bin + .expected 배치.
  해제 시 작업량: IFrameCodec 구현체 1개 추가 (서버·실행기·프레이밍은 수정 불필요 — 아래 구조가 그 지점을 미리 비워 둠).

구현한 것 (spec 무관 — 프로토콜 지식 0):
[결정] 프레임 경계 판정을 IFrameLengthRule 로 주입 분리 → StreamFrameAssembler 는 XGT 세부를 모른다.
  DESIGN "헤더 완독→길이만큼 완독" 상태머신을 이 층에 두어, spec 도착 시 규칙 구현체만 갈아끼우면 된다.
[결정] 부분 수신 불변(CLAUDE.md §4.2)을 테스트 전용 합성 프레이밍(TestOnlyLengthPrefixFraming, 0xAA55+len16LE)으로 고정.
  **XGT 가 아님을 클래스 주석·이름에 명시**하고 테스트 프로젝트에만 둔다(프로덕션 오염 방지).
  1바이트씩 주입 + 전 분할점(0..N) 왕복 + 25프레임 결정적 청크 스트리밍 + 최대길이 초과/헤더 해석불가 거절까지 검증.
[결정] 프로토콜 중립 요청/응답 모델(PlcRequest/PlcResponse/PlcErrorReason) + PlcRequestExecutor 로
  "요청 의미 → 메모리 효과 / 거절 사유"를 와이어와 분리. PlcErrorReason 은 **추상 사유이며 와이어 에러 코드가 아님**을 명시 —
  실제 에러 코드 표 매핑이 ⛔ 게이트 대상.
[결정] 거절은 예외가 아닌 PlcResponse 반환값 ("정확히 거절하는 것도 실장비 역할" — PRD X-04).
  쓰기는 검증 후 적용이라 거절된 요청은 메모리를 전혀 건드리지 않는다(부분 적용 금지 회귀 테스트 있음).
[결정] 한계값(개별 블록 수·연속 바이트 수)은 PlcRequestLimits 로 설정화하고 **기본 null=무제한**.
  근거 없는 상한을 넣으면 시뮬레이터가 실장비에 없는 거절을 발명해 LabVIEW 검증에 거짓 실패를 만든다 → 관용적 기본이 보수적 선택.
[결정] IFrameCodec 을 "프레임 → FrameExchange(응답 프레임 + 로그 요약 + 사유)" 한 메서드로 좁힘.
  에러 코드 매핑·Invoke/Frame ID 에코까지 구현체 안에 가두어, 서버(M3)가 프로토콜 지식 0으로 남는다.
[미정] 접속 포트 기본값 미기재 → 서버는 포트를 필수 설정 인자로 받고 기본값을 갖지 않는다(M3).
Next: M3

## M3 — 서버 e2e (테스트 클라이언트 왕복) · 2026-07-30
Status ✅ (⛔ M2 게이트는 그대로 — 아래 [미정] 참조)
Files: src/Nxs.Core/Time/{ITimeSource,SystemTimeSource}.cs, src/Nxs.Core/Diagnostics/{TrafficEvent,ITrafficSink}.cs,
  src/Nxs.Core/Server/{PlcTcpServerOptions,PlcTcpServer}.cs,
  tests/Nxs.TestKit/{Nxs.TestKit.csproj,FakeTimeSource,TestOnlyLengthPrefixFraming,TestOnlyFrameCodec,PlcTestClient,RecordingTrafficSink}.cs,
  tests/Nxs.Integration.Tests/{Nxs.Integration.Tests.csproj,GlobalUsings,PlcTcpServerTests}.cs
Tests 71/71 (Core 53 + Integration 18) · 빌드 경고0/에러0 · 통합 테스트 3회 반복 실행 안정
DoD: PRD M3 "읽기/쓰기/연속/오류 전 케이스 + 멀티클라이언트" 충족 —
  개별읽기·개별쓰기·연속읽기·연속쓰기 왕복 / 범위초과·주소파스실패 거절 / 1바이트씩 전송 왕복 /
  한 write 에 2요청 파이프라인 → 순서대로 2응답 / 3클라이언트 × 30왕복 응답 혼선 없음 /
  4클라이언트 × 20쓰기 전량 반영 / 연결 해제·프레이밍 위반 격리 / 정지·재시작 / 중복 시작 거절 / 트래픽 Rx·Tx·오류 기록.

[결정] 서버를 프로토콜 무지(protocol-agnostic)로 설계: PlcTcpServer 는 IFrameCodec 만 안다.
  → XGT 근거가 도착해도 이 파일은 **수정 대상이 아니다**. 코덱 1개만 추가하면 실장비 응답이 된다.
[결정] 연결마다 독립 StreamFrameAssembler → 부분 수신 상태가 연결별로 격리된다.
[결정] 프레이밍 위반 = 해당 연결만 종료(수신 상태 재동기화 불가), 요청 거절 = 연결 유지 + 에러 응답.
  실장비의 태도와 같고, 오류 후에도 같은 연결이 계속 동작하는지 회귀 테스트로 고정했다.
[결정] 테스트에서 Port=0 (OS 배정) 사용 → 포트 충돌로 인한 플레이키 제거. LocalEndPoint 로 실제 포트를 노출.
[결정] 트래픽 기록은 ITrafficSink 주입(이벤트 아님). 서버가 여러 스레드에서 호출하므로 "스레드 안전·비블로킹"
  계약을 인터페이스 주석에 명시. 전체 TrafficLog(필터·파일 저장)는 M6.
[결정] 타임스탬프는 전부 ITimeSource.UtcNow — FakeTimeSource 주입으로 결정적 검증(테스트가 시각을 단정한다).
  단조 증가 밀리초를 별도 제공(월클럭 점프에 영향받지 않는 자동화 tick 용, M6에서 사용).
[결정] TestKit 을 별도 프로젝트로 분리 — Core.Tests·Integration.Tests·(M5 UI 스모크)가 공유한다.
  합성 코덱/프레이밍은 **XGT 가 아님**을 클래스명(TestOnly*)·주석에 명시해 프로덕션 혼입을 구조적으로 차단.
[미정] 합성 코덱으로 검증한 것은 **서버 파이프라인**이다. 실제 XGT 프레임 왕복은 ⛔ M2 게이트가 풀린 뒤
  동일 테스트 구조를 실코덱으로 한 번 더 돌려야 완결된다(M7 캡처 회귀가 그 자리).
[미정] 기본 포트 없음 — PlcTcpServerOptions.Port 를 required 로 두어 근거 없는 기본값을 넣지 않았다.
Next: M4

## M4 — I/O 구성 모델 + .nxp · 2026-07-30
Status ✅
Files: src/Nxs.Core/Configuration/{ModuleDefinition,IoConfigurationException,ModuleMapping,IoConfiguration,AnalogChannelScale,NxpProject,NxpProjectFile}.cs,
  tests/Nxs.Core.Tests/Configuration/{IoConfigurationTests,AnalogChannelScaleTests,NxpProjectTests}.cs
Tests 108/108 (Core 90 + Integration 18) · 빌드 경고0/에러0
DoD: PRD M4 "매핑·라운드트립 테스트" 충족 — CONTEXT 랙 슬롯별 매핑 검증 + .nxp 저장/로드 라운드트립.

CONTEXT 랙 매핑 결과 (슬롯 스트라이드 256점):
  슬롯0 XGL-EFMT(B) / 슬롯1 XGL-C42A → 통신 모듈, 프로세스 데이터 0 → 매핑 없음
  슬롯2 XGI-D24A → %IX512..543 (32점) · 슬롯3 → %IX768..799
  슬롯4 XGQ-TR4A → %QX1024..1055 (32점)
  슬롯5 XGF-AD16A → %IW80..95 (16채널=16워드) · 슬롯6 → %IW96..111
  영역 내 범위 무중첩을 테스트로 고정.

[결정] **CONTEXT 랙의 슬롯 스트라이드를 256점(16워드)으로 지정.** 기본 가정 64점으로는 XGF-AD16A(16채널×16비트=256비트)가
  담기지 않는다 — 64점 스트라이드에 AD16A를 넣으면 IoConfigurationException 으로 거절되는 것을 테스트로 고정했다.
  DESIGN "spec 확정 시 [결정] 로그로 상수만 갱신, 산식 불변" 조항에 따라 **산식은 그대로**(base×BasePoints + slot×SlotPoints)
  두고 이 랙의 상수만 지정한다. AddressingOptions 기본값은 64로 유지 → M1 골든 벡터 불변(수정/삭제 없음).
[미정] 실제 XGI 슬롯 할당 규칙(고정 64점인지, 모듈별 가변인지)은 spec/xgi-addressing.md 미기재.
  확정 시 IoConfiguration.CreateDefaultRack() 의 SlotPoints 상수 하나만 바꾸면 된다.
[결정] 구성 모순은 침묵 대신 예외: 모듈이 스트라이드보다 큼 / 슬롯 번호 중복 / 베이스 용량 초과 → IoConfigurationException.
  겹치는 매핑을 조용히 만들어 두면 나중에 원인 모를 값 오염으로 나타난다.
[결정] 통신 모듈(FEnet/Cnet)은 OccupiedBits=0 이라 매핑 결과에서 제외 — 슬롯0·1이 %I/%Q 를 잠식하지 않는다.
[결정] AnalogChannelScale 은 공학단위↔raw 양방향 + 범위 밖 클램프. 양극성(-10..+10V) 스케일을 위해
  raw 를 부호 있는 값으로 다루고 워드 저장은 2의 보수(RawToWord/WordToRaw) — 음수 raw 가 왕복에서 살아남는지 테스트로 고정.
[결정] .nxp 는 System.Text.Json(인박스, NuGet 추가 없음) camelCase + WriteIndented — 사람이 열어 고칠 수 있어야 한다.
  저장은 임시 파일 → File.Move 교체(원자적)이고, 저장 전에 BuildMap()·ToServerOptions() 로 검증한다
  → 실패한 저장이 기존 파일을 손상시키지 않는 것을 테스트로 고정.
[결정] formatVersion 은 **스키마 바인딩 전에** 검사한다. 미래 버전 파일은 이 프로그램이 모르는 필드를 가질 수 있어
  바인딩이 먼저 실패하면 "JSON 오류"로 오진된다 — 실제로 그 오진이 테스트에서 잡혀 순서를 바로잡았다.
[결정] 자동화 룰 절은 M4 .nxp 에 넣지 않았다(타입이 M6에 생긴다). JSON 은 가산적이므로 M6에서 키를 추가해도
  기존 파일이 그대로 로드되어 포맷 버전 상승이 불필요하다.
Next: M5

## M5 — UI: 랙 패널(토글·LED·AD 입력) · 2026-07-30
Status ✅
Files: src/Nxs.Core/Simulator/SimulatorEngine.cs,
  src/Nxs.App/{Nxs.App.csproj,Program.cs,App.axaml,App.axaml.cs},
  src/Nxs.App/ViewModels/{MainWindowViewModel,SlotViewModel,DigitalPointViewModel,AnalogChannelViewModel}.cs,
  src/Nxs.App/Views/{MainWindow.axaml,MainWindow.axaml.cs},
  tests/Nxs.App.Tests/{Nxs.App.Tests.csproj,TestAppBuilder,VisualTokenTests,RackPanelSmokeTests}.cs,
  tests/Nxs.Integration.Tests/SimulatorEngineTests.cs
Tests 154/154 (Core 90 + Integration 32 + App 32) · 빌드 경고0/에러0
DoD: PRD M5 "스모크: 테스트 클라이언트로 쓴 값이 LED에, 토글이 읽기에 반영" 충족 —
  헤드리스로 윈도우 기동 → 서버 시작 → 클라이언트가 %QX1031 쓰기 → LED VM IsOn=true 확인 /
  UI 토글 %IX517 → 클라이언트 읽기 0x01, 해제 시 0x00 / AD 공학단위 5V 입력 → raw 2000 → 클라이언트 읽기 2000.

비주얼 이식 검증 (DESIGN Rev.B 골든 벡터 — 전 항목 통과):
  App.axaml 을 원본 nexys-modbus-workbench/src/Nmw.App/App.axaml 에서 **x:Class 한 줄만 바꿔 그대로 이식**(248행).
  리소스 값 대조 테스트로 고정: AccentBrush=#7A1020 · AccentSoftBrush=#F0EAE7 · CardBrush=#FCFBF9 ·
  CardSoftBrush=#F1EFEA · LineBrush=#DDDBD3 · InkBrush=#16171A · InkHoverBrush=#2C2E33 ·
  TextPrimaryBrush=#16171A · TextSecondaryBrush=#8B897F · ErrorBrush=#9C2030 ·
  ToggleButtonBackgroundChecked=#7A1020(+hover #9C2030 / pressed #5A0B18 / 흰 글자) ·
  SystemAccentColor 계열 3개 · AppBackgroundBrush 그라데이션 3스톱(#F4F3F0/#ECEAE5/#DDDBD3) ·
  pill 클래스(CornerRadius 17 + LineBrush 테두리) · 실제 윈도우의 mono 클래스 컨트롤 전수 FontFamily 검사.

[결정] 데이터 표시 monospace 를 `mono` 스타일 클래스로 도입. 원본은 MainWindow.axaml 에 인라인
  `FontFamily="Menlo,Consolas,monospace"` 1곳뿐이지만, 시뮬레이터는 데이터 컨트롤이 100개 이상(32점×3 + 16채널×2 + 주소 라벨)이다.
  **값은 원본과 완전히 동일**하고 새 시각 디자인을 만들지 않았으므로 "새 스타일 발명 금지"에 저촉되지 않는다.
[결정] 시뮬레이터 고유 요소(point 토글·led 타원·pill connected/disconnected·notice 배너)는
  기존 토큰만 재사용해 정의 — 팔레트 외 색을 도입하지 않았다(녹색 LED 금지 조항 준수).
  LED OFF = Transparent 채움 + LineBrush 테두리, ON = AccentBrush 채움을 테스트로 고정.
[결정] SimulatorEngine 을 Core 에 두어 UI 무관 계층으로 분리 → 랙/서버/메모리 조합을 App 없이 테스트할 수 있다.
[결정] **코덱 부재를 UI가 정직하게 드러낸다.** codecFactory 가 null 이면 CanStartServer=false,
  ServerUnavailableReason 이 spec 파일 경로와 해제 조건을 그대로 안내하고, 시작 버튼이 비활성화되며 notice 배너가 뜬다.
  없는 프로토콜을 흉내내는 대신 "왜 못 켜는지"를 말한다 — 조작 제로 원칙의 UI 표현.
  단 **게이트는 서버에만 걸린다**: 랙 패널(토글·LED·AD)은 코덱 없이도 전부 조작 가능하다(테스트로 고정).
[결정] 출력 점은 IsWritable=false — 마스터가 쓰는 값을 표시만 한다. 입력 점만 사용자 토글이 메모리에 반영된다.
[결정] UI 갱신은 DispatcherTimer 200ms 로 메모리를 읽기만 한다(블로킹 I/O 없음, CLAUDE.md §3).
[결정] AD 입력칸은 `_displayedRaw` 기준으로 **외부 변경만** 반영한다.
  최초 구현에서 주기 갱신이 입력 중 텍스트를 되돌리는 버그가 있었고(조건에 `Error is null` 을 잘못 넣어
  오류 상태에서 되돌림이 발생), 회귀 테스트(PeriodicRefreshDoesNotClobberInProgressTyping)가 이를 잡아 수정했다.
[결정] 프로젝트 저장은 현재 UI 상태(켜진 입력 점 + 0이 아닌 AD 채널)를 초기값으로 스냅샷한다 → 저장/열기 라운드트립 테스트.
  깨진 .nxp 를 열면 오류 메시지만 표시하고 앱은 계속 동작한다(테스트로 고정).
[미정] 파일 열기/저장 다이얼로그는 미연결 — OpenProject/SaveProject(path) API 만 있다. M8 에서 메뉴와 연결한다.
Next: M6

## M6 — 값 자동화 + 트래픽 로그 · 2026-07-30
Status ✅
Files: src/Nxs.Core/Automation/{IValueGenerator,ValueGenerators,AutomationRule,AutomationEngine,AutomationRuleSettings}.cs,
  src/Nxs.Core/Diagnostics/TrafficLog.cs, src/Nxs.Core/Configuration/NxpProject.cs(자동화 절 추가),
  src/Nxs.Core/Simulator/SimulatorEngine.cs(자동화 루프), src/Nxs.App/ViewModels/{AutomationRuleViewModel,TrafficRowViewModel,MainWindowViewModel}.cs,
  src/Nxs.App/Views/MainWindow.axaml(값 자동화·트래픽 로그 탭), tests/Nxs.Core.Tests/Automation/{ValueGeneratorTests,AutomationEngineTests,AutomationRuleSettingsTests}.cs,
  tests/Nxs.Core.Tests/Diagnostics/TrafficLogTests.cs, tests/Nxs.App.Tests/AutomationAndTrafficSmokeTests.cs,
  tests/Nxs.TestKit/FakeTimeSource.cs(Delay 양보 수정)
Tests 222/222 (Core 145 + Integration 32 + App 45) · 빌드 경고0/에러0
DoD: PRD M6 "제너레이터 벡터 + 스모크" 충족.

DESIGN 골든 벡터 (자동화 — 검증된 값) 전 항목 통과:
  Ramp(0,100,25) tick0..5 → 0,25,50,75,100,0 · Sine(0,1000,period4) → 500,1000,500,0 · Toggle → T,F,T
  룰 엔진을 통해서도 동일 수열이 메모리에 나타나는지 확인(SuccessiveDuePeriodsWalkTheGoldenRampVector).

[결정] Ramp 산식은 골든 벡터에서 역산: 한 주기의 값 개수 = (Max-Min)/Step + 1, value = Min + (tick % 개수) × Step.
  이것이 "Max 를 정확히 찍고 다음 tick 에 Min 으로 복귀"라는 벡터를 만족하는 유일한 해석이다.
  Increment(모듈러 카운터)와 별개 제너레이터로 분리 — 둘의 wrap 동작이 다르다.
[결정] Random 은 System.Random 인스턴스를 쓰지 않고 (seed, tick) 해시(splitmix64 계열)로 만든다.
  Random 인스턴스는 호출 순서에 값이 의존해 "tick 순수 함수" 계약을 깨뜨린다 — 같은 시드로 같은 수열이 재현되는지,
  임의 순서 호출에도 값이 같은지 테스트로 고정.
[결정] tick 진행 기준은 ITimeSource.MonotonicMilliseconds (월클럭 아님). 시스템 시각이 점프해도 주기가 튀지 않는다.
[결정] 한 룰의 실패가 다른 룰을 막지 않는다 — 범위 밖 주소는 AutomationTickResult.Failures 로 보고하고 계속 진행.
[결정] AutomationEngine.SetEnabled(index,bool) 로 실행 중 켜고 끌 수 있게 했다. 룰은 불변 record 라
  UI 체크박스가 동작하려면 엔진 쪽에 가변 상태가 필요했다. 다시 켤 때 tick 인덱스는 유지된다(테스트로 고정).
[결정] 공학단위 룰은 대상 주소가 AD 채널이면 **그 채널의 스케일을 자동으로 공유**한다
  (NxpProject.BuildAutomationRules 가 매핑을 역조회). DESIGN "채널 설정의 스케일 공유" 조항의 구현.
  채널이 아닌 주소에 공학단위를 켜면 스케일 없이(raw) 동작한다 — 조용히 틀린 변환을 하지 않는다.
[결정] TrafficLog 는 고정 용량(기본 5000) 링 버퍼. 장시간 켜 두는 도구라 무한 누적은 메모리를 잠식한다.
  넘친 건수를 DroppedCount 로 노출하고 저장 파일 헤더에도 적어, 로그가 **조용히 사라지지 않게** 했다.
[결정] 트래픽 표시는 최근 500행 상한. 5000행을 매 200ms 재투사하면 UI가 버거워진다.
[버그 수정] FakeTimeSource.Delay 가 완료된 Task 를 반환해 `while(!ct){ work(); await Delay(); }` 루프가
  스케줄러에 양보하지 않고 스레드를 영구 점유했다(RunAsync 테스트가 무한 정지). 시각 전진 후 Task.Yield() 하도록 수정.
  이는 테스트 더블의 결함이었지 프로덕션 코드 문제는 아니었다.
[수정] "M4 시절 .nxp(자동화 절 없음) 로드" 테스트를 정규식 문자열 삭제 방식에서 손으로 쓴 레거시 문서로 교체.
  정규식이 마지막 키를 지워 trailing comma 를 남기는 **테스트 자체의 결함**이었다. JSON 가산성은 그대로 확인됨.
[미정] 트래픽 로그 저장/프로젝트 열기·저장은 API(SaveTraffic/OpenProject/SaveProject)만 있고 파일 다이얼로그 미연결 — M8.
Next: M7

## M7 — 캡처 픽스처 회귀 + (있으면) Cnet · 2026-07-30
Status ⚠️ (하네스 ✅ / 회귀 스위트 비활성 — 입력 부재) · Cnet ⛔
Files: src/Nxs.Core/Fixtures/{CaptureCase,CaptureFixtureLoader,CaptureReplayRunner}.cs,
  tests/Nxs.Integration.Tests/{CaptureReplayTests,LabViewCaptureRegressionTests}.cs
Tests 237/237 (Core 145 + Integration 47 + App 45) · 빌드 경고0/에러0
DoD: PRD M7 "fixtures 재생 전 케이스 응답 일치" — **입력 부재로 실행할 케이스가 0건**.
  DESIGN "부재 시 skip" 조항에 따라 스위트는 비활성이며, 그 사실을 테스트가 명시적으로 고정한다.

⛔ blocked-part 1: **X-09 Cnet 서버 미구현.**
  근거: spec/xgt-cnet-reference.md 도 "채울 내용" 한 줄만 있고 제어문자(ENQ/EOT/ACK/NAK)·국번·명령어(RSS/WSS)·
  BCC 계산 범위·예제 프레임 전부 미기재. CLAUDE.md §3 에 따라 미구현. (P1 항목이므로 P0 완성도에는 영향 없음)
  참고: Cnet 도 IFrameCodec + StreamFrameAssembler 구조를 그대로 쓸 수 있다 — 전송만 System.IO.Ports 로 바뀐다.

⚠️ 비활성 사유 2겹: (1) fixtures/labview-capture/ 가 비어 있음 (2) XGT 코덱 부재(M2 ⛔).
  **둘 중 하나만 풀려도 부족하다**: 캡처를 해석할 코덱이 없으면 재생 불가, 코덱이 있어도 대조할 실장비 응답이 없으면
  정합성 확인 불가. 두 조건을 모두 충족해야 이 스위트가 "실장비 대역"으로 기능한다.

[결정] **빈 디렉터리를 훑고 통과하는 무의미한 테스트를 만들지 않기 위해**, 하네스 자체를 합성 픽스처로 검증했다
  (CaptureReplayTests 11건: 로더 탐색·정렬·확장자 규약 / 일치·불일치·기대값부재·프레이밍위반·중도절단 판정 /
  2요청 캡처 응답 연결 / 생성한 픽스처 디렉터리 e2e). 실캡처가 오면 **같은 코드**가 그것을 검사한다.
[결정] 재생은 요청 바이트를 **1바이트씩** 주입한다 → 캡처 회귀가 부분 수신 불변까지 동시에 검증한다(CLAUDE.md §4.2).
  BytesFedOneAtATime 을 결과에 노출해 실제로 1바이트씩 넣었는지 테스트가 확인한다.
[결정] .expected 부재는 **통과가 아니라 NoExpectedResponse(판정 불가)** 로 보고하고, 사람이 매뉴얼 대조로 작성할 수 있도록
  현재 시뮬레이터 응답 hex 를 메시지에 담는다. 시뮬레이터 출력을 그대로 기대값으로 복사하면
  회귀가 자기 자신을 검증하는 셈이 되어 무의미하므로, 자동 생성은 하지 않는다.
[결정] 캡처가 있는데 코덱이 없으면 **조용히 통과시키지 않고 실패**시킨다. 실제로 합성 캡처 1건을 넣어
  이 경로를 확인했다 — "캡처 'req_probe' 가 있지만 XGT FEnet 코덱이 없어 재생할 수 없습니다 …(M2 ⛔ blocked-part)"
  로 실패하고, 확인 후 파일을 제거했다. 게이트가 공허하지 않다는 증거.
[결정] RegressionSuiteStateIsReportedExplicitly 가 현재 상태(캡처 0건 · 코덱 null)를 고정한다.
  캡처가 추가되면 이 테스트가 실패해 "스위트가 활성화되었고 코덱 연결이 필요하다"는 신호를 준다.
[결정] xUnit v2 는 데이터가 빈 Theory 를 오류로 처리하므로 센티널 1건("(캡처 없음)")으로 비활성 경로를 표현했다.
  허용 NuGet 목록 밖(Xunit.SkippableFact)을 쓰지 않기 위한 선택이다.
[결정] CaptureFixtureLoader.FindDirectory 는 실행 위치에서 위로 올라가며 fixtures/labview-capture 를 찾는다
  (테스트가 bin/ 하위에서 돌기 때문). 탐색 자체가 항상 성립하는지 별도 테스트로 고정 — 캡처를 넣기만 하면 편입된다.
Next: M8

## M8 — publish + 사용법 README + LabVIEW 접속 체크리스트 · 2026-07-30
Status ✅
Files: README.md, LABVIEW_CHECKLIST.md, SMOKE_CHECKLIST.md,
  src/Nxs.App/Views/{MainWindow.axaml,MainWindow.axaml.cs}(파일 다이얼로그 연결)
Tests 237/237 (Core 145 + Integration 47 + App 45) · 빌드 경고0/에러0
DoD: PRD M8 "publish 확인 후 삭제" 충족.

publish 검증:
  `dotnet publish src/Nxs.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true
   -p:IncludeNativeLibrariesForSelfExtract=true`
  → 단일 파일 Nxs.App.exe 89MB 생성. `file` 판정 **PE32+ executable (GUI) x86-64, for MS Windows** 확인.
  CLAUDE.md §5 에 따라 확인 후 삭제 완료 (publish 디렉터리 + src/*/bin/Release 제거).
  ⚠️ **실행 검증은 하지 않았다** — 빌드 호스트가 macOS 라 Windows 바이너리를 구동할 수 없다.
  exe 가 올바른 형식으로 생성됨까지만 확인했고, 실제 기동은 Windows PC 에서 SMOKE_CHECKLIST.md 0장으로 수행해야 한다.

[결정] M5/M6 에서 [미정]으로 남긴 파일 다이얼로그를 연결했다 (Avalonia StorageProvider).
  헤더에 프로젝트 열기/저장·트래픽 로그 저장 버튼 3개(App.axaml 이식분의 IconFolder/IconSave/IconDownload 재사용 —
  새 아이콘 도입 없음). 다이얼로그 로직은 View 코드비하인드에 두어 ViewModel 의 UI 무의존을 유지했다.
[결정] README.md 를 **⛔ 게이트 설명으로 시작**하도록 썼다. 이 저장소를 처음 여는 사람이 가장 먼저 알아야 하는 것은
  "왜 서버가 안 켜지는지"와 "무엇을 채우면 켜지는지"이기 때문이다. 해제 방법 2가지(매뉴얼 발췌 / LabVIEW 캡처)와
  근거 도착 후 작업량(IFrameCodec 구현체 1개, 그 외 무수정)을 명시했다.
[결정] LABVIEW_CHECKLIST.md 의 0장을 "캡처 먼저"로 두었다. 1~5장은 코덱이 있어야 유효하지만
  0장은 지금 당장 가능하고, 그 캡처가 코덱 구현의 근거가 되므로 순서상 맨 앞이 맞다.
  증상별 원인 표(접속 불가·값 이상·엔디안·스케일·프레이밍)를 붙여 현장에서 바로 쓰게 했다.
[결정] SMOKE_CHECKLIST.md 는 코덱 없이 수행 가능한 장(0~2, 4~7)과 불가능한 장(3)을 구분해 표시했다.
  게이트가 서버에만 걸려 있어 랙 패널·자동화·프로젝트 파일은 지금도 전부 검증 가능하다.
[미정] spec/ 3개 파일은 사람의 입력 파일이라 손대지 않았다(내용을 추가하면 근거 조작이 된다).
Next: —

---

# 빌드 종료 요약 (M1~M8)

전체: 테스트 237/237 통과 · 빌드 경고0/에러0 · publish 확인 후 삭제 완료.

## ⛔ 미해결 blocked-part (전부 spec 근거 부재 — 코드 문제 아님)
1. **XGT FEnet 프레임 코덱** (M2) — spec/xgt-fenet-reference.md 6항목 미기재.
   영향: PRD X-03·X-04 의 와이어 인코딩, DESIGN "골든 벡터 § 프레임", 앱에서 서버 기동.
   해제 시 작업량: IFrameCodec 구현체 **1개** 추가. 서버·프레이밍·실행기·메모리·UI·자동화는 수정 불필요.
2. **Cnet 서버** (M7, PRD X-09, P1) — spec/xgt-cnet-reference.md 미기재.
   동일 IFrameCodec + StreamFrameAssembler 구조 재사용 가능, 전송만 System.IO.Ports 로 교체.
3. **캡처 회귀 스위트 비활성** (M7) — fixtures/labview-capture/ 비어 있음 + 위 1번.
   하네스는 합성 픽스처로 검증 완료. 캡처를 넣으면 자동 편입되고 코덱 부재 시 실패로 알린다(실측 확인).

## 미확정 가정 (spec/xgi-addressing.md 부재 — 확정 시 상수만 갱신)
- 슬롯당 점수: CONTEXT 랙은 256점(16워드) 가정. XGF-AD16A 16워드가 기본 가정 64점에 담기지 않아 택한 값.
  AddressingOptions 기본값은 64 유지 → M1 골든 벡터 불변. IoConfiguration.CreateDefaultRack() 의 상수 1개만 변경 지점.
- 베이스당 슬롯 수 12 가정 · FEnet 기본 포트 없음(필수 설정) · 요청 한계값 무제한(관용적 기본).

## 완성된 것 (spec 무관 전 항목)
메모리맵·IEC 주소 파서(골든 벡터 통과) / 부분 수신 불변 프레이밍 상태머신(1바이트 주입 + 전 분할점) /
프로토콜 중립 요청 실행기 + 정확한 거절 / 멀티클라이언트 TCP 서버(연결 격리·정지/재시작) /
I/O 구성 자동 매핑 + .nxp 라운드트립 / Avalonia 랙 패널(비주얼 이식 대조 테스트 통과) /
값 자동화 6종(골든 벡터 통과·tick 순수 함수) / 트래픽 로그(링 버퍼·오류 필터·파일 저장) / 캡처 재생 하네스.

---

## M9 — 공개 저장소 + 릴리즈 배포 (사용자 추가 요청) · 2026-07-30
Status ✅
Files: .gitignore, README.md(스크린샷·사용법 체크리스트 재작성), fixtures/labview-capture/.gitkeep,
  tools/Nxs.DocShots/{Nxs.DocShots.csproj,Program.cs}, docs/screenshots/*.png(6장),
  src/Nxs.App/App.axaml(비활성 버튼 스타일), src/Nxs.App/Views/MainWindow.axaml(DataGrid 열 폭),
  tests/Nxs.App.Tests/VisualTokenTests.cs(비활성 버튼 회귀 2건)
Tests 239/239 (Core 145 + Integration 47 + App 47) · 빌드 경고0/에러0
산출물: https://github.com/Kim-Hakseong/nexys-xgt-sim (PUBLIC)
       릴리즈 v0.1.0-preview — Nxs.App.exe (win-x64 단일 파일 88MB) + SHA256SUMS.txt

[결정] 스크린샷을 헤드리스 Skia 렌더러로 생성하는 도구(tools/Nxs.DocShots)를 만들었다.
  화면 캡처 방식은 재현 불가·창 포커스 의존이라 문서가 코드와 어긋나기 쉽다. 이제 `dotnet run` 한 줄로 재생성된다.
[결정] 랙 패널 스크린샷은 **코덱을 주입하지 않은 운영과 동일한 상태**로 찍었다(⛔ 배너 그대로 노출).
  서버가 켜진 화면을 찍으면 배포본이 할 수 없는 일을 할 수 있는 것처럼 오인시킨다.
[결정] 트래픽 로그 탭만 예외적으로 합성 코덱으로 실제 왕복을 발생시켜 찍고,
  README 캡션에 "표시된 hex 는 합성 포맷이며 XGT 프레임이 아니다"를 명시했다. 빈 로그 화면은 기능을 전달하지 못한다.
[버그 수정] ⛔ 게이트로 잠긴 [시작] 버튼이 **활성처럼 보였다** — Fluent 기본이 accent 배경을 비활성에서도 유지한다.
  스크린샷을 눈으로 검토하다 발견했다. 기존 토큰(CardSoft/Line/TextSecondary)만으로 비활성 상태를 정의하고
  회귀 테스트 2건(DisabledAccentButtonDoesNotLookClickable · StartButtonIsDisabledWhenTheCodecGateIsClosed)으로 고정.
  누를 수 없는 버튼이 눌릴 것처럼 보이면 사용자가 원인 모르고 계속 누르게 된다.
[수정] 트래픽 로그 DataGrid 의 시각·방향·사유 열이 잘려 "RangeExceede", "방" 으로 보였다 → 폭 조정(118/74/152).
[결정] 릴리즈는 **prerelease** 로 표시했다. FEnet 코덱이 없어 주 기능(LabVIEW 접속)을 수행할 수 없는 빌드를
  정식 릴리즈로 내면 받는 사람을 오인시킨다. 릴리즈 노트 첫 절이 "이 빌드는 LabVIEW 접속을 받을 수 없다"로 시작한다.
[결정] prerelease 라서 GitHub 의 `releases/latest` 가 이 릴리즈를 가리키지 않는다(실측 확인 — /releases 로 리다이렉트).
  README 링크를 `../../releases` 로 바꿔 항상 유효하고 다음 릴리즈에도 낡지 않게 했다.
[결정] exe 는 저장소에 커밋하지 않고 릴리즈 자산으로만 배포한다(.gitignore 에 *.exe). CLAUDE.md §5 의
  "확인 후 삭제" 취지를 지키면서 배포 요구를 충족한다 — 로컬 publish 산출물은 업로드 후 삭제했다.
[검증] 릴리즈 자산 다운로드를 실측했다: HTTP 200, 선두 2바이트 `4D 5A`(MZ) — 실제 PE 실행 파일임을 확인.
  README 스크린샷 6장 raw URL 도 전부 200 확인.
[미검증] **Windows 실제 기동은 여전히 확인하지 못했다** (빌드 호스트가 macOS).
  `file` 판정과 MZ 시그니처까지만 확인. 릴리즈 노트와 README 양쪽에 이 한계를 명시했다.
[미정] CONTEXT.md·README_KIT.md 는 사내 맥락(부서명·내부 작업 절차)을 담고 있으나 사용자 지시대로 공개했다.
  스크럽이 필요하면 히스토리 재작성이 필요하다(현재 커밋 1개라 비용은 낮다).
Next: — (사용자 요청 완료)

## M10 — XGT FEnet 코덱 초안 + 주소 워치 (사용자 지시) · 2026-07-30
Status ⚠️ (동작하지만 미검증 초안)
Files: spec/xgt-fenet-reference.md(초안 작성), src/Nxs.Core/Protocol/Xgt/{XgtFenetOptions,XgtFenetHeader,XgtFenetFraming,XgtFenetCodec}.cs,
  src/Nxs.Core/Fixtures/FrameRecorder.cs, src/Nxs.Core/Configuration/WatchEntry.cs,
  src/Nxs.App/ViewModels/WatchRowViewModel.cs, src/Nxs.App/{App.axaml.cs,Views/MainWindow.axaml},
  tests/Nxs.Core.Tests/Protocol/Xgt/XgtFenetCodecTests.cs, tests/Nxs.Core.Tests/Configuration/WatchEntryTests.cs,
  tests/Nxs.Integration.Tests/XgtFenetEndToEndTests.cs, tests/Nxs.App.Tests/{WatchListSmokeTests,TabRenderTests}.cs
Tests 325/325 (Core 198 + Integration 58 + App 69) · 빌드 경고0/에러0

[결정] **CLAUDE.md §3(조작 제로) 을 이번 건에 한해 완화.** 사용자가 4개 선택지 중 "제가 초안 작성 → 검증"을
  명시적으로 선택했다. 원래 규칙의 근거(추측 프레임은 LabVIEW 버그와 구분 불가)는 여전히 유효하므로
  **위험을 구조로 줄이는 방식**을 택했다:
  1. 신뢰도 낮은 헤더 필드(CPU Info·Position)는 **요청 값 에코** → 정답을 몰라도 된다. 맞혀야 하는 값의 수를 줄인다.
  2. 수신 BCC 는 **기본 미검사** — 계산 범위가 미확정인데 틀린 범위로 검사하면 정상 요청을 전부 거절해
     "접속 자체가 안 되는" 최악의 증상이 된다. 관용적 기본이 안전하다(ValidateInboundBcc 로 켤 수 있음).
  3. 에러 코드 표·쓰기 블록 배치·한계값을 XgtFenetOptions 로 노출 → 재컴파일 없이 실장비와 맞춘다.
  4. 해석 실패는 예외가 아니라 에러 응답 → 연결을 유지해 트래픽 로그로 계속 진단한다.
[결정] spec 문서에 **항목별 신뢰도 등급(높음/중간/낮음)** 을 붙였다. "미검증"을 뭉뚱그리면 어디를 먼저
  확인해야 하는지 알 수 없다. 가장 위험한 지점(§5 에러 코드 표)을 명시적으로 경고했다.
[결정] XgtFenetCodec.IsDraft = true 상수로 미검증 상태를 코드에 박고, UI 가 이를 읽어 경고 배너를 띄운다.
  검증 완료 시 false 로 바꾸면 경고가 사라진다 — 상태와 표시가 어긋날 수 없다.
[결정] **FrameRecorder — 수신 프레임 자동 캡처.** 사용자가 "랩뷰는 이미 정상 동작 중"이라고 확인해 주었으므로
  LabVIEW 프레임이 곧 정답이다. 그 프레임을 얻으려면 원래 nc 캡처 절차가 필요했는데,
  시뮬레이터가 접속 시 알아서 fixtures/labview-capture/ 에 저장하게 하여 **검증 루프가 스스로 닫히게** 했다.
  같은 모양은 1회만 저장(폴링으로 수천 건 쌓이는 것 방지), 응답은 .actual 로 저장(.expected 는 사람 확정본 전용).
[버그 수정·문서] 초안 §7 예제 프레임의 Length 값(12)이 산술 오류였다 — 바이트 나열은 요청 16 / 응답 14 였다.
  프레임 벡터를 테스트로 먼저 쓴 덕에 바로 잡혔다. 구현이 옳고 문서가 틀렸던 경우.
[버그 수정·UI] 워치 탭의 `$parent[ItemsControl].((vm:MainWindowViewModel)DataContext)` 조상 캐스팅 바인딩이
  런타임 타입 해석에 실패해 **탭을 열면 앱이 죽었다.** 행이 자기 RemoveCommand 를 갖도록 바꿔 해결.
  → 이 부류를 놓친 원인: TabControl 내용이 지연 생성되므로 뷰모델만 조작하는 테스트는 선택되지 않은 탭의
  DataTemplate 을 실체화하지 않는다. **TabRenderTests 를 추가해 전 탭을 실제로 렌더**하게 했다.
  발견은 스크린샷 도구가 했다 — 문서 생성이 테스트 역할을 겸한 사례.
[결정] 주소 워치(PRD 범위 확장, 사용자 요청): 임의 IEC 주소를 사용자가 추가·제거하고 값을 직접 읽고 쓴다.
  LabVIEW 는 대부분 %M 영역과 교신하는데 그 주소는 I/O 랙 매핑에 나타나지 않아 기존 UI 로는 볼 수 없었다.
  표시 형식 5종(10진/부호/16진/2진/ON·OFF), 입력도 10진·0x16진·음수·ON/OFF 를 받는다. .nxp 에 저장.
  AD 채널과 같은 "외부 변경만 반영" 규칙을 써서 주기 갱신이 입력 중 텍스트를 되돌리지 않게 했다.
[미검증] **Length 필드 의미·CPU Info 값·BCC 범위·에러 코드 표·2블록 이상 쓰기 배치** 는 여전히 미확인.
  LabVIEW 를 한 번 붙이면 자동 캡처로 1~3번이 즉시 확정된다. 에러 코드 표는 매뉴얼 필요.
[미검증] Windows 실제 기동 여전히 미확인(빌드 호스트 macOS).
Next: LabVIEW 접속 → 자동 캡처로 spec 검증 → IsDraft=false

## M11 — 워치 실수·엔디안 + 임의 디지털 점 (사용자 지시) · 2026-07-30
Status ✅ (LabVIEW 검증 성공 보고 이후 개선)
Files: src/Nxs.Core/Configuration/{WatchValue,WatchEntry,DigitalPointEntry,NxpProject,NxpProjectFile}.cs,
  src/Nxs.Core/Memory/{DataSize,IecAddress,PlcMemory}.cs, src/Nxs.Core/Protocol/Xgt/XgtFenetCodec.cs,
  src/Nxs.App/ViewModels/{WatchRowViewModel,CustomDigitalPointViewModel,DisplayOption,MainWindowViewModel}.cs,
  src/Nxs.App/Views/MainWindow.axaml, tests/Nxs.Core.Tests/Configuration/{WatchValueTests,WatchEntryTests}.cs,
  tests/Nxs.App.Tests/{CustomDigitalPointSmokeTests,WatchListSmokeTests,TabRenderTests}.cs,
  tests/Nxs.Integration.Tests/XgtFenetEndToEndTests.cs
Tests 375/375 (Core 223 + Integration 61 + App 91) · 빌드 경고0/에러0

[결정] **워치 값을 uint 대신 메모리 바이트로 다룬다.** 엔디안은 본질적으로 바이트 순서 문제이고
  Double 8바이트는 32비트에 담기지 않는다. WatchValue 가 "MSB 우선 바이트열"을 내부 표현으로 통일하고
  4가지 순서 치환(ABCD/DCBA/BADC/CDAB)을 적용한다. 모든 치환이 자기 역변환이라 정방향/역방향에 같은 함수를 쓴다
  (그 성질을 테스트로 고정).
[결정] 워드오더 표기를 자매 프로젝트(nexys-modbus-workbench) 관례에 맞춰 ABCD/DCBA/BADC/CDAB 로 썼다.
  A·B·C·D 는 **메모리에 놓인 순서**를 가리킨다. 기본값 DCBA(리틀엔디안) = 기존 ReadScalar 동작과 동일 → 회귀 없음.
[결정] Double 지원을 위해 **DataSize.LWord(8바이트) + %ML 주소**를 추가했다. XGT 데이터 타입 0x0004 가
  마침 LWord 라 코덱에서도 함께 지원하게 했다(실행기는 이미 바이트 기반이라 수정 불필요).
  M1 골든 벡터는 X/B/W/D 와 %ZW10 실패만 검사하므로 영향 없음.
[결정] ReadScalar/WriteScalar 는 LWord 에서 명확한 예외를 던지고, 대신 ReadRaw/WriteRaw(바이트) 를 추가했다.
  32비트 API 가 64비트를 조용히 잘라내는 것보다 낫다.
[결정] 형식 콤보에 **폭에 맞는 형식만** 노출한다 — 2바이트 주소에서 Double 을 고를 수 있으면 혼란만 준다.
  1바이트 주소는 바이트 순서 콤보를 아예 숨긴다(순서가 무의미).
[결정] enum 을 콤보에 직접 바인딩하니 "Dcba" 처럼 뜻이 전달되지 않았다 → DisplayOption<T> 래퍼로
  "DCBA (리틀엔디안)", "실수 Float (4B)" 같은 한국어 이름을 붙였다. 값 변환기 등록보다 단순하다.
[결정] 사용자 지정 디지털 점(DigitalPointEntry)에 Mode(Input/Output)를 두어 한 목록이 두 탭에 나뉘어 보인다.
  Input = 토글이 메모리에 반영(마스터가 읽음), Output = 마스터가 쓴 값을 LED 표시(조작 불가).
  **양쪽 모두 외부 변경을 표시에 반영**하므로 불리언 ON/OFF 를 양방향으로 검증할 수 있다.
  비트 주소가 아니면 추가 단계에서 거절한다(%MW320 같은 워드 주소를 디지털 점으로 넣을 수 없다).
[수정] DocShots 표본에서 값을 넣은 뒤 바이트 순서를 바꿨더니 hex 가 뒤집혀 보였다 — 순서가 파싱에도 쓰이므로
  **순서를 값보다 먼저** 적용해야 한다. 동작은 정상(같은 바이트, 다른 해석)이었고 표본 순서만 바로잡았다.
[검증] Float 3.14159274 ↔ IEEE754 0x40490FDB 를 4가지 순서 전부로 왕복, Double 전정밀도 왕복,
  LabVIEW 가 %MD500 에 쓴 실수를 워치가 해석하는 e2e, 커스텀 비트 양방향 e2e 를 테스트로 고정.
Next: —

## M12 — 디지털 탭 사용자 정의 전환 + 비트 배열 + 접속 표시등 (사용자 지시) · 2026-07-30
Status ✅
Files: src/Nxs.Core/Configuration/DigitalPointEntry.cs, src/Nxs.Core/Protocol/Xgt/XgtFenetCodec.cs(IsDraft=false),
  src/Nxs.App/ViewModels/{DigitalPointGroupViewModel,MainWindowViewModel}.cs (CustomDigitalPointViewModel 삭제),
  src/Nxs.App/{App.axaml,Views/MainWindow.axaml}, DESIGN.md(색 예외 기록),
  tests/Nxs.Core.Tests/Configuration/DigitalPointEntryTests.cs,
  tests/Nxs.App.Tests/{DigitalPointGroupSmokeTests,TabRenderTests,WatchListSmokeTests}.cs
Tests 410/410 (Core 250 + Integration 61 + App 99) · 빌드 경고0/에러0

[결정] 디지털 탭에서 **랙 슬롯 고정 표시를 제거**하고 사용자 정의만 남겼다. 랙 매핑은 spec 미확정
  가정값(슬롯 256점)에 의존하는데, 사용자가 직접 주소를 넣는 방식이 더 확실하고 실사용에 맞다.
  InputSlots/OutputSlots 컬렉션은 VM 에 남아 있으나(AD 탭이 AnalogSlots 를 쓰므로 구조 유지) UI 에 바인딩하지 않는다.
[결정] **비트 전용 제한 해제 → 폭만큼 비트 배열로 펼친다.** %MX=1 · %MB=8 · %MW=16 · %MD=32 · %ML=64.
  비트 0 이 시작 바이트의 최하위 비트다 — 리틀엔디안 저장과 일치하며 M1 골든 벡터(%MW0 비트0 = %MX0)를 따른다.
  DigitalPointEntry.BitAddressOf 가 절대 비트 주소를 만들고, 그룹 VM 이 기존 DigitalPointViewModel 을 비트마다 재사용한다.
  워드 그룹에 전체 ON/OFF 버튼을 넣었다(16·32비트를 하나씩 누르는 건 비현실적).
  그룹 헤더에 현재 값을 16진으로 표시 — 비트 배치와 값을 동시에 확인할 수 있다.
[결정] '값 자동화'·'랙 구성' 탭 제거(사용자 요청). **자동화 코어는 남겼다** — .nxp 로 정의하고
  AutomationEngine·테스트가 그대로 살아 있으나 UI 시작 버튼이 없어 지금은 기동할 수 없다.
  탭을 되살리면 즉시 복구된다(제거는 XAML 한 블록).
[결정] '미검증 초안 코덱' 배너 제거 + XgtFenetCodec.IsDraft = false.
  근거: 사용자가 실제 LabVIEW 로 현장 검증을 수행해 접속·읽기·쓰기 정상을 확인했다(spec §8 절차 C).
  **다만 spec §5 에러 상태 코드 표는 여전히 미확인**이다 — 거절 응답의 코드 값이라 정상 경로 검증으로는
  확인되지 않는다. 이 사실을 코드 주석·spec·README 에 남겼고 ErrorCodeMap 으로 교정 가능하다.
  UI 경고만 내리고 기록은 유지한 것이다(사용자는 배너 제거를 요청했고, 사실 기록은 지우지 않았다).
[결정] **접속 표시등에 초록불** — DESIGN.md 가 팔레트 외 색을 금지하지만("녹색 LED 등") 사용자가
  명시적으로 요청했다. 3단계로 만들었다: 정지=빈 원 / 수신 중=보조텍스트 채움 / 접속됨=초록(#2E7A4B)+발광.
  와인레드 단일 액센트로는 "수신 중"과 "접속됨"을 구분할 수 없어 실제로 정보가 부족했다.
  **적용 범위를 Ellipse.statusLamp 로 한정**하고 데이터 LED(출력 점)는 와인레드 규율을 유지했다.
  DESIGN.md §"시뮬레이터 고유 요소의 색 규칙"에 승인된 예외로 기록했다(골든 벡터 절은 건드리지 않음).
  기존 팔레트 대조 테스트(OutputLedUsesOnlyAccentAndLineBrush…)는 그대로 통과한다 — 데이터 LED 는 안 건드렸다.
[검증] %MW320=0x95EB → 켜진 비트 00,01,03,05,06,07,08,10,12,15 가 화면과 정확히 일치(스크린샷 대조).
  %QW10=0x8125 출력 LED 도 동일 확인. 탭 5개만 남았는지·초안 배너가 사라졌는지 렌더 테스트로 고정.
  스크린샷 생성 시 실제로 서버를 켜고 클라이언트를 붙여 초록불이 들어온 상태를 찍었다.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요)

## M13 — A/D 사용자 지정 전환 + 한글 글자 잘림 수정 (사용자 지시) · 2026-07-30
Status ✅
Files: src/Nxs.Core/Configuration/{WatchValue,AnalogPointEntry,NxpProject,NxpProjectFile}.cs,
  src/Nxs.App/ViewModels/{AnalogPointViewModel,MainWindowViewModel}.cs,
  src/Nxs.App/{App.axaml,Views/MainWindow.axaml},
  tests/Nxs.App.Tests/{AnalogPointSmokeTests,TabRenderTests}.cs
Tests 424/424 (Core 250 + Integration 61 + App 113) · 빌드 경고0/에러0

[버그 수정] **한글이 monospace 클래스에서 위쪽이 잘렸다** — "정지"가 "성시", "건"이 "선"으로 보였다.
  원인: Consolas 에 한글 글리프가 없어 대체 폰트로 넘어가는데 줄 높이는 Consolas 메트릭으로
  잡혀 어센더가 잘린다. 사용자 스크린샷 표시로 발견.
  조치: 한글이 들어가는 바인딩에서 mono 제거 — ConnectionText·ServerStatusText·TrafficSummary·
  Subtitle·CaptureSummary·ProjectPath·ScaleText. DESIGN 은 monospace 를 **데이터**(주소·hex·값·프레임)에만
  요구하므로 이 변경이 오히려 규칙에 더 부합한다. 남은 mono 바인딩을 전수 감사해 ASCII 전용임을 확인했다.
  추가로 mono 폰트 후보 순서를 Consolas 우선으로 바꾸고(배포 대상이 Windows), TextBox.mono 에
  MinHeight 34 · Padding 10,7 을 줘 디센더 여유를 확보했다.
[결정] A/D 탭도 디지털과 같은 **사용자 지정 주소 방식**으로 전환. 랙 슬롯 5·6 고정 표시를 없앴다.
  AnalogPointEntry(주소 + 스케일 + 바이트 순서)를 새로 두고 .nxp `analogPoints` 절에 저장한다.
  기존 슬롯 기준 AnalogChannelSettings(`analogChannels`)는 **자동화 룰의 공학단위 스케일 공유**에
  여전히 쓰이므로 남겼다(BuildAutomationRules). 역할이 다르다는 점을 주석에 명시.
[결정] 추가 카드에서 스케일(raw 최소/최대 · 단위 최소/최대 · 단위)을 직접 입력하게 했다.
  스케일 없이 주소만 받으면 채널이 무의미하다. 기본값 0~4000 ↔ 0~10 V 를 미리 채워 둔다.
  스케일 변경은 제거 후 재추가 또는 .nxp 편집 — 행마다 4개 숫자를 인라인 편집하면 화면이 산만해진다.
[결정] raw 는 부호 있는 정수로 다룬다(WatchValue.ToSigned/FromSigned 추가). 양극성 센서(-10~+10V)의
  음수 raw 를 지원하려면 필요하고, 바이트 순서도 함께 적용된다.
  주소 폭을 벗어난 raw 는 범위를 알려주며 거절한다.
[검증] 스케일 변환을 스크린샷으로 실측 대조: 6.25/10×4000=2500 · 132.5/250×4000=2120 · 1850/4000×400=185.
  전 탭 렌더 테스트에 A/D 탭 실체화 검사 추가.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요)

## M14 — LabVIEW 쓰기 실패 원인 해소 + 주소 입력 정규화 · 2026-07-30
Status ✅
Files: src/Nxs.Core/Protocol/Xgt/XgtFenetCodec.cs, src/Nxs.Core/Memory/AddressInput.cs,
  src/Nxs.App/ViewModels/{MainWindowViewModel,DigitalPointGroupViewModel,WatchRowViewModel,AnalogPointViewModel}.cs,
  src/Nxs.App/{App.axaml,Views/MainWindow.axaml}, spec/xgt-fenet-reference.md,
  tests/Nxs.Core.Tests/Memory/AddressInputTests.cs, tests/Nxs.Core.Tests/Protocol/Xgt/XgtFenetCodecTests.cs,
  tests/Nxs.App.Tests/{LowAddressAndEditingTests,DigitalPointGroupSmokeTests,AnalogPointSmokeTests}.cs
Tests 473/473 (Core 280 + Integration 61 + App 132) · 빌드 경고0/에러0

[근본 원인] **LabVIEW → 시뮬레이터 쓰기 실패.** 사용자가 "낮은 번지가 안 된다"고 보고했으나
  재현 테스트로 파서·뷰모델·낮은 주소 모두 정상임을 먼저 확인했다(10/10 통과). 그 뒤 사용자가
  "랩뷰에서 PLC 쪽으로 쓰는 게 안 된다"고 정정해 주어 진짜 원인을 찾았다.
  spec 초안 §3 에서 **신뢰도 '낮음'으로 표시해 둔 바로 그 지점**이었다: 개별 쓰기 값 구간에
  블록별 DataSize(2바이트)가 있다고 가정했는데, 크기 필드 없이 값만 오는 프레임을 받으면
  **값의 앞 2바이트를 길이로 오독**해 Slice 가 범위를 벗어나고 "데이터부 해석 실패"로 거절했다.
  값이 0xFFFF 면 65535바이트를 읽으려 하는 식이다.
[결정] **프레임 길이로 배치를 판별한다 — 추측을 없앴다.** 헤더 Length 가 데이터부 크기를 정확히 주므로
  이름 구간 뒤 남은 바이트가 N×(2+요소크기) 면 크기필드 있음, N×요소크기 면 없음으로 확정된다.
  두 값은 절대 같을 수 없으므로(2>0) 해가 유일하다. 어느 쪽과도 안 맞으면 무엇이 안 맞는지 수치로
  알리며 거절한다 — 조용히 오독하는 것보다 정확히 거절하는 편이 낫다.
  이로써 초안에서 가장 위험했던 가정 하나가 **가정이 아니게** 되었다.
[결정] **주소 입력 정규화(AddressInput).** 한글 IME 전각 문자(％ＭＷ０)가 화면에서 거의 같아 보이는데
  파서는 거부하므로 "왜 안 되는지 모르는" 증상이 된다. 전각→ASCII 접기 · 공백 제거 · 대문자화 ·
  선행 % 보충을 적용했다. 실패 시 받은 문자의 **코드포인트를 표시**해 눈에 안 보이는 문자를 드러낸다.
[결정] **출력 점도 사용자가 직접 켤 수 있게 했다.** 감시 전용으로 두면 마스터가 읽을 %Q 값을 만들
  방법이 없다 — 실장비에서는 PLC 프로그램이 %Q 를 쓰지만 시뮬레이터에서는 사람이 그 역할을 해야 한다.
  LED 모양을 유지한 클릭 가능 토글(ledToggle)로 만들고 전체 ON/OFF 도 붙였다.
  입력/출력의 차이는 표시 방식과 관용적 용도뿐이다.
[버그 수정] 200ms 주기 갱신이 **입력 중인 텍스트를 되돌려 캐럿을 뺏는** 문제. 마스터가 같은 주소를
  폴링하며 쓰는 상황에서는 실제로 입력이 불가능해진다. 마지막 키 입력 후 1.5초는 사용자에게
  우선권을 준다(워치·A/D 행 모두).
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요)

## M15 — 디지털 입력/출력 통합 (사용자 지시) · 2026-07-30
Status ✅
Files: src/Nxs.Core/Configuration/{DigitalPointEntry,NxpProject}.cs,
  src/Nxs.App/ViewModels/{DigitalPointGroupViewModel,MainWindowViewModel}.cs,
  src/Nxs.App/{App.axaml,Views/MainWindow.axaml}, tools/Nxs.DocShots/Program.cs, README.md,
  tests/Nxs.App.Tests/{DigitalPointGroupSmokeTests,TabRenderTests,LowAddressAndEditingTests,VisualTokenTests}.cs,
  tests/Nxs.Core.Tests/Configuration/DigitalPointEntryTests.cs,
  tests/Nxs.Integration.Tests/XgtFenetEndToEndTests.cs
Tests 474/474 (Core 280 + Integration 61 + App 133) · 빌드 경고0/에러0

[결정] **디지털 입력·출력 탭을 하나로 합쳤다("디지털 I/O").** M14 에서 출력도 쓰기 가능하게 만든 뒤로
  두 탭의 차이는 표시 방식뿐이었다 — 나눠 둘 근거가 사라졌다. DigitalPointMode 열거형과 Mode 속성을
  제거해 모델도 단순해졌다. 기존 .nxp 의 `"mode": "Input"` 키는 System.Text.Json 이 무시하므로 그대로 열린다.
[결정] 비트 표시를 LED + 번호 토글(ledToggle)로 통일했다. LED 는 상태를 즉시 읽히게 하고
  토글 버튼은 누를 수 있음을 알린다 — 양방향이라는 성격에 맞는 단일 표현이다.
[버그 수정] **켜진 비트의 번호가 보이지 않았다.** Fluent 가 checked ToggleButton 의 Foreground 를
  흰색(#FFFFFF)으로 바꾸는데 ledToggle 은 배경을 밝은 CardSoft 로 유지하므로 흰 글자가 사실상 사라졌다.
  스크린샷을 눈으로 검토해 발견. checked 상태에서 Foreground 를 잉크색으로 되돌리고 테두리를 와인레드로
  주어 켜짐을 이중으로 표시했다. 대비 회귀 테스트(CheckedLedToggleKeepsReadableText)로 고정.
[확인] RestartAfterStopBindsAgain 이 전체 실행에서 1회 실패했으나 단독 2회 통과 — 포트 재사용 플레이키이며
  이번 변경과 무관하다(서버 코드 미수정). 기록만 남긴다.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요)

## M16 — 연속 쓰기 길이 판별 + 트래픽 로그 주소·방향 필터 (사용자 지시) · 2026-08-03
Status ✅
Files: src/Nxs.Core/Protocol/Xgt/XgtFenetCodec.cs, src/Nxs.Core/Protocol/FrameExchange.cs,
  src/Nxs.Core/Diagnostics/{TrafficEvent,TrafficFilter,TrafficLog}.cs,
  src/Nxs.Core/Server/PlcTcpServer.cs,
  src/Nxs.App/ViewModels/{MainWindowViewModel,TrafficRowViewModel}.cs,
  src/Nxs.App/Views/MainWindow.axaml, tests/Nxs.TestKit/TestOnlyFrameCodec.cs,
  tools/Nxs.DocShots/Program.cs, README.md, spec/xgt-fenet-reference.md,
  tests/Nxs.Core.Tests/{Protocol/Xgt/XgtFenetCodecTests,Diagnostics/TrafficFilterTests}.cs,
  tests/Nxs.App.Tests/TrafficFilterUiTests.cs
Tests 507/507 (Core 300 + Integration 61 + App 146) · 빌드 경고0/에러0

[증상] "시뮬 → 랩뷰는 되는데 반대(랩뷰 → 시뮬 쓰기)가 안 된다."
[추론] 읽기가 되므로 헤더 20바이트와 데이터부 앞 8바이트 해석은 맞다. 남은 미처리 후보는
  **연속 쓰기(데이터 타입 0x0014)** 뿐이었다 — 개별 쓰기는 M14 에서 길이 판별로 고쳤지만
  연속 쓰기는 여전히 2바이트를 무검증으로 개수로 읽고 그 길이만큼 잘라내고 있었다.
  마스터가 개수 필드를 붙이지 않으면 데이터 첫 2바이트를 길이로 오독해 Slice 가 범위를 벗어난다.
[결정] **연속 쓰기도 프레임 길이로 개수 필드 유무를 판별한다.** 남은 바이트 수를 알고 있으므로
  선행 2바이트가 `남은 바이트 - 2` 와 정확히 같을 때만 개수 필드로 인정하고, 아니면 남은 전체를
  데이터로 본다. 데이터가 0바이트면 무엇이 비었는지 밝히며 거절한다.
  연속 **읽기**에도 같은 종류의 경계 검사(남은 바이트 < 2)를 넣었다 — 짧은 프레임에 예외를 던지지 않는다.
[결정] **주소를 요약 문자열에서 파싱하지 않는다.** 트래픽 필터를 만들며 요약 문자열을 검색하는 방식이
  가장 손쉬웠지만, 요약 문구를 고치면 필터가 조용히 깨진다. FrameExchange·TrafficEvent 에
  `Addresses` 를 실어 코덱이 이미 파싱한 주소를 그대로 넘긴다.
[결정] **방향 필터는 3택 열거형(RxAndTx / RxOnly / TxOnly)** 으로 두고 체크박스 2개로 나누지 않았다.
  둘 다 끈 상태(아무것도 안 보임)라는 무의미한 조합이 존재할 수 없게 만든다.
  연결 수립/해제 같은 방향 없는 Note 행은 `RX + TX 함께` 에서만 보이고 주소 필터가 걸리면 숨는다 —
  주소를 추적하는 중에 주소 없는 행이 섞이면 필터가 안 듣는 것처럼 보인다.
[결정] 주소 필터 입력도 M14 의 AddressInput 정규화를 거친다. 전각 `％ｍｗ３２０` 도 받고,
  중복은 조용히 무시하지 않고 이미 있다고 알린다.
[결정] 합성 코덱(TestOnlyFrameCodec)도 Addresses 를 채웠다. 스크린샷에서 주소 열이 비어 있으면
  기능이 없는 것처럼 보이는데, 그건 코덱이 안 채워서였다 — 서버 계층 회귀 테스트도 같은 이득을 본다.
[버그 수정] 트래픽 표의 "시각" 열 폭이 좁아 밀리초가 잘렸다(118→134). 스크린샷 검토로 발견.
[미확인] 연속 쓰기 수정이 실제 랩뷰 쓰기 실패를 해소했는지는 현장 확인이 필요하다.
  재현되면 자동 캡처된 `fixtures/labview-capture/req_*.txt` hex 가 답을 준다.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요) · 랩뷰 쓰기 현장 재확인

## M17 — A/D 채널 raw 형식·바이트 순서 선택 (사용자 지시) · 2026-08-11
Status ✅
Files: src/Nxs.Core/Configuration/{WatchValue,AnalogChannelScale,AnalogPointEntry}.cs,
  src/Nxs.App/ViewModels/AnalogPointViewModel.cs, src/Nxs.App/Views/MainWindow.axaml,
  tools/Nxs.DocShots/Program.cs, README.md,
  tests/Nxs.Core.Tests/Configuration/WatchValueNumberTests.cs,
  tests/Nxs.App.Tests/AnalogFormatSelectionTests.cs
Tests 545/545 (Core 325 + Integration 61 + App 159) · 빌드 경고0/에러0

[증상] A/D 탭에서 %MD220 의 raw 가 999999961, 공학단위가 2499999.903 으로 표시됐다.
  A/D 는 raw 를 부호 있는 정수로만 읽고 있었는데, 마스터가 그 워드에 IEEE754 실수를 넣으면
  실수의 비트 패턴을 정수로 읽은 값이 나온다 — 스케일 환산이 무의미해진다.
[결정] **워치와 같은 형식·바이트 순서 선택을 A/D 채널마다 둔다.** 값 기준을 맞추는 문제는
  워치에서 이미 같은 방식으로 해결했다 — 같은 문제에 다른 UI 를 두면 배울 것이 두 배가 된다.
  ON/OFF 는 아날로그 채널에 의미가 없어 목록에서 뺐고, 폭에 맞지 않는 형식(2바이트 주소의 Float)은
  애초에 노출하지 않는다.
[결정] **표시와 계산을 한 곳에서 해석한다.** Render/Parse 는 문자열을 다루지만 스케일 환산에는
  수치가 필요하다. 두 경로가 갈라지면 화면 값과 메모리 값이 어긋나므로
  WatchValue.ToNumber/FromNumber 를 추가해 같은 형식·순서 해석을 공유하게 했다.
  정수 형식에서는 반올림하고, 폭에 담기지 않는 값은 조용히 자르지 않고 null 로 거절한다.
[결정] AnalogChannelScale 에 **실수 오버로드**(ToEngineering(double) · ToRawValue)를 더했다.
  기존 정수 경로는 그대로 두어 호출부 동작이 바뀌지 않게 하고, 실수 raw 채널만 새 경로를 쓴다.
  ToRawValue 는 반올림하지 않고 raw 경계로만 클램프한다 — 소수부를 살리는 것이 목적이므로.
[버그 수정] **2진 형식이 화면의 표기를 되받지 못했다.** Render 는 "0001 0010 0011 0100" 을 내는데
  Parse 는 그 문자열을 10진으로 읽으려다 실패했다. 즉 2진으로 보이는 값을 고쳐 쓸 수 없었다 —
  A/D 에 형식 선택을 노출하면서 드러난 기존 결함이다. 공백·`_`·`0b` 접두를 받아 2진으로 파싱한다.
  (부작용: 2진 형식에서 "1234" 같은 10진 입력은 이제 거절된다. 2진 필드에 10진을 받는 편이
  오히려 조용한 오해였다.)
[결정] 저장된 형식이 주소 폭과 맞지 않으면(손으로 고친 .nxp 등) 예외 대신 폭에 맞는 첫 형식으로
  되돌린다 — 프로젝트 하나 때문에 앱이 열리지 않는 편이 더 나쁘다.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요) · 랩뷰 쓰기 현장 재확인

## M18 — 쓰기 길이는 이름이 아니라 프레임이 정한다 (현장 로그로 확정) · 2026-08-14
Status ✅
Files: src/Nxs.Core/Protocol/{PlcResponse,PlcRequestExecutor}.cs,
  src/Nxs.Core/Protocol/Xgt/XgtFenetCodec.cs,
  src/Nxs.App/ViewModels/{MainWindowViewModel,TrafficRowViewModel}.cs,
  src/Nxs.App/Views/MainWindow.axaml, tests/Nxs.TestKit/TestOnlyFrameCodec.cs,
  tools/Nxs.DocShots/Program.cs, README.md, spec/xgt-fenet-reference.md,
  tests/Nxs.Core.Tests/Protocol/{WriteWidthFromFrameTests,PlcRequestExecutorTests}.cs,
  tests/Nxs.Core.Tests/Protocol/Xgt/XgtFenetCodecTests.cs,
  tests/Nxs.App.Tests/TrafficFilterUiTests.cs
Tests 567/567 (Core 344 + Integration 61 + App 162) · 빌드 경고0/에러0

[방법 변경] **이번에는 추측하지 않았다.** 사용자가 보낸 현장 트래픽 로그 한 줄에 답이 있었다:
  "개별 쓰기 1블록: %MW000=02 00 00 00 · DataSizeMismatch".
  이 요약은 **성공 경로의 문구**다 — 즉 프레임 해석은 통과했고 거절한 것은 코덱이 아니라 실행기였다.
  M14·M16 에서 고친 것은 전부 코덱의 프레임 해석부였으니, 애초에 다른 곳을 보고 있었던 셈이다.
[원인] 실행기가 `값 길이 == 이름의 폭` 을 요구했다. 이름 %MW000 은 2바이트인데 마스터는 4바이트를
  보낸다 — 마스터는 데이터 타입으로 폭을 정하고 변수명은 시작 위치로만 쓴다.
  (%MW000 에 4바이트 = %MW000 + %MW001. 사용자가 그 두 워드를 테스트 중이었다는 사실과 일치한다.)
[결정] **시작 위치는 이름이, 길이는 실제 온 바이트 수가 정한다.** 이름 폭을 근거로 거절하면
  마스터가 보낸 데이터가 통째로 버려진다. 메모리 범위만 확인하고 온 만큼 쓴다.
  비트 주소는 예외로 남긴다 — 여러 바이트를 비트에 얹을 방법이 없다.
[결정] **거절에 설명을 붙였다(PlcResponse.Detail).** 이번 왕복이 길어진 진짜 이유는
  로그에 "DataSizeMismatch" 한 단어만 있어서 무엇과 무엇이 안 맞는지 알 수 없었다는 점이다.
  판정한 쪽이 그 자리에서 숫자를 적으면 트래픽 로그 한 줄로 원인이 드러난다.
  이제 "거절 · RangeExceeded — %MW99999 에서 4바이트 쓰기가 메모리 범위를 벗어납니다" 로 남는다.
[결정] **트래픽 행을 고르면 프레임 전문이 펼쳐진다.** 표의 raw hex 열은 잘려서, 화면을 캡처해도
  프레임을 읽을 수 없었다. 고른 행의 전문을 선택·복사 가능한 형태로 아래에 펼친다 —
  다음 실패는 캡처 한 장으로 진단할 수 있어야 한다.
  주기 갱신이 선택을 놓지 않도록 같은 사건을 다시 고른다(읽는 중에 패널이 닫히지 않게).
[변경] 기존 테스트 WriteWithWrongValueLengthIsRejectedWithDataSizeMismatch 는 옛 규칙을 고정하고
  있었다. 지우지 않고 새 규칙(WriteLengthComesFromTheFrameNotFromTheNameWidth)을 단언하도록 바꿨다.
Next: spec §5 에러 코드 표 확정 (매뉴얼 필요) · 이번 수정의 현장 확인
