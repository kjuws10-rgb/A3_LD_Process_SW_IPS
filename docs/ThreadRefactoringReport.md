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
| `async` | 409 | 미확정 | 작업 중 |
| `await` | 739 | 미확정 | 작업 중 |
| `Task` 형식 토큰 | 704 | 미확정 | 작업 중 |
| `Task<T>` | 284 | 미확정 | 작업 중 |
| `Task.Run` | 16 | 미확정 | 작업 중 |
| `Task.Delay` | 15 | 미확정 | 작업 중 |
| `ContinueWith` | 1 | 미확정 | 작업 중 |
| `ValueTask` | 0 | 0 | 완료 |
| `Parallel` | 0 | 0 | 완료 |
| 람다 `=>` | 0 | 0 | 완료 |
| `Thread.Abort` | 0 | 0 | 완료 |
| 사용자 정의 `interface` 선언 | 0 | 0 | 완료 |
| `Thread` 토큰 | 3 | 미확정 | 기존 짧은 Protocol 지연 |

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

## 7. 진행 기록

이하 단계별 Build, 회귀, 안정성 결과와 최종 Git 정보를 작업 중 계속 갱신한다.

