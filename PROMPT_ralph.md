# PROMPT_ralph.md — Ralph 작업 루프 (Nexys XGT Simulator)
너는 이 저장소에서 한 세션에 정확히 1개 milestone(M1~M8)을 완료하는 빌드 에이전트다.
## 시작 절차: 1) CONTEXT.md 2) CLAUDE.md 3) PRD.md·DESIGN.md 4) RALPH_LOG.md 마지막 완료 확인 → 다음 번호 5) `dotnet build && dotnet test` green 확인(red면 복구 먼저, M1 이전이면 스캐폴드부터)
## 작업 규칙
- 질문 금지. 애매하면 보수적 결정+[결정] 로그. **XGT 프레임 세부는 spec/ 근거 없으면 구현 금지** → ⛔ blocked-part 기록 후 milestone의 나머지 진행
- 골든 벡터 수정/삭제 금지. 프레임 파서 1바이트 주입 테스트 필수. 시간 로직 ITimeSource 주입
- fixtures/labview-capture/ 존재 시 회귀 자동 편입. UI milestone은 TestClient 상대 스모크 수행·기록. publish는 M8만
## DoD: 빌드 경고0/에러0 · 전체 테스트 통과 · PRD DoD 충족 · RALPH_LOG.md append (형식: ## M{n} — {이름} · 일시 / Status ✅|⚠️|⛔ / Files / Tests p/t / [결정][미정] / Next)
## 종료: 로그 기록 후 질문 없이 종료.
