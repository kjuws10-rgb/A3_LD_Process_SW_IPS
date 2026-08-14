# MELSECNET/H 드라이버 및 PLC Write-Confirm 안정성 보고서

## 1. 기준 정보

| 항목 | 결과 |
| --- | --- |
| 저장소 | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS.git` |
| 작업 시작 브랜치 | `main` |
| 작업 시작 Commit | `9e92b80e4d1df3a5d5cbe2938eb56773179665a5` |
| 작업 브랜치 | `agent/use-melsecnet-driver` |
| 구현 Commit | `8f25e4fe8bc2ab1aae6b0befdeea532807757aea` |
| Pull Request | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS/pull/6` |
| 작업 시작 일시 | 2026-08-14 KST |
| Solution | `Drilling.sln` |
| Target Framework | Common/File/Regression `net8.0`, UI/UI Regression `net8.0-windows` |
| 변경 전 Debug/Release | 오류 0, 경고 0 |
| 변경 전 Golden Regression | `REGRESSION_PASS` |

작업 시작 시 Source 외에 기존 Build 출력, `Data`, `Log`, `artifacts`가 변경 또는 추적되지 않은 상태로 존재했다. 이 파일들은 사용자 작업으로 간주하여 원복, 삭제, Commit하지 않았다.

## 2. 변경 방향

기존 `CMelsec`의 Mitsubishi MC 3E Binary TCP 구현은 제거했다. PLC 통신 경로는 Mitsubishi MELSEC Data Link Library의 다음 API만 사용한다.

- `mdOpen`
- `mdReceiveEx`
- `mdSendEx`
- `mdClose`

공식 API 경계는 `CMelsecNetApi` 한 클래스에 모았다. Native library 이름은 Mitsubishi 문서의 `MDFUNC32.dll`이며, Vendor SDK 파일 자체는 저장소에 복사하거나 수정하지 않았다.

`mdOpen`, Read, Write, `mdClose`는 모두 `CMelsec : CtrlThread`의 `MELSEC_CONTROL` 스레드에서 실행된다. UI나 Sequence 호출 스레드는 동기 요청 또는 Write Queue만 등록하며 Native API를 직접 호출하지 않는다.

## 3. 발견된 기존 문제와 처리

| No. | 위치 | 기존 문제 | 위험 | 처리 |
| --- | --- | --- | --- | --- |
| 1 | `CMelsec.cs` | TCP Socket과 MC 3E frame을 직접 작성 | 사용할 MELSECNET/H Driver와 불일치 | Socket, frame header, command/end code 처리 전체 제거 |
| 2 | `CMelsec.cs` | MC 전용 Device Code 사용 | `mdSendEx/mdReceiveEx`의 Device Type과 값 불일치 | Mitsubishi Data Link Library Device Type으로 교체 |
| 3 | `JHMI_INTERFACE.csv` | MELSEC가 `SOCKET_C`와 IP/Port로 설정 | `mdOpen` Channel을 표현할 수 없음 | `MELSEC_NET` 및 Channel/Network/Station 인자로 교체 |
| 4 | 회귀 검증 | 로컬 TCP MC 응답기로 통신 경로 검증 | 제거 대상 코드를 테스트가 보존 | 가짜 `CMelsecNetApi`로 Native 호출 계약 검증 |
| 5 | 초기화/종료 | Driver Path 수명과 전용 Thread 수명 불일치 가능 | 닫힌 Path Read 또는 다른 Thread에서 Close | 신규 요청 차단, Queue 취소, 같은 Thread의 `mdClose`, Join 순서 적용 |
| 6 | SDK 반환값 | Native 성공과 PLC Readback 성공을 혼동할 수 있음 | 실패 후 Sequence 진행 | 명령 접수/Write 성공/새 Read Cycle/Readback Confirm 상태 분리 |

## 4. CMelsec 개선 내역

| 파일/함수 | 변경 전 | 변경 후 | 검증 |
| --- | --- | --- | --- |
| `CMelsecNetApi.cs` | 없음 | `MDFUNC32.dll`의 네 Native API를 명시적 P/Invoke | 가짜 API 대체로 인자·호출 순서 검증 |
| `OpenCore` | TCP Connect | `mdOpen(channel, -1, path)` 반환값 검사 후 최초 `mdReceiveEx` 성공 시 Online | Open 성공/실패, 최초 Read 검사 |
| `ReadWords` | Socket request/response frame | 로컬 byte Size를 `ref`로 전달하고 Return Code 및 실제 Size 검사 | Word/Bit/2Word/String, 오류 Return Code |
| `WriteWords` | Socket frame 전송 | `mdSendEx` Return Code 및 실제 Size 검사 | Write 성공/실패, Queue 중단 |
| `GetMelsecNetDeviceType` | MC byte Device Code | X=1, Y=2, L=3, M=4, SM=5, F=6, D=13, SD=14, R=22, B=23, W=24, SB=25, SW=28, V=30, ZR=220 | 현재 W Map 및 D/W 회귀 Map 검증 |
| `DeInitialize` | 통신과 Thread 종료 경합 가능 | 신규 요청 차단 → Queue 취소 → 제어 Thread에서 `mdClose` → Thread Join | 반복 Stop/Start, Destroy 후 잔류 0 |
| Write 상태머신 | Write 완료와 반영 완료 혼동 가능 | `Queued → Writing → WriteSuccess → WaitReadback → Confirmed` | 새 Read Cycle 이전 Confirm 금지 |

Native Size는 Word 수가 아니라 byte 수로 계산하며 호출마다 지역 변수로 전달한다. 최대 전송 크기 1920 byte와 반환된 실제 Size 일치를 검사한다.

## 5. 설정 구조

`JHMI_INTERFACE.csv`의 MELSEC 행 형식은 다음과 같다.

| 필드 | 의미 | 검증 |
| --- | --- | --- |
| TYPE | `MELSEC_NET` | 다른 형식으로 Live Open 금지 |
| ARG1 | MELSECNET/H Channel No. | 51~54 |
| ARG2 | Network No. | 0~239 |
| ARG3 | Station No. | 0~255 |
| ARG4 | Write/Readback Timeout ms | 양수 |
| ARG5 | Retry Count | 0 이상 |

현재 저장소 기본 행은 Simulation이다. 실제 Channel, Network, Station 값은 현장 보드 설정과 PLC Network Parameter가 없으므로 추측해 넣지 않고 비워 두었다. Simulation에서는 Native DLL을 호출하지 않는다. Live로 전환할 때 세 값을 먼저 입력하지 않으면 설정 검증 또는 Open 단계에서 명확하게 실패한다.

UI에는 `MELSEC_NET` 선택지만 추가했다. 기존 WPF 화면 배치, Control 이름, 크기, 색상, 메뉴 순서는 변경하지 않았다.

## 6. Write-Confirm과 Readback

기존 Write Queue와 상태머신의 다음 보장을 유지하고 MelsecNet API 경로에 적용했다.

1. 요청 번호를 발급하고 Queue에 한 번 등록한다.
2. `mdSendEx`가 0을 반환하고 Size가 일치해야 Write 성공으로 기록한다.
3. Write 전 Read Cycle보다 큰 최소 Cycle 번호를 저장한다.
4. 이후 `mdReceiveEx`가 성공해야 Read Cycle을 증가시킨다.
5. 새 Cycle의 Readback ID 값이 기대값과 일치해야 `Confirmed`가 된다.
6. 불일치는 Poll 주기마다 다시 읽되 Log를 반복하지 않는다.
7. Timeout과 Retry 횟수를 초과하면 `Timeout` 또는 `CommunicationError`로 끝내고 다음 동작을 금지한다.

Live 경로에서 Write 값을 Read Buffer에 복사하는 코드는 없다. Read Snapshot은 성공한 `mdReceiveEx` 결과로만 갱신한다. Simulation Echo는 `_simulationWords`에만 기록하고 Log에 `[SIMULATION]`을 붙인다.

현재 `JHMI_MELSEC_MAP.csv`의 PLC ID, W 주소, Bit 위치, Scale, Length, Poll 주기는 변경하지 않았다. Readback ID가 없는 출력은 같은 ID를 Confirm 대상으로 가장하지 않으며, 명시적 Pair 또는 기존 Map의 Write/Read 짝을 사용한다.

## 7. 회귀 검증

| 항목 | 검증 방법 | 결과 |
| --- | --- | --- |
| Debug Build | `dotnet build Drilling.sln -c Debug --no-restore` | 오류 0, 경고 0 |
| Release Build | `dotnet build Drilling.sln -c Release --no-restore` | 오류 0, 경고 0 |
| Golden Regression | 변경 전 Snapshot과 Recipe/File/Plan/Script/Log/Alarm 결과 비교 | `REGRESSION_PASS`, 차이 0 |
| WPF Regression | Binding, Button, 좌표, 메뉴 생성·종료 | `WPF_REGRESSION_PASS` |
| MelsecNet Config | 현재 CSV Load → Save → Reload | `MELSEC_NET` 유지 |
| Simulation | Bit 0/15, Word, DWord 경계, Double Scale, 홀수/짝수 ASCII | 통과 |
| Write-Confirm | ON/OFF, 새 Read Cycle, 불일치 Timeout, Retry 성공 | 통과 |
| Native API 계약 | Open/Receive/Send/Close, byte Size, Device Type, Return Code | 가짜 API 경로 통과 |
| Thread | Native 호출 Thread 일치, 중복 Start, Stop/Restart, Destroy | 통과 |
| 정적 문법 검사 | Roslyn 100개 사용자 C# 파일 | 금지 문법 0 |
| MC 구현 검색 | 사용자 C#/CSV/XAML의 frame/header/command/socket server 검색 | 잔여 기능 0 |

Golden Snapshot SHA-512는 변경 전과 변경 후가 동일하다.

## 8. 정적 검사 결과

| 항목 | 직접 작성 코드 잔여 |
| --- | ---: |
| Lambda/`=>` | 0 |
| 익명 메서드 | 0 |
| 식 본문 | 0 |
| switch expression | 0 |
| 사용자 정의 `interface` 선언 | 0 |
| `async`/`await` | 0 |
| `Task`/`Task<T>` | 0 |
| `Thread.Abort` | 0 |
| MC frame 생성/해석 코드 | 0 |
| Native wrapper 외부 P/Invoke 선언 | 0 |
| `CMelsec` 외부 wrapper Read/Write 호출 | 0 |

## 9. 실장비 및 Vendor 환경 확인 필요

현재 PC와 저장소에는 Mitsubishi MELSECNET/H 보드 Runtime 및 `MDFUNC32.dll`이 없다. 따라서 다음 항목은 완료로 표시하지 않는다.

- 설치된 보드 Utility에서 Channel 51~54 중 실제 Channel 확인
- 실제 Network No.와 Station No. 확인
- Application과 Vendor Runtime의 x86/x64 일치 확인
- 실제 `mdOpen`, `mdReceiveEx`, `mdSendEx`, `mdClose` Return Code 확인
- 현재 W Read/Write 영역 접근 권한 및 Link Refresh 확인
- 각 Write 주소와 Read 주소가 PLC 프로그램에서 실제 Echo/Readback Pair인지 확인
- PLC 전원 OFF, Cable 단절, 보드 오류, 재연결 시 Alarm 및 복구 확인
- 실제 Busy/Ready/Complete/Abort Handshake의 Timeout 및 Sequence 중단 확인

위 값과 PLC 프로그램이 확인되기 전까지 기본 설정은 Simulation으로 유지해야 한다.

## 10. 변경 파일

- `Config/JHMI_INTERFACE.csv`
- `Drilling.Common/Interface/CInterfaceManager.cs`
- `Drilling.Common/Interface/Melsec/CMelsec.cs`
- `Drilling.Common/Interface/Melsec/CMelsecNetApi.cs`
- `Drilling.File/JHMI/CInterfaceFile.cs`
- `Drilling.UI/Menu/Menus/CMenuSetting.cs`
- `Drilling.Regression/Program.cs`
- `docs/MelsecHandshakeStabilityReport.md`

## 11. 공식 API 기준

- Mitsubishi Electric, MELSEC Data Link Library Reference Manual: `mdOpen`, `mdClose`, `mdSendEx`, `mdReceiveEx`, Device Type, Size 및 Channel 정의
- MELSECNET/H는 동일 Path를 한 제어 Thread에서 Open/Read/Write/Close하도록 구성했다.

구현 Commit은 `8f25e4fe8bc2ab1aae6b0befdeea532807757aea`이며 PR은 `#6`이다. Merge Commit은 GitHub 작업 완료 후 최종 작업 보고에 기록한다.
