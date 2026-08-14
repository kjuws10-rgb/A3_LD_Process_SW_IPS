# MELSEC 통신 및 PLC Handshake 안정성 개선 보고서

## 1. 기준 정보

| 항목 | 값 |
|---|---|
| 저장소 | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS.git` |
| 작업 시작 Branch | `agent/remove-async-task-thread` |
| 작업 시작 Commit ID | `f14084613167f5da59810f30d90ff93018cefdcd` |
| 작업 Branch | `agent/improve-melsec-handshake` |
| 작업 시작 일시 | `2026-08-14 08:22:56 +09:00` |
| Solution | `Drilling.sln` |
| Target Framework | Common/File/Regression `net8.0`, UI/UI Regression `net8.0-windows` |
| 변경 전 Debug Build | 성공, 경고 0, 오류 0 |
| 변경 전 Release Build | 성공, 경고 0, 오류 0 |
| 변경 전 Golden | 150줄, SHA-256 `5EA33F52AA0E1E63BF1B90F02156BED2C1F51472E849C6609E8D82F08629FADB` |
| 변경 후 Golden | 변경 전과 byte 단위 동일 |
| 구현 Commit ID | `0fd0e066294d211409a12c104664f2fbf56348c1` |
| PR | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS/pull/5` |
| Merge Commit | Merge 후 최종 결과에 기록 |

작업 시작 시 소스의 미커밋 변경은 없었다. 저장소에 추적 중인 `bin/obj` 빌드 산출물 변경과 추적하지 않는 `Data`, `Log`, `artifacts`, Release 빌드 산출물이 이미 있었으며, 이번 작업의 소스 변경과 분리했다. 해당 산출물은 수정 대상으로 취급하거나 Commit에 포함하지 않는다.

저장소에는 `AGENTS.md`가 없었다. `README.md`, Solution/Project 파일, 기존 두 리팩터링 보고서와 전수 조사 부록을 확인했다.

## 2. 실제 저장소 구조와 요청 용어의 대응

현재 저장소에는 다음 요청 예시의 API가 존재하지 않는다.

- `P_ID`, `M_ID`, `INTERFACE_TYPE`
- `CheckInterfaceIO()`
- `SetData()`, `GetMelsecData()`
- `mdOpen()`, `mdClose()`, `mdSendEx()`, `mdReceiveEx()`
- `mReadBitData`, `mReadWordData`, `mWriteBitData`, `mWriteWordData`
- WinForms Main Form의 `Get(P_ID)`/`Set(P_ID)`

실제 프로그램은 WPF/.NET 8이며, `JHMI_MELSEC_MAP.csv`의 문자열 ID와 Mitsubishi MC Protocol 3E Binary TCP frame을 사용한다. 따라서 존재하지 않는 Enum이나 PLC 주소를 새로 만들지 않고 실제 구조에 다음 개념을 대응했다.

| 요청 개념 | 현재 저장소의 실제 구조 |
|---|---|
| PLC ID | `ST_MELSEC_MAP_DATA.Id` 문자열 |
| Bit/Word 주소 | `ST_MELSEC_MAP_DATA.Address` |
| PLC Open/Close | `CMelsec.Open()` / `CMelsec.DeInitialize()`의 TCP 연결/해제 |
| SDK Write 반환 | MC 3E Response의 End Code `0x0000` |
| Read cycle | 성공한 MC Batch Read Response마다 증가하는 `ReadCycleNo` |
| Read buffer | 성공한 Read에서만 기록되는 `CMelsecReadSnapshot` |
| Sequence 결과 조회 | `GetWriteResult()` / `GetWriteStatus()` |
| Timeout/Retry | `JHMI_INTERFACE.csv` MELSEC 행의 `ARG4=3000`, `ARG5=1` 또는 호출 시 명시 값 |

## 3. 변경 전 호출 경로

| 호출 클래스 | 호출 함수 | PLC ID | Read/Write | Bit/Word | bCtrl | 다음 동작 | 변경 전 확인 방식 |
|---|---|---|---|---|---|---|---|
| `CInterfaceManager` | `Initialize/Connect/Reconnect/Disconnect/Destroy` | MELSEC Device 0 | 연결 | TCP | 해당 없음 | 공통 Interface 상태 변경 | 일반 텍스트 Socket 연결 상태 |
| `CMonitorStatusPollingService` | `ReadMelsecValue` | Map 행의 `Id` | Read | Bit/Word/DWord/Double/Float/String | 해당 없음 | Monitor snapshot 갱신 | 동기 MC Read 결과 |
| `CMenuMonitor` | `ExecuteMelsecWrite` | 사용자가 선택한 Write 행 | Write | 모든 Map 형식 | 해당 없음 | 즉시 성공 메시지 | Write 호출이 예외 없이 반환했는지만 확인 |
| `Drilling.Regression` | `RunProtocolFlow` | `WORD_TEST` | Write 후 Read | Word | 해당 없음 | Golden 기록 | 같은 Simulation 주소 직접 Read |

생산 Auto/Manual/Align/Review Station Sequence에는 MELSEC Write 호출이 없었다. PLC Handshake 출력의 실제 진입점은 Monitor 수동 Write뿐이었다. 존재하지 않는 Busy/Ready/Complete 주소나 Sequence를 추측해 추가하지 않았다.

## 4. 발견된 기존 문제

| No. | 파일 | 함수 | 문제 | 위험 |
|---:|---|---|---|---|
| 1 | `CMelsec.cs` | 모든 public Read/Write | 호출 Thread에서 Socket I/O를 직접 수행 | UI 정지, 다중 Thread 동시 진입 |
| 2 | `CInterfaceManager.cs` / `CMelsec.cs` | Connect/EnsureConnected | 일반 `CSocketComm`과 MC 전용 Socket이 같은 MELSEC Endpoint를 각각 열 수 있음 | PLC 채널 이중 점유 |
| 3 | `CMelsec.cs` | WriteBit/WriteWord | MC Write End Code 성공만으로 호출 완료 | PLC 반영/Readback 미확인 |
| 4 | `CMelsec.cs` | WriteBit | Merge 기준 Word Read와 Write는 lock 안이지만 명령 전체 직렬 Queue가 없음 | 서로 다른 호출 Thread의 동일 Word Bit 충돌 가능성 |
| 5 | `CMelsec.cs` | Simulation Write | Write 주소만 즉시 변경하며 별도 Readback/오래된 cycle 구분이 없음 | 실제 확인과 Simulation 자체 확인 혼동 |
| 6 | `CMelsec.cs` | Dispose | Socket만 닫고 전용 제어 Thread/Queue 개념이 없음 | 종료 중 신규 Read/Write 위험 |
| 7 | `CMelsec.cs` | Error log | Map과 Message 중심이며 Network/Station/상태/요청 번호 부족 | 실장비 장애 분석 곤란 |
| 8 | `CMelsecMapFile.cs` | LoadAll/Validate | 주소 형식, Bit 범위, MC 3-byte 주소 범위, 2Word 길이를 사용 시점까지 검증하지 않음 | 시작 후 늦은 실패 |
| 9 | `CMelsec.cs` | IsSimulation | 원본 CSV의 `SIMUL` 값과 Manager override를 OR 처리 | Live 전환 후에도 Simulation으로 판단할 수 있음 |
| 10 | `CMenuMonitor.cs` | ExecuteMelsecWrite | UI 명령이 PLC 응답까지 동기 대기하고 Write 반환만 성공으로 표시 | UI 지연 및 거짓 완료 표시 |

## 5. CMelsec 개선 내역

| 파일 | 함수/구조 | 변경 전 | 변경 후 | 검증 결과 |
|---|---|---|---|---|
| `CMelsec.cs` | Class | 일반 sealed class | `CMelsec : CtrlThread` | 중복 Start 방지, Stop/Restart 통과 |
| `CMelsec.cs` | public Read/Write | 호출 Thread에서 직접 MC I/O | 호환 API는 전용 Queue에 등록하고 `MELSEC_CONTROL` Thread에서만 실행 | Golden 동일 |
| `CMelsec.cs` | 비차단 Write | 없음 | `QueueWriteBit/Word/Double/String` | UI 즉시 반환 검증 |
| `CMelsec.cs` | 상태머신 | 없음 | `Ready → PrepareWrite → Write → WaitReadback → Confirm/Retry/Error` | Simulation/Live 로컬 MC 검증 통과 |
| `CMelsec.cs` | 결과 | 예외 또는 void | `Queued`, `Writing`, `WriteSuccess`, `WaitReadback`, `Confirmed`, `Timeout`, `CommunicationError`, `InvalidParameter`, `Cancelled` | 모든 종단 상태 검증 |
| `CMelsec.cs` | Read cycle | 없음 | 성공한 MC Read만 cycle 증가, Write 후 `MinimumReadCycle` 강제 | 오래된 값 Confirm 방지 통과 |
| `CMelsec.cs` | Snapshot | 없음 | 실제/Simulation Read 성공 시에만 저장 | Write 직후 Read snapshot 강제 갱신 0건 |
| `CMelsec.cs` | Retry | Socket 내부 연결 Retry만 존재 | Write/Readback 명령별 제한 Retry | 첫 실패 후 성공, 초과 실패 통과 |
| `CMelsec.cs` | Queue | 없음 | lock 보호 Queue, 128건 제한, 동일 진행 명령 병합 | 순서/중복/Stop 검증 통과 |
| `CMelsec.cs` | Bit Merge | 호출 단위 lock | 최신 출력 Word Read 후 한 Bit 변경, 명령 전체 직렬화 | Bit 0/15 보존 통과 |
| `CInterfaceManager.cs` | MELSEC 연결 | 일반 Text Socket + MC Socket 가능 | MELSEC는 external state를 사용하고 `CMelsec.Open`만 실제 Endpoint 소유 | 로컬 Live Open/Close/Reconnect 통과 |
| `CInterfaceManager.cs` | 종료 | 공통 Device Disconnect 중심 | 신규 요청 차단 → Queue 취소 → Thread Stop/Join → MC Socket Close → 상태 정리 | 잔류 Thread 0 |
| `CMenuMonitor.cs` | 수동 Write | 동기 Write 및 즉시 성공 | 비차단 Queue 등록, 요청 번호/QUEUED 표시, 즉시 오류는 실패 표시 | WPF Build/Regression 통과 |

기존 `ReadBit`, `WriteBit`, `ReadWord`, `WriteWord`, `ReadDouble`, `WriteDouble`, `ReadString`, `WriteString` 공개 서명은 유지했다. 기존 호출부와 Golden 동작은 바꾸지 않았으며, 중요한 새 호출부는 비차단 Queue API를 사용한다.

## 6. 실제 Map Write-Readback 매핑

`JHMI_MELSEC_MAP.csv`에는 37행이 있고 Write 행은 8개이다. 각 Write ID에서 마지막 `_WRITE`만 `_READ`로 바꾼 정확한 ID가 실제 CSV에 존재할 때만 자동 연결한다. 명시한 Readback ID도 Map, Device No, Access, Data Type을 검증한다.

| Sequence/기능 | Write ID / 주소 | Readback ID / 주소 | 형식 | 기본 Timeout | 기본 Retry | Simulation 결과 |
|---|---|---|---|---:|---:|---|
| Pause ACK | `FUNCTION_PAUSE_ACK_WRITE` / `W33458.1` | `FUNCTION_PAUSE_ACK_READ` / `W23458.1` | BIT | 3000 ms | 1 | Confirmed |
| Communication Check | `FUNCTION_COMMUNICATION_CHECK_COMMCHECK_WRITE` / `W33660` | `FUNCTION_COMMUNICATION_CHECK_COMMCHECK_READ` / `W23660` | WORD | 3000 ms | 1 | Confirmed |
| Online PPID Name | `PPID_ONLINE_PPID_NAME_WRITE` / `W36286` | `PPID_ONLINE_PPID_NAME_READ` / `W26286` | STRING | 3000 ms | 1 | Confirmed |
| Stage Speed | `PPID_STAGE_SPEED_WRITE` / `W3628E` | `PPID_STAGE_SPEED_READ` / `W2628E` | DWORD | 3000 ms | 1 | Confirmed |
| Laser Power | `PPID_LASER_POWER_WRITE` / `W36290` | `PPID_LASER_POWER_READ` / `W26290` | DWORD | 3000 ms | 1 | Confirmed |
| Laser Frequency | `PPID_LASER_FREQUENCY_WRITE` / `W36292` | `PPID_LASER_FREQUENCY_READ` / `W26292` | DWORD | 3000 ms | 1 | Confirmed |
| Align Stage X1 | `EC_SET_ALIGN_STAGE_X1_POS_WRITE` / `W349F0` | `EC_SET_ALIGN_STAGE_X1_POS_READ` / `W249F0` | DOUBLE | 3000 ms | 1 | Confirmed |
| Align Stage Y1 | `EC_SET_ALIGN_STAGE_Y1_POS_WRITE` / `W349F2` | `EC_SET_ALIGN_STAGE_Y1_POS_READ` / `W249F2` | DOUBLE | 3000 ms | 1 | Confirmed |

위 8개 주소와 CSV 열은 변경하지 않았다. Busy/Ready/Complete/Result/Abort 등의 별도 ID는 현재 CSV에 없으므로 임의 주소를 추가하지 않았다.

## 7. Write-Confirm 검증

| 항목 | Write 결과 | 새 Read cycle | Readback | 다음 Step | 결과 |
|---|---|---|---|---|---|
| Simulation Word 정상 | MC Simulation Write 성공 | 필수 | 기대값 일치 | Complete | 통과 |
| Simulation Bit ON/OFF | 성공 | 각 요청마다 필수 | 1/0 일치 | Complete | 통과 |
| 오래된 값 | 성공 | 이전 cycle은 거부 | 새 Read 후 비교 | 대기 유지 | 통과 |
| Readback 불일치 | 성공 | 반복 새 Read | 불일치 | 진행 금지 | Timeout 통과 |
| 첫 시도 불일치 | 성공 | 재시도 후 새 Read | 두 번째 시도 일치 | Complete | Retry 1회 통과 |
| 통신 오류 모사 | 실패 | 없음/실패 | 확인 불가 | 진행 금지 | CommunicationError 통과 |
| 통신 OFF 요청 | 등록 전 거부 | 없음 | 없음 | 진행 금지 | 통과 |
| 실제 MC TCP Word | End Code `0x0000` | 필수 | 별도 D Read 주소 Echo | Complete | 로컬 PLC 응답기 통과 |
| 실제 MC TCP Bit | End Code `0x0000` | 필수 | 별도 W Read 주소 Echo | Complete | ON/OFF 통과 |
| 실제 MC End Code 오류 | 오류 `0xC051` | 없음 | 없음 | 진행 금지/Offline | 통과 |
| Reconnect | TCP 재연결+초기 Read | 증가 | 정상 | 신규 명령 허용 | 통과 |

로컬 PLC 응답기는 실제 MC 3E Binary Request header, Device code, 3-byte address, point count, Word payload와 End Code를 처리한다. 이는 실제 네트워크 코드 경로 검증이며 Vendor PLC나 실장비 검증을 대체하지 않는다.

## 8. Thread 및 종료 안정성

| 항목 | 변경 전 위험 | 개선 내용 | 결과 |
|---|---|---|---|
| I/O 실행 Thread | UI/Monitor/호출자 Thread | `MELSEC_CONTROL` 하나로 제한 | 통과 |
| 중복 시작 | 전용 Thread 없음 | `CtrlThread.Start` 중복 방지 | 통과 |
| Queue 경쟁 | 명령 Queue 없음 | Count/Dequeue/Enqueue 동일 lock | 통과 |
| 동일 명령 중복 | 반복 클릭마다 실행 | 같은 ID/Readback/값의 진행 중 요청 번호 재사용 | 통과 |
| 동일 Word Bit | 호출 간 직렬 보장 불명확 | 전용 Queue와 최신 Word Read-Merge-Write | Bit 0/15 통과 |
| Stop 중 요청 | 차단 구조 없음 | `_acceptRequests=false`, 대기/진행 요청 Cancelled | 통과 |
| Stop/Restart | Socket Dispose만 | Stop/Join/Close/Clear 후 Initialize 가능 | 통과 |
| 연결 이중 점유 | 일반 Socket + MC Socket | MC Socket 단독 소유 | 코드/로컬 통신 검증 통과 |
| 프로그램 종료 | Socket/Thread 순서 불명확 | Station/Motion 이후 Interface Destroy에서 MELSEC Stop/Close | WPF 2회 종료, 잔류 0 |
| Busy loop | 해당 없음 | `CtrlThread` 2 ms 주기, Readback `POLL_MS` 존중 | 코드 검증 완료 |

## 9. CSV 및 데이터 검증

- 실제 `JHMI_MELSEC_MAP.csv` 37행 정상 로딩
- 빈 파일 거부
- 필수 열 부족 거부
- 중복 ID 거부
- `#`, `;`, `//` ID 주석 행 제외
- Bit 0과 Bit 15 허용, Bit 16 거부
- MC 3E 24-bit Device 주소 범위 검증
- BIT LENGTH 1 검증
- DWORD/DOUBLE/FLOAT LENGTH 2 이상 검증
- 숫자/Enum/Access/Direction 기존 Parser 검증 유지
- CSV Header, 열 순서, 값, 주소 변경 0건

데이터 변환은 Simulation과 로컬 MC 통신에서 다음을 확인했다.

- Word: 0, 1, `-1`의 기존 unsigned 1Word 결과
- DWord: `int.MinValue`, `int.MaxValue`
- Bit: 0, 15 및 같은 Word Merge
- Double: Scale `0.001`
- ASCII: 홀수/짝수 길이, 빈 문자열, 중간 Null 문자
- 기존 Little Endian Word 순서 유지

## 10. Log 및 오류 처리

새 Write-confirm 경로는 다음 구분을 한 번씩 기록한다.

- `[I/F][SEND]`
- `[I/F][WRITE_OK]`
- `[I/F][CONFIRM]`
- `[I/F][RETRY]`
- `[I/F][TIMEOUT]`
- `[I/F][COMM_ERROR]`
- `[I/F][COMPLETE]`
- Simulation에서는 앞에 `[SIMULATION]` 추가

통신 오류 상세에는 Network No., PC No., IO No., Station No., Endpoint, Data Size, `CMelsec` 상태, Write 요청 번호가 포함된다. Polling 중에는 성공/실패 상태 전이 이외의 Handshake Log를 반복하지 않는다. 빈 catch를 추가하지 않았다.

현재 저장소에는 PLC Handshake 전용 Alarm Code/Alarm 매핑이 없다. 기존 Alarm Code를 추측하거나 새로 만들지 않았고, 실패는 `WriteStatus`, Interface Error Log, Offline 상태와 Monitor 실패 결과로 전달한다. 실장비 사양에서 사용할 Alarm Code가 확정되면 기존 `CAlarmManager` 경로에 연결해야 한다.

## 11. 정적 검사

Roslyn syntax tree로 5개 직접 관리 프로젝트의 C# 99개 파일을 검사했다.

| 검사 항목 | 직접 작성 코드 잔여 | 비고 |
|---|---:|---|
| `async` keyword | 0 | 완료 |
| `await` expression | 0 | 완료 |
| `Task` type | 0 | 문자열의 Automation Task 명칭 제외 |
| Lambda/`=>` | 0 | 완료 |
| 익명 메서드 | 0 | 완료 |
| 식 본문/switch expression | 0 | 완료 |
| 사용자 정의 `interface` 선언 | 0 | 완료 |
| `Thread.Abort` | 0 | 완료 |
| 빈 catch | 0 | 완료 |
| `mReadBitData`/`mReadWordData` 강제 갱신 | 0 | 해당 구조 자체 없음 |
| `mdSendEx`/`mdReceiveEx` 외부 호출 | 0 | SDK를 사용하지 않는 MC TCP 구현 |
| `CheckInterfaceIO` 무한 대기 | 0 | 해당 함수 없음 |

`using System.Threading.Tasks;`와 `using System.Linq;`의 불필요한 직접 선언은 대상 파일에 없다. 기존 코드의 named method 기반 LINQ와 .NET Framework Interface는 금지 문법으로 분류하지 않았다.

## 12. Build 및 회귀 결과

| 검증 | 결과 | 경고 | 오류 |
|---|---|---:|---:|
| `dotnet restore Drilling.sln` | 통과 | - | 0 |
| Debug 전체 Build | 통과 | 0 | 0 |
| Release 전체 Build | 최종 2회 검증에서 확인 | 0 | 0 |
| Golden Regression | `REGRESSION_PASS` | - | 0 |
| Golden SHA-256 | 기준/최종 `5EA33F...FADB` 동일 | - | 0 |
| WPF Regression | `WPF_REGRESSION_PASS` | - | 0 |
| WPF Simulation Smoke | 2회 시작/정상 종료, `Laser Drilling`, Responding=True | - | 0 |
| 종료 후 `Drilling.UI` Process | 0 | - | 0 |
| Roslyn Audit | 금지 syntax node 0 | - | 0 |

## 13. 미검증 및 실장비 확인 항목

다음은 검증 완료로 표시하지 않는다.

1. 실제 MELSEC PLC `192.168.0.10:5000` 연결과 PLC Channel 수
2. Network No. `0x00`, PC No. `0xFF`, IO No. `0x03FF`, Station No. `0x00`의 PLC 프로그램 일치
3. 실제 8개 Write 주소에서 대응 Read 주소로 Echo/반영되는 PLC Ladder 동작
4. `FUNCTION_PAUSE_ACK_*`의 실제 Handshake 순서와 PLC Request OFF 조건
5. Busy/Ready/Complete/Result/Abort 신호의 주소와 Alarm Code: 현재 CSV/코드에 정의 없음
6. 실제 PLC 부하에서 `POLL_MS`, 3000 ms Timeout, Retry 1회의 적정성
7. 케이블 단절, PLC 전원 OFF, Station 변경, 재연결 후 실제 Socket 자원 회수
8. 실장비 장시간 반복 운전의 CPU/메모리/Handle 추세

위 항목은 `실장비 검증 필요` 또는 `PLC 프로그램 확인 필요`이다. 주소나 정책을 추측해 코드에 넣지 않았다.

## 14. Git 결과

| 항목 | 결과 |
|---|---|
| Branch | `agent/improve-melsec-handshake` |
| 변경 파일 | Common 2, File 1, UI 1, Regression 1, 문서 1 |
| 구현 Commit ID | `0fd0e066294d211409a12c104664f2fbf56348c1` |
| Push | `agent/improve-melsec-handshake` 원격 Push 성공 |
| PR | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS/pull/5` 생성, 자체 Diff 검토 완료 |
| Merge | 모든 검증 통과 후 수행 |

`bin`, `obj`, `.g.cs`, `.g.i.cs`, `Data`, `Log`, `artifacts`는 Commit에서 제외한다. 강제 Push, History 재작성, `reset --hard`, `clean`은 사용하지 않는다.
