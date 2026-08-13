# Lambda 및 C# Interface 제거 리팩터링 보고서

## 1. 작업 식별 정보

| 항목 | 값 |
|---|---|
| 저장소 | `kjuws10-rgb/A3_LD_Process_SW_IPS` |
| 기준 main Commit SHA | `57d1fa176d0ecf4eb82e5233006244d58693c455` |
| 작업 브랜치 | `refactor/remove-lambda-interface` |
| PR | https://github.com/kjuws10-rgb/A3_LD_Process_SW_IPS/pull/1 |
| 최종 리팩터링 Merge Commit SHA | 병합 후 확정 예정 |
| 대상 솔루션 | `Drilling.sln` |
| 대상 프로젝트 | `Drilling.Common`, `Drilling.File`, `Drilling.UI` |

기준 main은 작업 시작 당시 `origin/main`과 동일했다. 별도의 새 작업 디렉터리에서 시작했으며 기존 체크아웃과 기존 사용자 변경은 건드리지 않았다. 저장소에 `AGENTS.md`는 없었고, `README.md`, 솔루션/프로젝트 파일, NuGet 설정 및 기존 BAT 파일을 확인했다.

## 2. 전수 조사와 제거 결과

Roslyn syntax tree와 semantic model을 사용해 `bin`, `obj`, `.g.cs`, `.g.i.cs`를 제외하고 조사했다. 단순 정규식 검색은 보조 검증에만 사용했다. 개별 구문의 파일, 줄, 멤버, 호출 문맥, 캡처 변수, 실행 스레드, 호출 시점, 예외 전달, 위험도 및 변경 방법은 [REFACTOR_LAMBDA_INTERFACE_INVENTORY.tsv](REFACTOR_LAMBDA_INTERFACE_INVENTORY.tsv)에 1,906건 기록했다.

| 구분 | Drilling.Common 최초 | Drilling.File 최초 | Drilling.UI 최초 | 최초 합계 | 최종 |
|---|---:|---:|---:|---:|---:|
| 람다 | 342 | 215 | 600 | 1,157 | 0 |
| 식 본문 | 51 | 4 | 469 | 524 | 0 |
| switch expression | 65 | 20 | 140 | 225 | 0 |
| switch expression arm | 438 | 118 | 720 | 1,276 | 0 |
| 익명 메서드 | 0 | 0 | 0 | 0 | 0 |
| 사용자 정의 interface 선언 | 28 | 0 | 1 | 29 | 0 |
| 사용자 정의 interface 형식 참조 | 406 | 16 | 467 | 889 | 0 |
| class/record/struct의 interface base type | 16 | 15 | 15 | 46 | 0 |

최종 Roslyn 감사 결과는 운영 프로젝트와 두 회귀 프로젝트 모두 아래 항목이 0이다.

- Lambda, anonymous method, expression-bodied member, switch expression/arm
- 사용자 정의 interface 선언 및 사용자 정의 interface 형식 참조
- class, record, struct의 interface 구현/base type
- 사용자 작성 C# 파일의 `=>` 토큰

`IReadOnlyList<T>`, `IEnumerable<T>`, `ICollection<T>` 같은 .NET BCL 컬렉션 형식과 WPF의 `IDataObject`, `IInputElement` 형식은 외부 프레임워크 API 형식이므로 유지했다. 이는 사용자 정의 계약 또는 사용자 클래스의 interface 구현이 아니다. 외부 DLL과 SDK 내부 interface도 수정하지 않았다.

## 3. 인터페이스별 기존 구현과 교체 구조

| 기존 interface | 기존 구현/사용 위치 | 실제/Simulation 용도 | 교체 구조 | 위험도 |
|---|---|---|---|---|
| `IAutomationManager` | `CAutomationManager`, Common/UI | 공용 | `CAutomationManager` 직접 사용 | 중간 |
| `IComm` | Serial/Socket/장치 통신 구현 | 실제/Simulation | 공통 구현을 `CCommBase` 추상 기본 클래스로 이동 | 높음 |
| `ICommMessageSource` | `CSocketServerComm` 수신 이벤트 | 실제 통신 | 구체 `CSocketServerComm` 형식 확인 후 동일 이벤트 연결 | 높음 |
| `IInterfaceDevice` | `CInterfaceDevice` | 실제/Simulation | `CInterfaceDevice` 직접 사용 | 높음 |
| `IBETFile` | `CBETFile` | 설정/장비 공용 | `CBETFileBase` + `CBETFile` | 중간 |
| `IInterfaceManager` | `CInterfaceManager` | 실제/Simulation | `CInterfaceManager` 직접 사용 | 높음 |
| `IMelsecMapFile` | `CMelsecMapFile` | 실제/Simulation | `CMelsecMapFileBase` + `CMelsecMapFile` | 높음 |
| `IMelsec` | `CMelsec` | 실제/Simulation | `CMelsec` 직접 사용 | 높음 |
| `IPowerMeterFile` | `CPowerMeterFile` | 실제/Simulation | `CPowerMeterFileBase` + `CPowerMeterFile` | 중간 |
| `ILogManager` | `CLogManager` | 공용 | `CLogManager` 직접 사용 | 높음 |
| `IConfigStructureFile` | `CConfigStructureFile` | 시작 검증 | `CConfigStructureFileBase` + `CConfigStructureFile` | 중간 |
| `IManualScanFile` | `CManualScanFile` | 수동 운전 | `CManualScanFileBase` + `CManualScanFile` | 중간 |
| `IRecipeFile` | `CJhmiRecipeFile` | 공용 | `CRecipeFileBase` + `CJhmiRecipeFile` | 높음 |
| `IRecipeManager` | `CRecipeManager` | 공용 | `CRecipeManager` 직접 사용 | 높음 |
| `ISettingFile` | `CSettingFile` | 공용 | `CSettingFileBase` + `CSettingFile` | 높음 |
| `IInterfaceFile` | `CInterfaceFile` | 실제/Simulation | `CInterfaceFileBase` + `CInterfaceFile` | 높음 |
| `ISettingManager` | `CSettingManager` | 공용 | `CSettingManager` 직접 사용 | 높음 |
| `IMotionManager` | `CMotionManager` | 실제/Simulation | `CMotionManager` 직접 사용 | 높음 |
| `IMotorFile` | `CMotorFile` | 실제/Simulation | `CMotorFileBase` + `CMotorFile` | 높음 |
| `IIoFile` | `CIoFile` | 실제/Simulation | `CIoFileBase` + `CIoFile` | 높음 |
| `IProductFile` | `CProductFile` | 공용 | `CProductFileBase` + `CProductFile` | 높음 |
| `IProductManager` | `CProductManager` | 공용 | `CProductManager` 직접 사용 | 높음 |
| `IReviewResultFile` | `CReviewResultFile` | Review | `CReviewResultFileBase` + `CReviewResultFile` | 중간 |
| `IReviewRuleFile` | `CReviewRuleFile` | Review | `CReviewRuleFileBase` + `CReviewRuleFile` | 중간 |
| `IReviewManager` | `CReviewManager` | Review | `CReviewManager` 직접 사용 | 높음 |
| `IAutomation1Script` | `CAutomation1ScriptFile.CAutomation1Script` | 실제/Simulation script | `CAutomation1ScriptBase` + 기존 구체 script | 높음 |
| `IAutomationScriptFile` | `CAutomation1ScriptFile` | 실제/Simulation script | `CAutomationScriptFileBase` + `CAutomation1ScriptFile` | 높음 |
| `IStationManager` | `CStationManager` | 자동/수동 시퀀스 | `CStationManager` 직접 사용 | 높음 |
| `IMenu` | UI menu classes | UI | `CMenuBase : CBindingBase`와 구체 menu classes | 높음 |

프레임워크 interface 구현도 제거했다.

| 기존 구현 | 교체 방식 | 보존 사항 |
|---|---|---|
| `ICommand` | 구체 `CButtonCommand`와 `CButtonCommandBehavior` attached property | Click 시점, CommandParameter, CanExecute/활성화 갱신, 헤더 클릭 |
| `INotifyPropertyChanged` | `CBindingBase`의 속성명별 명시적 변경 이벤트 | WPF binding 갱신 시점과 기존 `SetProperty` 호출 위치 |
| `IMultiValueConverter` | `CPreviewCoordinateBehavior` attached properties | uniform/stretch 좌표 계산 및 유효하지 않은 입력의 0 처리 |
| `IDisposable` | 구현 base list만 제거하고 기존 public `Dispose()` 유지 | 명시적 자원 해제 호출과 해제 순서 |

## 4. 주요 리팩터링 방식

- 이벤트 람다는 이름 있는 이벤트 처리 메서드로 교체했다.
- Dispatcher, Invoke, Thread, Timer, Task 콜백은 동일 호출 위치의 이름 있는 메서드 또는 local method로 교체했다. 기존 예약 방식, 대상 스레드, await/예외 흐름을 유지했다.
- LINQ 람다는 이름 있는 predicate/selector/comparer 또는 명시적 `for`/`foreach`로 교체했다. 정렬 안정성, 중복 key 예외, 열거 순서를 별도 확인했다.
- 식 본문 멤버는 블록 본문과 명시적 `return`, `get`, `set`으로 확장했다.
- switch expression은 일반 switch 문으로 교체했다. async 분기는 기존 await 및 `ConfigureAwait` 위치를 유지했다.
- 구현이 하나뿐인 Manager/Service 계약은 구체 형식으로 교체했다. 파일과 script 계약은 단순 추상 기본 클래스로 교체했다.
- Simulation 여부와 Live 분기는 기존 `SetSimulationMode`/`IsSimulation` 흐름을 유지했다. 장비 통신 `Interface` 폴더와 Manager/PLC/Scanner/Automation1/Melsec/Socket/Serial 기능은 삭제하지 않았다.

## 5. 수정 프로젝트와 파일

### Drilling.Common (39개)

`Alarm/CAlarmManager.cs`, `Automation/CAutomationManager.cs`, `InterLock/CInterLockManager.cs`, `Interface/Automation1/CAutomation1Comm.cs`, `Interface/CComm.cs`, `Interface/CInterfaceManager.cs`, `Interface/Melsec/CMelsec.cs`, `Interface/PicoMotor/CPicoMotor.cs`, `Interface/PicoMotor/CPicoMotorService.cs`, `Interface/Serial/CBeamExpander.cs`, `Interface/Serial/CConex_AGP.cs`, `Interface/Serial/COrionChiller.cs`, `Interface/Serial/CPowerMeter.cs`, `Interface/Serial/CSerialComm.cs`, `Interface/Serial/CTalonLaser.cs`, `Interface/Socket/CSocketComm.cs`, `Interface/Socket/CSocketServerComm.cs`, `Log/CLogManager.cs`, `Log/CProgramOpenLog.cs`, `Managers/CManager.cs`, `Managers/CRecipeManager.cs`, `Managers/CSettingManager.cs`, `Motion/A3200/CA3200Motion.cs`, `Motion/ACS/CACSComm.cs`, `Motion/ACS/CACSMotion.cs`, `Motion/AJIN/CAjinMotion.cs`, `Motion/CMotionController.cs`, `Motion/CMotionManager.cs`, `Motion/PMAC/CPmacMotion.cs`, `Motion/UMAC/CUmacMotion.cs`, `Motion/XPS/CXpsComm.cs`, `Motion/XPS/CXpsMotion.cs`, `Product/CProductManager.cs`, `Recipe/CCellPointCalculator.cs`, `Recipe/CRecipeHolePlan.cs`, `Review/CReviewManager.cs`, `Review/CReviewSampleRuleSelector.cs`, `Station/CStationManager.cs`, `Station/CStationProcess.cs`.

### Drilling.File (15개)

`JHMI/CBETFile.cs`, `JHMI/CConfigStructureFile.cs`, `JHMI/CInterfaceFile.cs`, `JHMI/CIoFile.cs`, `JHMI/CJhmiRecipeFile.cs`, `JHMI/CManualScanFile.cs`, `JHMI/CMelsecMapFile.cs`, `JHMI/CMotorFile.cs`, `JHMI/CPowerMeterFile.cs`, `JHMI/CReviewRuleFile.cs`, `JHMI/CSettingFile.cs`, `Parser/CCsvParser.cs`, `Product/CProductFile.cs`, `ReviewResult/CReviewResultFile.cs`, `Script/CAutomation1ScriptFile.cs`.

### Drilling.UI (42개)

`App.xaml`, `CApp.xaml.cs`, `CAppStartup.cs`, `CRootView.cs`, `CRootView.xaml`, `Menu/CBindingBase.cs`, `Menu/CButtonCommand.cs`, `Menu/Menus/CCellPreviewDrawing.cs`, `Menu/Menus/CMenuAlarm.cs`, `Menu/Menus/CMenuBase.cs`, `Menu/Menus/CMenuCorrection.cs`, `Menu/Menus/CMenuExit.cs`, `Menu/Menus/CMenuMain.cs`, `Menu/Menus/CMenuManual.cs`, `Menu/Menus/CMenuMonitor.cs`, `Menu/Menus/CMenuPm.cs`, `Menu/Menus/CMenuRecipe.cs`, `Menu/Menus/CMenuReview.cs`, `Menu/Menus/CMenuSetting.cs`, `Menu/Menus/CMonitorStatusPollingService.cs`, `Menu/Menus/CReviewGlassPreviewBuilder.cs`, `Menu/Menus/CScannerStatusPollingService.cs`, `Popup/CInterfaceStatusDialog.xaml.cs`, `Popup/CPasswordInputDialog.xaml.cs`, `Popup/CRecipeNameDialog.xaml.cs`, `Popup/CValueInputDialog.xaml.cs`, `Views/CAlarmView.xaml`, `Views/CCorrectionView.xaml`, `Views/CMainView.xaml`, `Views/CManualView.xaml`, `Views/CMonitorView.xaml`, `Views/CMonitorView.xaml.cs`, `Views/CPicoMotorMonitorView.xaml`, `Views/CPmView.xaml.cs`, `Views/CRecipeView.xaml`, `Views/CRecipeView.xaml.cs`, `Views/CReviewView.xaml`, `Views/CSettingView.xaml`, `Views/CSettingView.xaml.cs`.

추가 파일은 `CPreviewCoordinateBehavior.cs`, `Menu/CButtonCommandBehavior.cs`이고, 기존 interface converter인 `CPreviewCoordinateConverter.cs`는 삭제했다. 기존 `Menu/Menus/IMenu.cs`는 interface 제거 후 실제 형식에 맞춰 `CMenuBase.cs`로 변경했다.

### 검증 및 운영 보조 파일

- `Drilling.Regression`: Golden 동작/출력 비교
- `Drilling.UI.Regression`: WPF binding, command, 좌표 갱신 검증
- `Drilling.sln`: 두 회귀 프로젝트 포함
- `pull_build_run.bat`, `push_current_branch.bat`: 안전 검사 보강
- `docs/REFACTOR_LAMBDA_INTERFACE_INVENTORY.tsv`: 전수 조사 부록

## 6. 빌드 결과

| 시점/구성 | 오류 | 경고 | 결과 |
|---|---:|---:|---|
| 기준 main Debug | 0 | 0 | 통과 |
| 기준 main Release | 0 | 0 | 통과 |
| 리팩터링 후 Debug | 0 | 0 | 통과 |
| 리팩터링 후 Release | 0 | 0 | 통과 |

최종 검증 명령은 다음과 같다.

```text
dotnet restore Drilling.sln
dotnet build Drilling.sln -c Debug --no-restore
dotnet build Drilling.sln -c Release --no-restore
dotnet run --project Drilling.Regression/Drilling.Regression.csproj -c Debug --no-build -- artifacts/current-regression.txt Drilling.Regression/Golden/baseline-regression.txt
dotnet run --project Drilling.UI.Regression/Drilling.UI.Regression.csproj -c Debug --no-build
```

## 7. 회귀 및 기능 동일성 검증

### Golden 비교

기준 SHA와 리팩터링 후에 동일한 회귀 소스를 각각 컴파일해 같은 입력을 실행했다.

| 항목 | 결과 |
|---|---|
| Golden 줄 수 | 150 |
| 기준 SHA-256 | `5EA33F52AA0E1E63BF1B90F02156BED2C1F51472E849C6609E8D82F08629FADB` |
| 리팩터링 후 SHA-256 | `5EA33F52AA0E1E63BF1B90F02156BED2C1F51472E849C6609E8D82F08629FADB` |
| 줄 단위/순서 비교 | 완전 일치 |

검증 범위는 다음과 같다.

- Recipe 저장/재로드 값, CSV field 수·순서·따옴표 escaping·숫자 정밀도
- Setting 저장/재로드 값과 CSV 결과
- Process Plan head/cell/hole 순서, 좌표, encoder count
- Automation1 main/head script 파일명, 명령 순서, 수치 출력
- Simulation 연결/명령/상태/Disconnect 순서
- Talon, Chiller, Attenuator, BET, PowerMeter, PicoMotor, Melsec simulation 명령 결과와 로그 순서
- Chiller 송신 frame HEX, Pico axis/query 명령 문자열, Melsec word write/read 결과
- Interlock 상태, Alarm 코드·등급·문자열·해제/재발생 시각 유지
- Log 상대 경로와 payload 형식

### WPF 회귀

`Drilling.UI.Regression`을 STA/WPF로 실행해 아래를 검증했고 `WPF_REGRESSION_PASS`를 확인했다.

- 속성명별 명시적 이벤트가 실제 WPF binding target을 갱신
- `CButtonCommandBehavior` Click 1회 실행, CommandParameter 전달, CanExecute 변경 시 IsEnabled 갱신
- preview 좌표의 uniform/stretch 계산 결과

### Simulation smoke test

기준과 최종 Release 실행 모두 Simulation 모드에서 수행했다. 최종 실행 결과는 다음과 같다.

- 프로세스 생존, `Laser Drilling` window 표시, 응답 상태 `True`
- ProgramOpen 01~39 항목 모두 `Ready`
- Interface 13, Motor 0, IO 28, Melsec map 37, Active Product load 성공
- 13개 장치 instance의 `INIT_CONNECT / SIMULATION / SIMULATION`
- Station `Process plan prepared` 기록
- UI Automation tree 433개 descendant 확인
- MAIN, MANUAL, RECIPE, SETTING, ALARM, MONITOR, REVIEW, CORRECTION, PM 메뉴 선택 성공
- 주요 화면 heading 전환 확인 후 MAIN 복귀
- `CloseMainWindow` 정상 종료

Windows 화면 캡처 계층이 현재 환경에서 지원되지 않아 픽셀 단위 전후 screenshot 비교는 수행하지 못했다. 대신 XAML diff에서 layout/size/color/style의 임의 변경이 없는지 확인하고, 실제 WPF binding/command test와 UI Automation 화면 전환으로 보완했다.

### 스레드 및 예외 흐름

- 운영 코드에 새 `async`, `await`, `Task`, `Task.Run`, `Parallel`, Thread 구조를 추가하지 않았다.
- 기존 Dispatcher/Invoke/Task/Timer/Thread 호출 지점을 유지하고 delegate만 이름 있는 메서드로 교체했다.
- Task fault, 직접 호출 예외, 이벤트/Dispatcher 예외 전달 방식은 기존 호출 API를 유지했다.
- Simulation 기동, 화면 전환, polling 동작 중 UI 응답 상태와 정상 종료를 확인했다.

Roslyn 비교 수치는 아래와 같이 기준/최종이 동일하다. diff에서 새 줄로 보이는 `Task.Run`과 `async`는 기존 async lambda의 호출 지점과 본문을 이름 있는 callback으로 분리한 결과이며 실행 구조 증가는 없다.

| 항목 | 기준 | 최종 |
|---|---:|---:|
| `async` keyword | 409 | 409 |
| await expression | 739 | 739 |
| `Task.Run` invocation | 16 | 16 |
| `new Thread` | 0 | 0 |
| `new Timer` | 0 | 0 |
| statement가 없는 기존 catch | 20 | 20 |

기존 취소/timeout 경로의 statement 없는 catch는 새로 만들지 않았으며 위치와 개수 모두 그대로 유지했다.

## 8. BAT 파일 사용 방법과 검증

### `pull_build_run.bat`

저장소 루트의 파일을 더블클릭하거나 명령 프롬프트에서 실행한다. BAT 위치를 기준으로 저장소 루트로 이동한 뒤 Git/.NET/Git worktree/현재 브랜치/Detached HEAD/미커밋 변경을 확인한다. 안전할 때만 현재 원격 브랜치를 `git pull --ff-only`로 받고 restore, Release build, 실행을 수행한다. 충돌, non-fast-forward, 변경 작업 트리는 자동 해결하지 않고 중단한다.

### `push_current_branch.bat`

커밋을 사용자가 먼저 검토해 만든 뒤 실행한다. BAT 위치를 기준으로 이동하고 Git worktree, origin, 현재 브랜치, Detached HEAD, HEAD commit, 상태를 표시한다. 원격 브랜치가 있으면 fetch 후 fast-forward 관계와 보낼 commit 수를 확인한다. 최초 push는 upstream을 설정하고 이후에는 동일 이름의 원격 브랜치로 push한다. 자동 commit과 force 옵션은 사용하지 않는다.

실제 검증 결과:

- 변경이 있는 저장소에서 Pull BAT: 안전 중단, exit code 1
- 공백이 포함된 별도 로컬 저장소에서 최초 Push/upstream 설정: 통과
- 추가 1 commit Push: 통과
- 보낼 commit 없음: 원격 변경 없이 통과

## 9. 커밋 구성

- 프로젝트별 식 본문 제거 3개 commit
- 프로젝트별 switch expression 제거 3개 commit
- 프로젝트별 lambda 제거 3개 commit
- Manager/파일/나머지 사용자 interface 제거 3개 commit
- framework interface 구현 제거 1개 commit
- Golden/WPF 회귀와 protocol characterization 2개 commit
- BAT 안전성 개선 1개 commit

각 구간에서 Debug/Release build 또는 관련 회귀 검증을 수행했고, 자동 생성 파일과 `bin`/`obj` 결과는 commit에 포함하지 않았다.

## 10. 실제 설비에서 추가 확인할 항목

물리 장비와 SDK runtime이 없는 환경이므로 아래 항목은 실제 설비 연결 후 별도 확인이 필요하다. 검증하지 않은 항목을 통과로 기록하지 않는다.

- Wonik Control/Vision/Automation1/Motion/Talon/Chiller/Attenuator/BET/PowerMeter/PicoMotor/Melsec 실제 연결과 재연결
- Serial/Socket/Melsec live packet의 상대 장비 ACK/NAK 및 timeout/retry 실시간 동작
- Automation1 실제 controller script load/run/abort와 scanner/laser timing
- 실제 IO/interlock/alarm 입력에 따른 자동/수동 sequence 전이
- 장시간 polling, Dispatcher 부하, 종료 시 thread 및 SDK resource 해제
- 설비 파일 공유 경로와 운영 권한에서 Recipe/Product/Review/Log 저장

## 11. 남아 있는 제한사항

- 물리 장비 동작은 수행하지 못했으며, Simulation 및 Golden packet/command/log 비교로 대체했다.
- 화면 캡처 API 제한으로 pixel-by-pixel screenshot 비교는 수행하지 못했다.
- 외부 SDK/DLL 내부 interface와 .NET/WPF가 제공하는 컬렉션·입력 API interface 형식은 제거 대상이 아니므로 유지했다. 사용자 정의 interface 선언/참조 및 사용자 class의 interface 구현은 0이다.
