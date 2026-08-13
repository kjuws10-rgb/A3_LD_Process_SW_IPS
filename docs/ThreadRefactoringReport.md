# A3 Laser Drill Process SW 스레드 구조 리팩터링 보고서

## 1. 변경 전 기준 정보

| 항목 | 기준 값 |
|---|---|
| 대상 저장소 | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS.git` |
| 기준 작업 트리 | 현재 연결된 작업 트리 1개만 사용 |
| 작업 시작 Branch | detached HEAD (`origin/main`과 동일) |
| 작업 시작 Commit ID | `9b33c16ee102a6879acb4411ba781771ec0759d4` |
| 작업 Branch | `agent/remove-async-task-thread` |
| 작업 시작 일시 | `2026-08-14 05:28:18 +09:00` |
| Target Framework | Common/File/Regression: `net8.0`, UI/UI.Regression: `net8.0-windows` |
| Solution | `Drilling.sln` |
| 시작 프로젝트 | `Drilling.UI` (`WinExe`, WPF) |

이 작업의 기능 비교 기준은 위 Commit의 소스, 빌드, Golden 회귀 결과 및 WPF 실행 결과뿐이다. 다른 저장소, 별도 원본, 과거 프로젝트는 조회하거나 비교에 사용하지 않는다.

### 1.1 Solution 및 Project

| 프로젝트 | 용도 |
|---|---|
| `Drilling.Common` | 장치·통신·Motion·Manager·Station·좌표·Alarm·Log 도메인 |
| `Drilling.File` | Recipe/Setting/INI 대응 CSV/Script/Product/Review 파일 입출력 |
| `Drilling.UI` | WPF Shell, 메뉴, Popup, 화면, 상태 Polling |
| `Drilling.Regression` | 파일·좌표·Process Plan·Script·통신·Alarm·Log Golden 회귀 |
| `Drilling.UI.Regression` | WPF Binding·Button Command·좌표 표시 회귀 |

### 1.2 시작 시점 작업 트리

작업 시작 시 운영 소스의 사용자 변경은 없었다. 이전 빌드에서 생성된 다음 항목만 존재했으며 이번 Commit 범위와 분리한다.

- 세 운영 프로젝트의 `obj` 아래 NuGet 생성 파일 12개가 수정 상태였다.
- Common/File/UI의 Release 출력과 Regression 두 프로젝트의 `bin`, `obj`, `artifacts`가 추적되지 않은 상태였다.
- 이 생성 결과는 삭제·원복·Commit하지 않는다.

### 1.3 변경 전 Git History

| 순서 | Commit |
|---:|---|
| 1 | `9b33c16 Merge pull request #2 from kjuws10-rgb/refactor/remove-lambda-interface-docs` |
| 2 | `ab9465a docs: finalize refactor report after main verification` |
| 3 | `d056467 Merge pull request #1 from kjuws10-rgb/refactor/remove-lambda-interface` |
| 4 | `068f9b9 docs: record pull request self-review` |
| 5 | `6c38cb5 docs: record refactor pull request link` |

## 2. 변경 전 빌드 및 실행 기준

| 검증 | 결과 | 경고 | 오류 |
|---|---|---:|---:|
| `dotnet restore Drilling.sln` | 성공 | - | 0 |
| Debug 전체 Build | 성공 | 0 | 0 |
| Release 전체 Build | 성공 | 0 | 0 |
| Golden Regression | `REGRESSION_PASS` | - | 0 |
| WPF Regression | `WPF_REGRESSION_PASS` | - | 0 |
| WPF 시작 | `Laser Drilling` 창 표시 확인 | - | 0 |
| WPF 종료 | `Alt+F4` 후 대상 창 0개 확인 | - | 0 |

Golden 결과는 150줄이며 SHA-256은 `5EA33F52AA0E1E63BF1B90F02156BED2C1F51472E849C6609E8D82F08629FADB`이다. 기준 실행 결과는 `artifacts/thread-refactor-baseline.txt`에 생성했으며 생성물이라 Commit에는 포함하지 않는다.

Windows 캡처 API는 현재 PC에서 WPF 창 테두리 인터페이스 오류 `0x80004002`를 반환했다. 따라서 UI pixel 비교는 수행할 수 없으며 XAML diff, WPF 회귀, 창 표시/종료 및 메뉴 자동화로 보완한다.

## 3. 주요 UI, Driver 및 Sequence

### 3.1 주요 WPF 화면

- Main Shell: `CRootView.xaml`, `CRootView`
- MAIN: `CMainView.xaml`, `CMenuMain`
- MANUAL: `CManualView.xaml`, `CMenuManual`
- RECIPE: `CRecipeView.xaml`, `CMenuRecipe`
- SETTING: `CSettingView.xaml`, `CMenuSetting`
- ALARM: `CAlarmView.xaml`, `CMenuAlarm`
- MONITOR: `CMonitorView.xaml`, `CMenuMonitor`
- REVIEW: `CReviewView.xaml`, `CMenuReview`
- CORRECTION: `CCorrectionView.xaml`, `CMenuCorrection`
- PM: `CPmView.xaml`, `CMenuPm`
- Popup: Interface 상태, Password, Recipe 이름, 값 입력 Dialog

### 3.2 주요 장치·통신 Driver

| 영역 | 주요 클래스 |
|---|---|
| 전체 Interface | `CInterfaceManager`, `CInterfaceDevice`, `CCommBase` |
| Serial | `CSerialComm` |
| Socket Client/Server | `CSocketComm`, `CSocketServerComm` |
| Laser | `CTalonLaser` |
| Scanner/Automation1 | `CAutomationManager`, `CAutomation1Comm` |
| Scanner Amp/Stage | `CMotionManager`, `CMotionController`, A3200/ACS/AJIN/PMAC/UMAC/XPS Driver |
| Attenuator | `CConex_AGP` |
| Beam Expander | `CBeamExpander` |
| Power Meter | `CPowerMeter` |
| Chiller | `COrionChiller` |
| Pico Motor/DOE | `CPicoMotorService`, `CPicoMotorCommandSession` |
| PLC/CIM | `CMelsec`, Socket Server/Client Interface |

### 3.3 주요 Sequence

- Station Auto: `CStationProcess.Start`, Process step 생성/진행/완료/정지/오류
- Manual: `CMenuManual`의 Center/Position/Shape/Laser/Script 명령
- Power Measurement/Calibration: `CMenuMonitor`의 Power Meter Process/Step/측정 흐름
- Review: `CReviewManager`, `CMenuReview`의 Start/Retry/Offset 적용
- APC/Correction: `CMenuCorrection`의 Review offset 계산·적용·저장
- Scanner: Automation1 Script Build/Upload/Run/Buffered Run/Stop
- Alarm/Interlock: `CAlarmManager`, `CInterLockManager`, Station Stop/Reset 경로

## 4. 변경 전 기능 회귀 기준 목록

| 기능 | 진입 함수 | 주요 클래스 | 입력 | 출력 | 상태 변화 | 완료 조건 | 오류 조건 |
|---|---|---|---|---|---|---|---|
| 프로그램 시작 | `CApp.OnStartup` | `CAppStartup`, `CManager` | Config root | Main Window, 시작 Log | Manager 초기화 | Main 창 표시 | 초기화 예외/시작 실패 |
| 프로그램 종료 | Main Window Close | `CApp`, `CManager` | 종료 요청 | 장치/창 종료 | Running→Stopped | 프로세스와 작업 종료 | 자원/Thread 잔류 |
| 설정 읽기/저장 | `CSettingManager.LoadSection/SaveSection` | `CSettingFile` | Tab, Parameter | CSV 값/History | Loaded/Modified | 파일 반영 | CSV/경로/권한 오류 |
| Recipe 생성/열기/저장/삭제/복사 | `CMenuRecipe` 명령 | `CRecipeManager`, `CJhmiRecipeFile` | Recipe/PPID/좌표 | Recipe CSV | 선택·수정 상태 | 재로딩 동일 | 중복/형식/파일 오류 |
| Manual Scan 설정 | `CMenuManual` 명령 | `CManualScanFile` | Scan 설정 | `.scan` 파일 | 선택·수정 상태 | 재로딩 동일 | 형식/경로 오류 |
| Product/변경 이력 | `CProductManager` | `CProductFile` | 공정/Head 결과 | Active/History CSV | Created→Running→Complete/Error | 결과 저장 | 저장/상태 오류 |
| Process Plan | `CStationProcess.PrepareProcessPlan` | `CRecipeHolePlan`, Script builder | Recipe/설정/좌표 | Head/Cell/Hole Plan | NotCreated→Created | 12 Head preview/plan 생성 | 좌표/Recipe 검증 오류 |
| 좌표 변환 | Recipe/Review 계산 함수 | `CCellPointCalculator`, `CReviewCoordinateTransformer` | Design/Global/Stage/GX/GY | 보정 좌표 | 없음 | Golden 값 일치 | 범위/축/단위 오류 |
| Automation Script | Build/Upload/Run | `CAutomationManager`, `CAutomation1ScriptFile` | Plan/Head/Task | Script/Controller 명령 | Created→Running→Complete | 모든 Task 완료 | Controller/Timeout/Task 오류 |
| Auto Start | `CStationProcess.Start` | `CStationManager` | Process Plan/옵션 | 공정 상태/Log | PreCheck→Align→Optic→Process→Inspection→Complete | 모든 활성 Step 완료 | Interlock/장치/Timeout |
| Stop/Cycle Stop/Reset | Station 명령 | `CStationProcess` | Stop/Reset 요청 | 안전 명령/상태 | Running→Stopped/Ready | Laser/Script/Motion 정지 | 정지 명령 실패 |
| Motion | `CMotionManager` 명령 | 각 Motion Driver | Axis/위치/속도 | Axis 상태 | Servo/Home/Move/Stop | InPosition/상태 응답 | 통신/Servo/Timeout |
| Laser | Interface 명령 | `CTalonLaser` | Head/명령/값 | 응답/상태 | On/Off/Shutter/Gate | ACK/조회 응답 | Alarm/Timeout/Parsing |
| Chiller | Interface 명령 | `COrionChiller` | Run/Stop/온도 | Frame/상태 | Stop/Run/Pump | 응답 상태 일치 | CRC/Alarm/Timeout |
| Attenuator | Interface 명령 | `CConex_AGP` | 위치/Home/Stop | 위치/상태 | Ready/Moving/Error | 목표 위치/상태 | 장치 오류/Timeout |
| Beam Expander | Interface 명령 | `CBeamExpander` | Mag/Div/Home/Stop | 위치/상태 | Ready/Moving/Error | 목표 위치/상태 | 장치 오류/Timeout |
| Power Meter | Interface/PM 명령 | `CPowerMeter`, `CMenuMonitor` | Process/Step/파장 | 측정값/파일 | Ready→Measure→Complete | Step 결과 저장 | 통신/범위/Timeout |
| Pico Motor/DOE | Monitor 명령 | `CPicoMotorService` | Motor/위치/속도 | 위치/상태 | Idle/Moving/Error | 목표 위치/Stop | 연결/Timeout/중복 |
| Melsec/PLC | Monitor/Interface 명령 | `CMelsec` | Device/주소/Word | Read/Write 값 | Online/Simulation/Error | 정상 응답 | Packet/연결/Timeout |
| Socket/Serial 재연결 | `CInterfaceManager.Reconnect` | Comm Driver | Interface No | 연결 상태 | Offline→Online/Simulation | 연결 상태 갱신 | 연결 실패/Timeout |
| Review | `CReviewManager.Start` | Review Manager/UI | Rule/Point/이미지 | 검사 결과/Offset | Ready→Running→Complete/Error | 대상 Point 완료 | Vision/Rule/Timeout |
| APC/Correction | Correction 명령 | `CMenuCorrection` | Review Result | 보정 Offset/Recipe | Calculated→Applied | 저장 후 재로딩 동일 | 결과 부족/범위 오류 |
| Alarm/Interlock | 상태 Refresh/Reset | Alarm/Interlock Manager | IO/장치/Station | Alarm Code/문자열 | Clear↔Occur | 조건 해제/Reset | 조건 지속/장치 오류 |
| Log | `CLogManager.Write` | Log Manager | Category/Message | 날짜별 Log | Append | 기존 형식/경로 | 경로/권한 오류 |
| UI 메뉴/Popup | `CRootView.SelectMenu` | 각 Menu/Popup | Click/선택 값 | View/상태 | CurrentScreen 변경 | 해당 화면 표시 | Build/Binding 예외 |
| 상태 Polling | Polling Service | Monitor/Scanner | 주기/선택 Tab | Snapshot | 주기 갱신 | Snapshot 교체 | 통신 실패/중복 Poll |

## 5. 금지 문법 최초 조사

조사 범위는 5개 프로젝트의 직접 관리 `.cs`이며 `bin`, `obj`, `.g.cs`, `.g.i.cs`는 제외했다.

| 검색 항목 | 최초 수 | 직접 코드 최종 수 | 상태 |
|---|---:|---:|---|
| `async` | 409 | 0 | 완료 |
| `await` | 739 | 0 | 완료 |
| `Task` 형식 토큰 | 704 | 0 | 완료 |
| `Task<T>` | 284 | 0 | 완료 |
| `Task.Run` | 16 | 0 | 완료 |
| `Task.Delay` | 15 | 0 | 완료 |
| `ContinueWith` | 1 | 0 | 완료 |
| `ValueTask` | 0 | 0 | 완료 |
| `Parallel` | 0 | 0 | 완료 |
| 람다 `=>` | 0 | 0 | 완료 |
| `Thread.Abort` | 0 | 0 | 완료 |
| 사용자 정의 `interface` 선언 | 0 | 0 | 완료 |
| `Thread` 토큰 | 3 | Thread 기반 구조로 전환 | 완료 |

문자열 `interface`, Task 번호/Automation Task 같은 도메인 명칭, .NET Framework 형식은 C# 금지 문법과 별도로 분류한다.

## 6. 최초 구조 위험도

| 영역 | 기존 방식 | 사용 목적 | 변경 방향 | 위험도 |
|---|---|---|---|---|
| `CStationProcess` | async/await 전체 순차 실행 | Auto 가공·Scanner·Laser·Motion | `CtrlThread` + enum/switch 상태머신, 한 Run에 한 상태 | 높음 |
| `CInterfaceManager`/Driver | Task 반환 통신 | Serial/Socket/장치 명령 | 장치/통신 지속 Thread, Queue+lock, 동기 Send/Receive | 높음 |
| `CMotionManager` | Task 기반 명령/상태 | Stage/Motion | Motion Thread + 명령 상태/Timeout | 높음 |
| Automation1 | Task.Run 및 await | Script/Scanner | Automation Thread + 명령 상태 | 높음 |
| Review/APC | async 순차 처리 | 검사·보정 | Review/Correction 상태머신 | 높음 |
| File/Recipe/Setting | Task.FromResult 중심 | 파일 I/O | 명시적 동기 메서드 | 중간 |
| Monitor/Scanner Polling | Task.Run + Delay | 상태 감시 | `CtrlThread.Run()` 주기 Poll | 중간 |
| WPF Command | async void/Task callback | UI 명령 | 이름 있는 void 이벤트/명령 등록 | 중간 |
| 단순 화면 Build | Task 반환 | ViewModel 생성 | 명시적 동기 Build | 낮음 |

## 7. 최종 스레드 변환 내역

| 파일/영역 | 클래스 | 기존 방식 | 변경 방식 | 주기 | 상태머신/Queue | 검증 결과 |
|---|---|---|---|---:|---|---|
| `Threading/CtrlThread.cs` | `CtrlThread` | 중복 시작·재시작·Stop 결과가 불명확 | 중복 시작 방지, Pause/Resume, Stop flag, wake-up, 최대 3초 Join, 재시작, Run 예외 기록 | 가변 | `EN_CTRL_THREAD_STATE` | Start/중복 Start/Pause/Resume/예외 후 생존/Stop/재시작 자동 검증 통과 |
| `Interface/CComm.cs` | `CCommBase` | 명령별 Task 및 비동기 Send/Receive | 장치 통신별 지속 Thread, 잠금 Queue, 명령 완료 Event, 동기 Send/Receive | 1 ms | Queue+lock | Simulation Protocol/연결·해제/명령 결과 Golden 통과 |
| `Station/CStationProcess.cs` | `CStationProcess` | 전체 Auto 흐름 async/await | `CtrlThread` + `EN_STATION_SEQUENCE` switch | 10 ms | PreCheck→OpticReady→PowerCheck→Align→Process→Inspection→Complete/Stop/Error | Process Plan/Script/상태 결과 Golden 통과 |
| `Station/CStationProcess.cs` | `CBufferedRunGroupThread` | Controller 그룹별 Task 병렬 실행 | Automation controller별 지속 Thread 병렬 실행 및 완료/Event 감시 | 5 ms | Controller별 요청 상태 | 기존 Controller 그룹 동시성 코드 비교 완료, 실장비 Timeout/Stop 필요 |
| `Review/CReviewManager.cs` | `CReviewManager` | UI에서 전체 Point를 동기 완료할 때까지 실행 | Point별 `CtrlThread` 상태머신 | 10 ms | Select→Stage→Vision→Measure→Wait→Apply→Complete/Stop/Error | Simulation 비차단 Start/Complete/Stop/Shutdown 자동 검증 통과 |
| `Menu/CMenuManual.cs` | `CManualOperationThread` | UI 이벤트에서 Script Build/Upload/Run/Stop 동기 호출 | 클릭 값 Snapshot + 잠금 Queue + 지속 Thread | 5 ms | Manual command enum/switch | WPF 메뉴 Build/Shutdown 통과, 실장비 Script 동작 필요 |
| `CAppStartup.cs` | `CManagerInitializationThread` | Task 기반 Manager 초기화 | 전용 초기화 Thread | 1 ms | Initialize 1회 실행 | WPF 시작/종료 및 메뉴 회귀 통과 |
| Monitor | `CMonitorStatusPollingService` | Task.Run/Delay Polling | Context/Snapshot 잠금 Poll Thread | 250 ms | 주기별 장치 상태 분기 | WPF 메뉴 회귀 및 종료 통과 |
| Scanner | `CScannerStatusPollingService` | Task.Run/Delay Polling | Scanner 상태 Poll Thread | 1000 ms | Snapshot lock | WPF 메뉴 회귀 및 종료 통과 |
| Recipe | `CRecipePreviewThread` | Task Delay debounce | due-time 기반 Preview Thread | 20 ms | 최신 요청 교체 | Recipe Golden 및 메뉴 회귀 통과 |
| Power/Pico | `CPowerMeterSequenceThread`, `CPicoOperationThread` | UI async 흐름 | enum/switch + 잠금 Queue Thread | 10 ms | 중복 명령 방지 | WPF 메뉴 회귀, Simulation 코드 검증 완료 |
| Log | `CStationLogThread` | Task 기반 Log 전달 | 잠금 Queue/flush Thread | 5 ms | Queue+lock | Log Golden 형식·경로 일치 |

운영 코드에서 `CtrlThread`를 상속하는 클래스는 12개이며, 회귀용 `CTestCtrlThread` 1개를 별도로 추가했다. 하나의 장치 명령마다 새 Thread를 만들지 않고 통신·장치·독립 제어 루프 단위의 지속 Thread를 사용한다.

## 8. 안정성 개선

| 항목 | 변경 전 위험 | 개선 내용 | 검증 결과 |
|---|---|---|---|
| 중복 Thread | 같은 Start 경로 재진입 | 살아 있는 Thread이면 Start 무시 | 자동 검증 통과 |
| Pause/Resume | 일관된 공통 구조 없음 | `ManualResetEvent` Reset/Set | 자동 검증 통과 |
| 종료/재시작 | 종료 후 참조 및 상태 불명확 | Stop flag+wake-up+Join, 종료 시 참조 정리 | 자동 검증 통과 |
| Run 예외 | Thread 전체 종료 가능 | 예외 기록 후 다음 Run 유지 | 자동 검증 통과 |
| 통신 Queue | Count/Dequeue 경쟁 가능 | 동일 lock 안에서 확인·Dequeue | 코드 검증 및 Simulation 통과 |
| 수동 명령 중복 | 연속 클릭 시 동일 명령 중복 가능 | 현재/대기 Queue의 동일 enum 거부 | 코드 검증 완료 |
| 수동 Stop | 앞선 요청 뒤에 대기 가능 | 진행 요청 Cancellation 후 Queue Clear, Stop 우선 등록 | 코드 검증 완료 |
| Review UI 정지 | Simulation Point당 대기 중 UI 블로킹 | 시간 비교 기반 Wait 상태로 분리 | 비차단 시간 및 상태 자동 검증 통과 |
| Buffered controller 동시성 | Task 제거 시 직렬화될 위험 | Controller 번호별 Thread로 기존 병렬성 유지 | 코드 흐름 비교 완료 |
| UI Cross-thread | Worker callback에서 UI 갱신 위험 | 이름 있는 Dispatcher callback 및 Binding dispatcher 전달 | WPF 회귀/실행 통과 |
| 종료 자원 | UI/Manager/통신 Thread 잔류 위험 | 메뉴→Review/Station→Motion/Interface 순서 Shutdown | 반복 실행·종료 후 프로세스 0 확인 |

## 9. 변경 전·후 기능 보존 검증

| 기능 | 변경 전 기준 | 변경 후 | 검증 방법 | 결과 |
|---|---|---|---|---|
| Recipe/CSV | 기준 Commit Golden | 필드 수·순서·escaping·값 동일 | 150줄 Golden exact compare | 통과 |
| Setting/INI 대응 데이터 | 기준 Commit Golden | 읽기/쓰기 결과 동일 | Round-trip exact compare | 통과 |
| Process Plan/좌표 | 기준 Head/Cell/Hole 결과 | 좌표·부호·정밀도 동일 | Golden exact compare | 통과 |
| Scanner Script | 기준 Script text/순서 | 파일명·명령·Point 순서 동일 | Golden exact compare | 통과 |
| 통신 Packet/명령 | 기준 Simulation 응답·Melsec 값·장치 명령 | 결과와 Log payload 동일 | Protocol Golden | 통과 |
| Alarm/Interlock | 기준 Alarm code/order/조건 | 동일 | Golden exact compare | 통과 |
| Log | 기준 경로와 payload | 동일 | Golden exact compare | 통과 |
| Simulation | 기준 연결/명령/해제 | 동일 | Golden+Review Simulation | 통과 |
| Review | 전체 실행 반환 전 UI 대기 가능 | 비차단 요청, Point 순서·3초 Simulation timing 유지 | Start 시간/Complete/Stop/Save 1회 | 통과 |
| UI 메뉴/Binding | 기준 10개 메뉴와 Binding | XAML 변경 없이 동일 메뉴 Build | WPF Regression | 통과 |
| 실제 WPF 창 | `Laser Drilling` 창 | 동일 제목·응답 상태 | 2회 시작/Alt+F4/잔류 확인 | 통과 |
| Thread | 기준 비동기 흐름 | 지속 Thread/상태머신 | Start/Stop/Pause/Resume/재시작 회귀 | 통과 |

Golden 결과는 최종에도 150줄, SHA-256 `5EA33F52AA0E1E63BF1B90F02156BED2C1F51472E849C6609E8D82F08629FADB`로 기준과 완전히 동일하다. 운영 XAML은 변경하지 않았다.

## 10. 최종 정적 검사

Roslyn C# syntax tree로 5개 프로젝트의 직접 관리 소스 99개를 파싱했다. `bin`, `obj`, `.g.cs`, `.g.i.cs`는 제외했다.

| 검색 항목 | 문자열 검색 수 | C# 문법 노드 잔여 | 분류 |
|---|---:|---:|---|
| `async` | 0 | 0 | 제거 완료 |
| `await` | 0 | 0 | 제거 완료 |
| `Task` | 183 | 0 | 문자열/Automation Task 번호·상태·속성 이름만 존재 |
| `Task<T>` | 0 | 0 | 제거 완료 |
| `Task.Run` | 0 | 0 | 제거 완료 |
| `Task.Delay` | 0 | 0 | 제거 완료 |
| `TaskCompletionSource` | 0 | 0 | 제거 완료 |
| `ContinueWith` | 0 | 0 | 제거 완료 |
| `ValueTask` | 0 | 0 | 제거 완료 |
| `Parallel` | 0 | 0 | 제거 완료 |
| 람다/화살표 `=>` | 0 | 0 | 제거 완료 |
| 익명 메서드 | - | 0 | 제거 완료 |
| switch expression | - | 0 | 제거 완료 |
| `interface` | 481 | 0 | 폴더·Manager·통신·문자열의 장비 Interface 명칭만 존재 |
| `Thread.Abort` | 0 | 0 | 제거 완료 |

## 11. Build 및 자동 회귀 결과

| 검증 | 최종 결과 | 경고 | 오류 |
|---|---|---:|---:|
| `dotnet restore Drilling.sln` | 성공 | - | 0 |
| Debug 전체 Build 1차/2차 | 성공 | 0 | 0 |
| Release 전체 Build 1차/2차 | 성공 | 0 | 0 |
| Golden Regression 1차/2차 | `REGRESSION_PASS` | - | 0 |
| WPF Regression 1차/2차 | `WPF_REGRESSION_PASS` | - | 0 |
| Roslyn syntax audit 1차/2차 | 금지 문법 노드 0 | - | 0 |
| WPF Smoke 2회 | 창 표시·정상 종료·잔류 프로세스 0 | - | 0 |

새 경고는 0개이다. 회귀 과정에서 확인된 오류는 Review UI 블로킹 가능성 1건과 Buffered controller 직렬화 위험 1건이었고 모두 수정 후 재검증했다.

## 12. BAT 파일

- `Git_Pull.bat`: 저장소 루트에서 실행하면 `pull_build_run.bat`를 호출한다. Git/dotnet 확인, 현재 Branch/detached HEAD/작업 트리 확인, `git pull --ff-only`, Restore, Release Build, WPF 실행을 순서대로 수행한다. 변경 파일이 있으면 Pull 전에 안전하게 중단한다.
- `Git_Push.bat`: 저장소 루트에서 실행하면 `push_current_branch.bat`를 호출한다. Git/origin/Branch/detached HEAD를 확인하고 상태를 표시하며, 원격보다 앞선 Commit만 같은 Branch로 Push한다. 최초 Push는 upstream을 설정한다. `Git_Push.bat --dry-run`은 Fetch와 모든 안전 조건만 확인하고 원격을 변경하지 않는다.
- 두 파일과 내부 구현에는 credential, 고정 PC 경로, `reset --hard`, `clean`, `--force`, `--force-with-lease`, 자동 Commit이 없다.
- Pull BAT는 변경 파일이 있는 현재 작업 트리에서 실제 실행하여 안전 중단을 확인했다. Push BAT는 원격 변경을 만들기 전 정적 검증을 완료하고, 최종 Branch Push는 동일 내부 명령 경로로 수행한다.

## 13. 미검증 및 실장비 추가 확인 항목

다음 21개 범주는 코드/Simulation 검증을 완료했지만 실제 장비 또는 외부 시스템 없이는 통과로 판정하지 않았다.

1. Talon Laser 실제 On/Off, Shutter/Gate, Alarm 복구
2. Automation1 Scanner 8 Head Script Upload/Run/Stop 및 Controller별 동시 Buffered Run
3. Scanner Amp와 실제 GX/GY 이동·홀수/짝수 Head 방향
4. 실제 Stage Y/Home/Stop/InPosition/Servo 및 Cycle Stop
5. Attenuator Home/위치/Stop/Timeout
6. Beam Expander(DOE Z 포함) Mag/Div/Home/Stop
7. DOE Tilt/Pico Motor 연결·재연결·위치·All Move·Stop
8. Power Meter 파장·측정·Process/Step 반복
9. Power Calibration 결과와 설비 기준기 비교
10. Vision Shot/Review 측정/재측정/외부 Vision 응답 Parsing
11. APC/Correction 보정 적용 방향과 실측 결과
12. Serial 장치 CRC/terminator/Timeout/재연결
13. Ethernet/Socket Client 실제 단절·재연결
14. Socket Server 다중 Client 연결·종료
15. PLC/Melsec 실제 Word/Bit 주소와 Packet
16. CIM 외부 Host 명령/ACK/재전송
17. Auto Start 전체 물리 Sequence와 Interlock
18. Manual IOF 및 Manual P-to-P 실제 가공
19. MOF/Align/보호윈도우 교체 실제 기구 순서
20. Emergency/Stop/Cycle Stop 시 Laser·Motion·Scanner 안전 정지 시간
21. 장시간 반복 운전 CPU/메모리 추세 및 종료 후 장치 Handle/Thread 잔류

Vendor SDK 내부의 Interface/Task 형식은 수정하지 않았다. 사용자 코드에서는 해당 비동기 형식을 확산하지 않고 동기 API와 전용 Thread 경계에서 처리한다.

## 14. Git 결과

| 항목 | 결과 |
|---|---|
| 기준 Commit | `9b33c16ee102a6879acb4411ba781771ec0759d4` |
| 작업 Branch | `agent/remove-async-task-thread` |
| 주요 작업 Commit | `d7fe6e8`, `43dc141`, `20e837d`, `5ccea3b`, `8ecb200` |
| 변경 파일 수 | 직접 관리 파일 70개 |
| Push | `origin/agent/remove-async-task-thread` 성공 |
| Pull Request | `https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS/pull/3` |
| Merge Commit | `4fa49aefc017882c832f00426664b573583e1864` |
| 최종 main Commit | `4fa49aefc017882c832f00426664b573583e1864` (기능 리팩터링 병합 기준) |
