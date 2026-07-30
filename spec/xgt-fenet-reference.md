# xgt-fenet-reference.md — XGT FEnet 전용 프로토콜 근거 (사람이 채움. 부재 시 M2 blocked)
채울 내용 (LS XGT FEnet I/F 사용설명서에서 발췌, 출처 페이지 명기):
1. 애플리케이션 헤더 레이아웃 (필드·크기·값: Company ID, PLC Info, CPU Info, 프레임 방향, Invoke ID, 길이, 체크섬 등)
2. 명령 코드 표 (개별/연속 읽기·쓰기 요청/응답), 데이터 타입 코드 표(비트/바이트/워드/더블워드/연속)
3. 변수명 표기 규칙 (직접변수 ASCII, 개수 필드)
4. 에러 상태 코드 표
5. 예제 프레임 최소 2쌍 (요청+응답, 매뉴얼 기재 해석 포함)
6. 접속 포트·프로토콜(TCP/UDP) 기본값
※ 추가 검증 소스: 완성된 LabVIEW 코드를 더미 TCP 리스너에 접속시켜 요청 프레임 캡처 → fixtures/labview-capture/에 저장 (README_KIT 절차 참조)
