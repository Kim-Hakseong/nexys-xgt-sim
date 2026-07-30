# DESIGN.md — Nexys XGT Simulator · Rev.B (비주얼 체계 포함)

## 구조
Nxs.Core(메모리/주소/프로토콜/자동화 — UI 0) + Nxs.App(Avalonia) + tests(+TestClient) + spec/ + fixtures/labview-capture/

## 메모리 모델
- 영역별 바이트 배열(I/Q/M, 크기 설정 가능 기본 각 64KB). 접근 뷰: X(비트)/B/W(리틀엔디안 워드)/D.
- IEC 주소 파서: `%<area><size><addr>` — %MW100(M 워드100), %MX801(M 비트801), %IX0.2.5(입력 base0.slot2.point5 → 절대 비트 = base×basePts + slot×slotPts + point, slotPts는 spec 확정값·기본 64). 파서는 [start,end) 바이트/비트 범위 반환(로그 하이라이트용).

## FEnet 서버
- TcpListener 멀티클라이언트, **바인딩 IP(NIC 선택)·포트 UI 설정 + .nxp 저장** (기본 포트는 spec 기재값). 클라이언트별 수신 상태머신(헤더 완독→길이만큼 완독 — 부분 수신 불변).
- 프레임 코덱은 spec/xgt-fenet-reference.md의 헤더 레이아웃·명령 코드·데이터타입 코드·에러 코드 표만 구현. Invoke/Frame ID는 요청 에코 규칙 준수(spec 기재대로).
- 연속 읽기 최대 크기·개별 읽기 최대 블록 수 등 한계값도 spec 기재 → 초과 요청은 실장비와 동일 오류.

## 값 자동화
룰=(대상 주소, 제너레이터, 주기), 제너레이터는 tickIndex 순수 함수(고정/증가/램프/사인/랜덤/토글). AD 채널은 공학단위 룰(min/max) → raw 변환(채널 설정의 스케일 공유).

## 비주얼 디자인 — Modbus Workbench 완전 동일 (Rev.B 추가)

**원본이 유일한 진실이다**: 로컬 저장소 `/Users/haku/Projects/nexys-modbus-workbench` (원격 github.com/Kim-Hakseong/nexys-modbus-workbench)의
`src/Nmw.App/App.axaml` 리소스 사전과 스타일 클래스(예: `pill`)를 **그대로 이식**한다 (같은 소유자 저장소 — 이식 허용, CLAUDE.md §2).
새 스타일 발명 금지. 아래 토큰 표는 원본에서 추출한 대조용 정답이며, 이식 후 값이 이 표와 일치해야 한다.

### 팔레트 (추출 검증 완료 — 웜 뉴트럴 그레이지 + 와인 레드)
| 역할 | 키 | 값 |
|---|---|---|
| 캔버스/배경 | — | #F4F3F0 / #ECEAE5 |
| 카드 | CardBrush / CardSoftBrush | #FCFBF9 / #F1EFEA |
| 구분선 | LineBrush | #DDDBD3 |
| 잉크(주 텍스트) | InkBrush / InkHoverBrush / TextPrimaryBrush | #16171A / #2C2E33 / #16171A |
| 보조 텍스트 | TextSecondaryBrush | #8B897F |
| **액센트(와인 레드)** | AccentBrush / hover / pressed / AccentSoftBrush | **#7A1020** / #9C2030 / #5A0B18 / #F0EAE7 |
| 오류 | ErrorBrush | #9C2030 |
| 은은한 그림자 틴트 | — | #1A26251F |

### 타이포 (원본 관례)
- UI: 시스템 산세리프. 크기 관례 11/12/14/15/19, 제목 Bold·소제목 SemiBold.
- **데이터(주소·hex·값·프레임)는 예외 없이 `Menlo,Consolas,monospace`** — 원본과 동일.

### 컴포넌트 관례 (원본 이식)
- 토글/선택 상태 = 와인레드 배경 + 흰 글자 (ToggleButtonBackgroundChecked #7A1020 계열, 탭 선택 동일)
- 상태 표시는 `pill` 클래스 Border (CardBrush 배경) 재사용
- 다이얼로그·버튼·입력 스타일 전부 App.axaml 이식분 사용

### 시뮬레이터 고유 요소의 색 규칙 (단일 액센트 규율 유지)
- 디지털 입력 토글: ON = 와인레드(#7A1020)+흰 글자, OFF = CardSoft — 기존 토글 스타일 그대로
- 출력 LED: ON = 와인레드 채움, OFF = LineBrush 테두리 빈 원 (새 색 도입 금지 — 녹색 LED 등 팔레트 외 색 금지)
- 트래픽 로그: TX/RX 구분은 잉크/보조텍스트 농도로, 오류 행만 ErrorBrush
- 접속 상태: 연결 = 와인레드 pill + 흰 글자, 미연결 = 보조텍스트 pill
- **[승인된 예외 · 2026-07-30]** 접속 표시등에 초록(#2E7A4B + 발광 #8FC7A6) 사용 —
  저장소 소유자가 "통신이 붙으면 초록불"을 명시적으로 요청했다. 통신 연결 여부는 가장 자주 확인하는
  상태이고 와인레드 단일 액센트로는 "수신 중"과 "접속됨"을 구분할 수 없었다.
  **적용 범위는 접속 표시등(Ellipse.statusLamp)뿐이며 데이터 LED(출력 점)는 와인레드 규율을 유지한다.**

## 골든 벡터 (수정 금지)
### 주소 파서 (slotPts=64 가정 — spec 확정 시 [결정] 로그로 상수만 갱신, 산식 불변)
- %MW100 → area M, word, offset 100 (byte 200..202)
- %MX801 → area M, bit 801 (byte 100 bit 1)
- %IX0.2.5 → area I, 절대 비트 = 2×64+5 = 133
- %IW0.5.0 → area I, slot5 word0 → 절대 워드 = (5×64)/16 = 20
- 오류: %ZW10 → 파스 실패(미지원 영역), %IX0.2 → 실패(형식)
### 메모리
- W→B→X 일관성: %MW0=0x0001 → %MB0=0x01, %MX0=true (리틀엔디안)
- 연속 워드 10개 쓰기→읽기 라운드트립 일치
### 자동화 (검증된 값)
- Ramp(0,100,25): tick0..5 → 0,25,50,75,100,0 · Sine(0,1000,period4): 500,1000,500,0 · Toggle: T,F,T
### 프레임 (M2에서 spec 기재 예제로 확정 후 불변 — 현재 게이트 상태)
- spec/xgt-fenet-reference.md의 예제 요청/응답 프레임 각 1개 이상 → 코덱 파스·생성 일치 + 1바이트 주입 불변
### 캡처 회귀
- fixtures/labview-capture/*.bin 존재 시: 각 요청 → 시뮬레이터 응답이 기대 응답(.expected, 사람이 매뉴얼 대조로 확정)과 일치. 부재 시 skip
### 비주얼 이식 검증 (Rev.B 추가)
- App.axaml 이식 후 리소스 값 대조 테스트(또는 스모크 체크리스트): AccentBrush=#7A1020, CardBrush=#FCFBF9, LineBrush=#DDDBD3, 데이터 표시 컨트롤 FontFamily에 monospace 포함 — 위 표와 전 항목 일치
