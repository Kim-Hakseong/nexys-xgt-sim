# Nexys XGT Simulator — 킷 사용법
## 실행: ~/Haku/builds/nexys-xgt-sim/에 킷 복사 → claude → "PROMPT_ralph.md 를 읽고 그대로 수행해." (세션1=마일스톤1, M1~M8)
## 사람이 할 일 (M2 전, 중요)
1. **spec/xgt-fenet-reference.md 채우기** — XGT FEnet I/F 매뉴얼 발췌 (회사 자료, 이 킷의 최중요 입력)
2. **LabVIEW 캡처 (장비 불필요, 30분)**: 아무 TCP 서버(예: `nc -l 2004 > cap.bin` 또는 파이썬 소켓 로거)를 띄우고 완성된 LabVIEW 앱의 접속 IP를 localhost로 → 읽기/쓰기 동작 수행 → 수신된 요청 프레임을 fixtures/labview-capture/req_*.bin으로 저장. 각 요청의 기대 응답을 매뉴얼 대조로 .expected에 작성 → M7 회귀가 이걸 실장비 대역으로 사용
3. spec/xgi-addressing.md 확인 (슬롯당 점수 등)
## 완료 후 검증 (5분): 시뮬레이터 실행 → LabVIEW 접속 IP를 시뮬레이터 PC로 → 입력 토글이 LabVIEW 화면에, LabVIEW 출력 명령이 LED에 반영되면 끝.
