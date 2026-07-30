# CLAUDE.md — Nexys XGT Simulator (코드명 nxsim) 빌드 헌법
우선순위: CONTEXT.md → **CLAUDE.md > DESIGN.md > PRD.md**

## 1. 정체성
- LS XGT PLC(XGI CPU) 시뮬레이터: FEnet(이더넷)·Cnet(시리얼) 서버 + PLC 메모리맵 + I/O 조작 패널. 사내 도구, UI 한국어.
- 최우선 가치: **프로토콜 정합성** — LabVIEW가 실장비와 구분 못 할 것.

## 2. 기술 스택 (고정 — Modbus Workbench 검증 조합)
| 언어 | C# 12 / .NET 8 LTS | UI | Avalonia 11.1.x + CommunityToolkit.Mvvm |
|---|---|---|---|
| 테스트 | xUnit | 허용 NuGet | Avalonia.*, CommunityToolkit.Mvvm, System.IO.Ports, xunit*, Microsoft.NET.Test.Sdk, coverlet.collector |
- 자기 소유 저장소(testbench-modbus-workbench)의 시뮬레이터 설계 패턴(DataStore·값 자동화·장애 주입) 참조 허용. 외부 라이브러리 금지.

## 3. 절대 금지
- **XGT 프로토콜 프레임 구조를 기억/추측으로 구현 금지.** spec/ 폴더에 매뉴얼 발췌 또는 캡처 픽스처로 근거가 기재된 것만 구현 (조작 제로 원칙). 근거 없으면 해당 부분 ⛔ blocked-part 로그 후 나머지 진행.
- npm/Electron/LangChain류 금지. 클라우드 호출 금지. UI 스레드 블로킹 I/O 금지. 허용 목록 외 NuGet 금지.

## 4. 정확성 규칙
1. 테스트 없는 milestone 완료 없음. DESIGN 골든 벡터 수정/삭제 금지.
2. 프레임 파서는 부분 수신 불변(1바이트 주입 테스트 필수). 시간 로직은 ITimeSource 주입.
3. fixtures/labview-capture/ 에 캡처 프레임이 있으면 "캡처 요청 → 시뮬레이터 응답" 회귀 테스트 자동 편입, 없으면 skip.
4. placeholder 데이터 금지. 테스트 데이터는 코드 생성.

## 5. 완성 정의
리뷰 가능한 소스 + 통과 테스트 + RALPH_LOG.md 기록. publish는 M8에서만(win-x64 단일 exe, 확인 후 삭제).
