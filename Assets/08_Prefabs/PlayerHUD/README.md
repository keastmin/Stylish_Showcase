# Project R 플레이어 HUD

UGUI와 TextMesh Pro로 구성한 화면 고정형 플레이어 HUD다. **왼쪽 위 PlayerStatus**는 십자 아이콘과 체력 바, 낫 아이콘과 스킬 바를 쌓은 432×68 크기의 얇은 표시부다. 글자와 숫자 없이 굵기·길이·색·눈금으로 상태를 전달한다. 회피와 스킬 사용 정보는 **오른쪽 아래 ActionDock**에 두 행으로 배치했다. 기준 해상도는 1920×1080이다. ActionDock에는 회피 `SHIFT`와 스킬 `E` 키캡이 있으며, 상시 WASD·LMB 조작 안내 행은 없다. 완벽 회피 중에는 아래 중앙에 일시적으로 반격 안내를 표시한다.

## 디자인 근거

- 이전 대화 **「GroundCrack 컬러감 통일 분석」**에서 정리하고 적용한 흰색 중심광, 청록 `#BAF3FF`, 청회색 `#718A9A`를 이어받았다. 반투명 차콜 패널과 금속 낫 문양은 슬래시 중심의 전투 연출과 연결된다.
- **「완벽 회피 슬로우모션 설계」**에서 요청한 대로 UI 애니메이션은 전투 슬로우모션과 별개로 진행한다. 보간·피격 잔량·메시지 지속 시간은 Inspector에서 조절할 수 있다.
- 4타 콤보, 완벽 회피 반격, 다단 스킬과 Impact Frame 중심의 현재 전투를 기준으로 구성했다. 별도 스태미나, 점수, 미니맵, 미구현 패링은 표시하지 않는다.
- 왼쪽 위의 큰 배경 패널, `PLAYER / 01`, `VITALS STABLE`, HP 수치, 게이지 백분율과 비용 문구를 제거했다. 체력은 밝고 굵은 4분할 바, 스킬은 더 짧고 가는 바와 비용 눈금으로 구분한다. 기존 516×216 외곽에서 높이를 약 69%, 외곽 면적을 약 74% 줄였으며 두 바 사이로 배경이 보인다.
- 낫 문양은 이미지 생성 도구로 제작한 투명 PNG다. 패널, 테두리, 게이지, 숫자와 글자는 실제 UGUI/TMP 오브젝트이므로 각각 편집할 수 있다. 제작 프롬프트와 최종 색상 수정 내용은 `Art/GenerationPrompt.txt`에 기록했다.

## 저장 파일

```text
Assets/08_Prefabs/PlayerHUD/
├─ PlayerHUD.prefab                 화면 전체를 담당하는 HUD 프리팹
├─ README.md                        이 문서
├─ Art/
│  ├─ ScytheCrest.png                투명 배경의 낫 문양
│  ├─ WhitePixel.png                 빌더가 생성하는 평면 게이지용 흰색 Sprite
│  └─ GenerationPrompt.txt           이미지 생성·색상 수정 기록
├─ Preview/
│  ├─ CombatHUD_1920x1080.png         전투 프로토타입 씬의 기본 상태
│  ├─ DemoHUD_1920x1080.png           환경 데모 씬의 기본 상태
│  ├─ DemoHUD_FeedbackPreview.png     체력·게이지·피드백 샘플 상태
│  └─ DodgeHUD_CooldownPreview.png    회피 0/2, 재충전 0.6초 샘플 상태
├─ Scripts/
│  ├─ PlayerHUDController.cs         PlayerCore 연결 및 HUD 갱신
│  ├─ PlayerHUDSafeArea.cs           안전 영역에 맞춘 앵커 갱신
│  └─ HUDFacetGraphic.cs             잘린 모서리 패널을 UGUI 정점으로 생성
└─ Editor/
   ├─ PlayerHUDBuilder.cs            프리팹 재생성과 활성 씬 설치 메뉴
   ├─ PlayerHUDVerification.cs       저장된 프리팹의 참조·표시 규칙 검증
   └─ PlayerHUDCapture.cs            지정 해상도의 화면 검토용 캡처
```

Unity가 생성하는 `.meta` 파일도 에셋과 함께 유지해야 한다. 프리팹은 아래의 기존 TMP 폰트 및 `PlayerCore`에 의존하므로 이 폴더만 다른 프로젝트로 복사하면 완전한 독립 패키지가 되지는 않는다.

## 프리팹 계층

```text
PlayerHUD
└─ SafeArea
   ├─ PlayerStatus
   │  ├─ Health
   │  │  ├─ RailFrame
   │  │  ├─ HealthIcon
   │  │  │  ├─ CrossHorizontal
   │  │  │  └─ CrossVertical
   │  │  ├─ HealthTrack
   │  │  │  ├─ Track
   │  │  │  ├─ DamageTrail
   │  │  │  ├─ HealthFill
   │  │  │  └─ Division_01 … Division_03
   │  │  └─ LowHealthAlert
   │  │     ├─ CriticalAccent
   │  │     ├─ CriticalCrossHorizontal
   │  │     └─ CriticalCrossVertical
   │  └─ SkillGauge
   │     ├─ RailFrame
   │     ├─ SkillIcon
   │     │  └─ ScytheCrest
   │     └─ SkillTrack
   │        ├─ Track
   │        ├─ SkillFill
   │        └─ CostThreshold
   ├─ ActionDock
   │  ├─ Plate
   │  ├─ TopAccent
   │  ├─ RowDivider
   │  ├─ DodgeAction
   │  │  ├─ DodgeKeycap
   │  │  │  └─ DodgeKey
   │  │  ├─ DodgeLabel
   │  │  ├─ DodgeStatus
   │  │  ├─ Charge_01
   │  │  ├─ Charge_02
   │  │  ├─ DodgeValue
   │  │  ├─ RechargeTrack
   │  │  │  ├─ Track
   │  │  │  └─ RechargeFill
   │  │  └─ CooldownTime
   │  └─ SkillAction
   │     ├─ SkillKeycap
   │     │  └─ SkillKey
   │     ├─ ScytheCrest
   │     ├─ SkillLabel
   │     ├─ SkillStatus
   │     └─ ReadyAccent
   │        ├─ ReadyLine
   │        └─ ReadyDiamond
   └─ CombatFeedback
      ├─ FeedbackPlate
      ├─ AccentLeft
      ├─ AccentRight
      ├─ FeedbackTitle
      └─ CounterHint
```

| 오브젝트 | 주요 컴포넌트와 역할 |
| --- | --- |
| `PlayerHUD` | `Canvas`, `CanvasScaler`, `CanvasGroup`, `PlayerHUDController`. Screen Space - Overlay, Sorting Order 40. |
| `SafeArea` | `RectTransform`, `PlayerHUDSafeArea`. 모든 표시 요소가 화면 안전 영역을 공유한다. |
| `PlayerStatus` | 왼쪽 위 432×68 표시부. 전체 배경 패널과 TMP 텍스트가 없으며, 체력·스킬 두 바만 모은다. |
| `Health` | 위쪽 432×30 영역. 28×28 십자 배지와 384×12 체력 바를 표시한다. 25% 간격의 세 구분선으로 잔량을 읽는다. |
| `HealthFill` | 수평 Filled `Image`. 실제 체력 비율로 부드럽게 이동한다. |
| `DamageTrail` | HP 바 뒤의 Filled `Image`. 피격 전 잔량을 잠시 유지한 뒤 감소시킨다. |
| `LowHealthAlert` | `Health` 하위의 `CanvasGroup`. 저체력에서 바와 십자가 붉어지고, `CriticalAccent` 밑줄과 붉은 십자 두 선이 맥동한다. 글자 경고나 게임의 사망 동작은 실행하지 않는다. |
| `SkillGauge` | 아래쪽 364×28 영역. 28×28 낫 배지와 316×6 스킬 바를 표시한다. 준비되면 바와 낫이 청록색으로 밝아진다. |
| `CostThreshold` | 게이지 최대값 대비 사용 비용 위치를 표시한다. 게이지가 100%가 되기 전에도 비용을 충족하면 `READY`가 된다. |
| `ActionDock` | 오른쪽 아래 418×192 패널. 회피와 스킬의 키·현재 사용 상태를 위아래 두 행으로 모은다. |
| `SkillAction` | 아래쪽 행. `E` 키캡, 낫 아이콘, 스킬 `CHARGING`/`READY` 상태를 표시한다. 스킬 게이지 자체는 PlayerStatus에 있다. |
| `ReadyAccent` | 스킬 사용 가능 상태에서 선과 다이아몬드 장식이 천천히 밝아졌다 어두워진다. |
| `DodgeAction` | 위쪽 행. `SHIFT` 키캡, 회피 잔여 횟수·사용 가능 여부·전체 재충전 시간을 표시한다. |
| `Charge_01`, `Charge_02` | 사용 가능한 횟수만큼 밝게 켜지는 두 개의 Image. 소진된 칸은 흐리게 표시한다. |
| `DodgeValue`, `DodgeStatus` | `2 / 2`, `1 / 2`, `0 / 2`와 `AVAILABLE`/`RECHARGING`을 표시한다. |
| `RechargeFill`, `CooldownTime` | 두 횟수가 함께 복구되는 공용 타이머의 진행률과 남은 초. 완충 시 `READY`를 표시한다. |
| `CombatFeedback` | 화면 아래 중앙의 일시적 알림. 평소에는 alpha 0. 완벽 회피와 반격 이벤트에 반응한다. |
| `RailFrame`, `HealthIcon`, `SkillIcon`, `Plate`, `DodgeKeycap`, `SkillKeycap` | `HUDFacetGraphic`과 `CanvasRenderer`. 모서리 절삭, 얇은 테두리, 아래쪽 알파를 가진 편집 가능한 UGUI 도형이다. `CanvasRenderer`는 필수 컴포넌트다. 왼쪽 위는 바와 배지마다 개별 프레임을 사용한다. |

루트 `CanvasGroup`은 `Interactable`과 `Blocks Raycasts`가 꺼져 있으며 모든 `Graphic.raycastTarget`도 false다. 버튼 입력을 받지 않는 표시용 HUD로, 별도의 `EventSystem`이나 `GraphicRaycaster`가 필요하지 않다.

## 씬에 사용하기

1. `PlayerHUD.prefab`을 씬 루트로 드래그한다. Canvas를 이미 포함하므로 다른 Canvas 아래에 넣을 필요가 없다.
2. 루트의 `PlayerHUDController`에서 **Player**(`_player`)에 해당 씬의 `PlayerCore`를 지정한다.
3. 플레이어가 하나인 씬에서는 **Auto Find Player**(`_autoFindPlayer`)를 켜 두면 런타임에 찾는다. 여러 플레이어가 있는 씬에서는 명시적으로 지정하거나 `BindPlayer(player)`를 호출한다.
4. 기존 플레이어 HP/스킬 UI가 있다면 중복 표시를 피하도록 해당 UI만 비활성화한다. 적 체력바와 화면 밖 적 경고는 별도 시스템이므로 유지한다.
5. 씬을 저장하고 Play Mode에서 확인한다.

런타임에 플레이어를 다시 생성하면 자동 탐색은 0.5초 간격으로 시도한다. 활성 씬의 활성 플레이어를 우선 찾으며, 여러 플레이어가 있으면 직접 연결하는 방식이 명확하다. 플레이어 참조가 없거나 비활성화되어 있으면 왼쪽 위 게이지를 비우고 비용 마커와 저체력 경고를 숨긴다. 오른쪽 아래에는 `NO SIGNAL`과 `--`를 표시하며 반격 안내도 숨긴다. 다시 활성화되면 실제 현재 수치를 읽는다. 프리팹 에셋은 씬의 플레이어 참조를 저장할 수 없으므로 인스턴스에서 연결해야 한다.

### 제작·설치 메뉴

| 메뉴 | 동작 |
| --- | --- |
| `Tools > Project R > Player HUD > Rebuild Prefab` | `PlayerHUDBuilder.cs`의 설계값으로 `PlayerHUD.prefab`을 다시 저장한다. |
| `Tools > Project R > Player HUD > Install In Active Scene` | 현재 활성 씬에 프리팹 인스턴스를 만들고, 그 씬에서 찾은 `PlayerCore`를 연결한다. 기존 `PlayerHealthUI`와 `PlayerSkillGaugeUI`가 붙은 오브젝트를 비활성화한다. |
| `Tools > Project R > Player HUD > Verify Saved Prefab` | 저장된 프리팹을 격리된 편집 씬으로 열어 참조, TMP 폰트, 입력 통과, 표시 규칙 등을 검사하고 결과를 Console에 출력한다. 검사한 샘플 상태는 저장하지 않는다. |

세 메뉴는 편집 모드에서 사용한다. 설치 메뉴는 씬을 변경된 상태로 표시하며 **씬 저장은 별도로 해야 한다**. 이미 `PlayerHUDController`가 있는 씬에서는 해당 오브젝트를 선택하고 종료한다. 이때 기존 HUD를 다시 설치하거나 플레이어 참조를 자동 갱신하지 않는다.

재생성은 프리팹을 빌더의 내용으로 덮어쓴다. Prefab Mode에서 직접 수정한 디자인을 계속 유지하려면 빌더에도 반영하거나 재생성을 하지 않는다. 기존 씬 인스턴스의 override는 남을 수 있으므로 재생성 후에는 인스턴스의 Overrides도 확인한다.

이번 작업에서는 `Assets/01_Scenes/CombatPrototypeScene.unity`와 `Assets/01_Scenes/DemoScene 1.unity`에 설치하고 저장했다. 두 씬의 기존 플레이어 HP·스킬 바는 비활성화했고 적 체력바와 화면 밖 적 경고 UI는 유지했다.

## 데이터 연결과 표시 규칙

`PlayerHUDController`는 `PlayerCore`의 전투 데이터를 읽고 이벤트를 구독한다. HUD가 체력이나 게이지를 변경하지 않는다. 활성화·재연결 때 현재 값을 읽고, 비활성화·재연결 때 기존 이벤트 구독을 해제한다.

| 입력 데이터/이벤트 | 반영되는 표시 |
| --- | --- |
| `MaxHealth`, `CurrentHealth` / `OnHealthChange(maximum, current)` | `healthFill`, `healthTrail`, `lowHealthGroup` |
| `MaxSkillGauge`, `CurrentSkillGauge`, `SkillGaugeCost` / `OnSkillGaugeChange(maximum, current)` | `skillFill`, `skillThresholdMarker` |
| `IsSkillEnable` | `skillStatus`의 `CHARGING`/`READY`, `skillIcon`·`skillGaugeIcon` 색, `readinessGroup` |
| `DodgeCharges`, `MaxDodgeCharges`, `DodgeCooldownRemaining`, `DodgeCooldownDuration` / `OnDodgeChargesChanged(charges, remaining)` | `dodgeChargeOne`, `dodgeChargeTwo`, `dodgeValue`, `dodgeStatus`, `dodgeRechargeFill`, `dodgeCooldown` |
| `OnPerfectDodgeStarted(EnemyCore)` | `feedbackLabel`에 `PERFECT DODGE` 표시 |
| `IsPerfectDodgeWindowOpen` | 반격 입력 안내 유지. 실제 반격 가능 구간에서만 `feedbackHint` 표시 |
| `OnPerfectDodgeEnded()` | 반격 안내 제거 및 메시지 페이드아웃 |
| `OnDodgeAttackStarted(EnemyCore)` | `COUNTER ATTACK` 표시, 반격 입력 안내 제거 |
| `PlayerInput.actions` / `InputSystem.onActionChange`의 `BoundControlsChanged` | `dodgeKeyLabel`, `skillKeyLabel`과 완벽 회피 중의 `feedbackHint` 공격 키 갱신 |

현재 기본 조작은 다음과 같다.

| 키 | 동작 |
| --- | --- |
| `WASD` | 이동 |
| `LMB` | 기본 공격 / 완벽 회피 중 반격 |
| `SHIFT` | 왼쪽 Shift 회피 |
| `E` | 스킬 |

위 표는 조작 문서이며 화면에 상시 표시되는 키 설명 행이 아니다. ActionDock에 회피의 `SHIFT`와 스킬의 `E` 키캡을 표시하고, 실제 완벽 회피 구간에만 반격 키를 별도로 안내한다. 키 표시는 현재 연결된 `PlayerInput`의 키보드·마우스 바인딩을 읽는다. 연결된 Action이 없을 때는 기본 문자열을 사용하며, Action은 있지만 적절한 바인딩을 찾지 못하면 `UNBOUND`를 표시한다. 게임패드 전용 아이콘이나 입력 장치 자동 전환 UI는 포함하지 않는다. 매우 긴 재바인딩 이름은 해당 TMP 영역 크기도 함께 조절해야 한다.

### 회피 2회 공용 재충전 규칙

회피는 최대 2회이며, **마지막으로 성공한 회피부터 1초 후 두 횟수가 함께 복구**된다. 사용 가능 횟수가 남아 있으면 추가 자원 쿨다운 없이 다음 회피를 사용할 수 있다. 기존 애니메이션의 다음 행동 허용 구간은 그대로 유지한다.

| 상황 | 잔여 횟수 | 공용 타이머 |
| --- | --- | --- |
| 시작 / 완충 | 2 | 없음 |
| 첫 번째 회피 성공 | 1 | 1초부터 시작 |
| 만료 전에 두 번째 회피 성공 | 0 | 다시 1초로 초기화 |
| 0회에서 회피 입력 | 0 | 원래 남은 시간을 유지하며 입력 거부 |
| 마지막 성공한 회피 후 1초 경과 | 즉시 2 | 종료 |

예를 들어 0.0초에 첫 번째, 0.4초에 두 번째 회피를 사용하면 1.4초에 2회가 동시에 복구된다. 첫 번째만 사용했다면 1.0초에 2회가 된다. 0→1→2로 한 칸씩 충전되는 방식은 아니다.

HUD는 1회가 남아 있어도 `AVAILABLE`을 표시하면서 남은 재충전 시간을 함께 보여준다. 0회가 되면 `RECHARGING`과 경고색으로 바뀐다. 공용 진행 바는 사용 직후 비워졌다가 1초 동안 채워지며, 만료 시 두 횟수 표시가 모두 켜진다. 남은 초는 소수점 첫째 자리까지 올림하여 실제 시간이 남아 있는데 `0.0s`로 보이지 않게 한다.

실제 규칙은 `Assets/02_Scripts/Player/PlayerDodgeChargePool.cs`가 담당하고, `PlayerCore`가 시간을 전달한다. Player의 **PlayerCore > Dodge Charges > Dodge Cooldown Duration**(`_dodgeCooldownDuration`)에서 재충전 시간을 수정할 수 있으며 기본값은 1초다. 최대 횟수는 2회로 고정되어 있다.

타이머는 실제 시간인 `Time.unscaledDeltaTime`을 사용하므로 프로젝트의 `CombatTimeController` 슬로우모션과 개별 히트스톱에 느려지지 않는다. 단, `Time.timeScale <= 0`인 게임 일시정지에서는 회피 사용과 타이머 진행을 모두 멈춘다. 이 회피 자원 규칙은 아래의 HP 보간·알림 같은 UI 장식 애니메이션과 구분된다.

프레임 도중 회피를 사용한 경우에는 사용하기 전의 경과 시간을 새 쿨다운에서 빼지 않는다. `PlayerCore`를 껐다 켜도 남은 횟수를 초기화하지 않으며 비활성 시간은 충전에 포함하지 않는다. HUD를 다시 켜면 현재 횟수와 잔여 시간을 읽어 표시한다.

`PlayerStateMachine`이 정면·후방 회피로 진입하기 전에 횟수를 검사하고 소모한다. 실패하면 현재 상태를 종료하지 않으며 타이머도 초기화하지 않는다. 정면 회피의 종료 동작과 완벽 회피 후 반격은 추가 횟수를 소모하지 않는다. HUD는 이벤트의 상태를 표시할 뿐 자체적으로 쿨다운을 진행하거나 회피 사용을 판단하지 않는다.

### Inspector 연결 필드

루트의 `PlayerHUDController`에서 아래 참조들은 빌더가 연결한다. 자식 오브젝트를 교체하면 대응 참조도 다시 지정한다.

| 필드 | 계층 대상 |
| --- | --- |
| `healthFill`, `healthTrail` | `SafeArea/PlayerStatus/Health/HealthTrack/HealthFill`, `DamageTrail` |
| `lowHealthGroup` | `SafeArea/PlayerStatus/Health/LowHealthAlert` |
| `skillFill`, `skillThresholdMarker` | `SafeArea/PlayerStatus/SkillGauge/SkillTrack/SkillFill`, `CostThreshold` |
| `skillGaugeIcon` | `SafeArea/PlayerStatus/SkillGauge/SkillIcon/ScytheCrest` |
| `skillIcon`, `skillStatus` | `SafeArea/ActionDock/SkillAction/ScytheCrest`, `SkillStatus` |
| `skillKeyLabel` | `SafeArea/ActionDock/SkillAction/SkillKeycap/SkillKey` |
| `readinessGroup` | `SafeArea/ActionDock/SkillAction/ReadyAccent` |
| `feedbackGroup`, `feedbackLabel`, `feedbackHint` | `SafeArea/CombatFeedback`, 하위 `FeedbackTitle`, `CounterHint` |
| `dodgeKeyLabel` | `SafeArea/ActionDock/DodgeAction/DodgeKeycap/DodgeKey` |
| `dodgeChargeOne`, `dodgeChargeTwo` | `SafeArea/ActionDock/DodgeAction/Charge_01`, `Charge_02` |
| `dodgeValue`, `dodgeStatus`, `dodgeCooldown` | `SafeArea/ActionDock/DodgeAction/DodgeValue`, `DodgeStatus`, `CooldownTime` |
| `dodgeRechargeFill` | `SafeArea/ActionDock/DodgeAction/RechargeTrack/RechargeFill` |

`healthValue`, `skillValue`, `skillCostLabel`, `playerState`는 선택 참조로 남아 있지만 현재 프리팹에서는 모두 `None`이다. 왼쪽 위에는 숨겨 둔 TMP 오브젝트도 생성하지 않는다. 별도 디자인에서 수치나 상태 문구가 필요한 경우 해당 필드에 TMP를 연결하면 기존 갱신 로직을 사용할 수 있다.

`SetPreview(health, maxHealth, gauge, maxGauge, skillCost)`와 `SetDodgePreview(charges, remaining, duration)`는 표시만 즉시 바꾸는 제작·검증용 API다. `BindPlayer(player)`는 실제 플레이어를 연결하고, `RefreshInputLabels()`는 키 표시를 다시 읽는다. `ShowFeedback(message)`는 임의 문자열 알림을 표시한다. 이 API들은 게임의 전투 수치를 수정하지 않는다.

## 애니메이션과 크기 조절

`PlayerHUDController`의 아래 값은 기본값이며 Inspector에서 수정할 수 있다. 보간, 잔량 감소, 알림, 맥동은 `Time.unscaledDeltaTime`과 `Time.unscaledTime`을 사용한다.

| 필드 | 기본값 | 의미 |
| --- | --- | --- |
| `fillResponse` | 12 | 체력·스킬 게이지가 목표값을 따라가는 지수 보간 반응 속도. 초 단위 지속 시간이 아니다. |
| `damageTrailDelay` | 0.4초 | 피격 후 잔량 바가 감소하기 전 대기 시간 |
| `damageTrailSpeed` | 0.55/초 | 잔량 바의 정규화 비율 감소 속도 |
| `feedbackDuration` | 1.15초 | 일시 알림 표시 시간. 마지막 페이드 구간을 포함한다. |
| `feedbackFadeDuration` | 0.3초 | 알림의 마지막 페이드아웃 시간 |
| `pulsePeriod` | 1.8초 | 스킬 준비 장식·저체력 알림의 맥동 주기 |
| `lowHealthThreshold` | 0.25 | 최대 체력의 25% 이하를 저체력으로 표시 |

완벽 회피 입력 구간이 열려 있는 동안에는 메시지가 사라지지 않으며, 종료 이벤트를 받으면 페이드아웃한다. 반격으로 전환하면 `COUNTER ATTACK` 메시지로 바뀐다. `healthNormalColor`, `healthCriticalColor`, `skillChargingColor`, `skillReadyColor`는 표시 색을 조정한다.

`CanvasScaler`는 `Scale With Screen Size`, 기준 1920×1080, `Match Width Or Height = 0.5`다. `PlayerStatus`는 왼쪽 위 앵커·피벗 `(0, 1)`, 위치 `(56, -48)`, 크기 `432×68`을 사용한다. 하위 `Health`는 위치 `(0, 36)`, 크기 `432×30`, `SkillGauge`는 위치 `(0, 2)`, 크기 `364×28`이다. `ActionDock`은 오른쪽 아래 앵커·피벗 `(1, 0)`, 위치 `(-56, 48)`, 크기 `418×192`다. `CombatFeedback`은 아래 중앙 앵커를 사용한다. 전체 규모는 CanvasScaler, 개별 위치·크기는 각 그룹의 RectTransform에서 조절한다.

`PlayerHUDSafeArea`는 편집 모드와 플레이 모드 모두에서 화면 크기와 `Screen.safeArea` 변경을 확인한다. SafeArea의 앵커를 정규화한 안전 영역에 맞추며 자식 위치를 그 안에서 유지한다. **Respect Safe Area**(`_respectSafeArea`)를 끄면 화면 전체를 사용한다. 가로 화면용 구성으로 제작했으며 세로 화면용 별도 재배치 로직은 없다.

`HUDFacetGraphic`의 `cornerCut`, `borderWidth`, `borderColor`, `bottomAlpha`는 패널 모양을 조절한다. 문양을 바꾸려면 각 `ScytheCrest` Image의 Sprite를 변경한다.

## 이미지와 폰트 의존성

낫 PNG의 임포트 설정은 빌더에서 다음처럼 적용한다.

- Texture Type: `Sprite (2D and UI)`, Sprite Mode: `Single`
- Alpha Is Transparency: On, Mip Maps: Off
- Max Size: 512, Compression: Uncompressed

문양의 투명 영역은 원본 PNG의 알파 채널이다. 패널이나 텍스트가 이미지에 합쳐져 있지 않다.

`WhitePixel.png`는 이미지 생성 도구의 결과가 아니라 `PlayerHUDBuilder.ImportSolidSprite()`가 Unity의 흰색 텍스처에서 만드는 평면 Sprite다. 게이지 Filled Image의 밝기와 가장자리를 균일하게 유지한다. Single Sprite, Mip Maps Off, Point 필터, Uncompressed 설정을 사용한다.

TMP 폰트는 프로젝트의 다음 에셋을 참조한다.

- `Assets/Download Assets/Heat - Complete Modern UI/Fonts/RobotoCondensed-Regular SDF.asset`
- `Assets/Download Assets/Heat - Complete Modern UI/Fonts/RobotoCondensed-Bold SDF.asset`

기존 폰트를 복제하지 않고 사용한다. 현재 표시 문구는 영문이다. 한글로 변경하려면 한글 글리프를 포함하는 TMP Font Asset 또는 적절한 fallback 폰트를 연결해야 한다. UGUI, TMP, Input System 및 프로젝트의 `PlayerCore`가 필요하다. `Editor` 폴더의 빌더 코드는 플레이어 빌드에 포함되지 않는다.

## 적용 및 검증 기록

2026-11-03 기준 얇은 좌상단 PlayerStatus와 기존 우하단 ActionDock의 연결을 확인했다. 이번 재디자인은 게임플레이 코어 규칙을 변경하지 않았다.

- `CombatPrototypeScene`과 `DemoScene 1`의 저장된 프리팹 인스턴스에서 432×68 크기, 플레이어 참조, `PlayerStatus` 하위 TMP 0개, `skillGaugeIcon` 연결을 확인했다. 두 씬 모두 저장되지 않은 변경이 없었으며 기존 플레이어 바만 비활성화된 상태를 유지했다.
- 컴파일·실행 오류 0개, 저장된 프리팹 검증 152개 항목을 통과했다. 내부 참조, Missing Script 부재, TMP 폰트, Raycast 비활성화, 스킬 비용 50에서 49/50 경계, 비용 70의 마커 위치, 저체력·범위 제한·미연결 상태, 좌상단 텍스트 부재와 회피 1/0/2회 표시를 확인했다. 검사 수는 제거된 표시 오브젝트와 참조에 맞게 변경되었다.
- 새 디자인의 DemoScene Play Mode에서 `AddSkillGauge(72)`가 `SkillNormalized = 0.72`, 좌상단 낫의 준비 색, 우하단 `READY`에 반영되었다. 선택 TMP 참조 네 개를 모두 비운 상태에서도 오류가 없었다.
- 1920×1080·1024×768 레이아웃에서 잘림과 겹침이 없음을 시각 검토했다. 체력 20%의 붉은 바·십자·밑줄, 스킬 35%의 충전 중 색도 확인했다. `Preview`의 PNG 네 개를 모두 새 디자인으로 갱신했다.

아래는 회피 기능 구현과 이전 배치 때 완료한 게임플레이·이벤트 연결 검증 기록이다. 이번 재디자인에서는 해당 코어를 변경하지 않았다.

- `PlayerDodgeChargeVerification.Run()`의 251개 항목을 통과했다. 30/60/120Hz 진행, 마지막 성공한 사용부터 타이머 재시작, 중간 1회 복구 부재, 세 번째 회피 거부 시 상태·효과·타이머 유지, 일시정지, 긴 프레임 도중 외부 진입의 사용 이전 시간 제외를 확인했다. 편집기 메뉴 `Tools > Combat > Validate Dodge Charges`에서 다시 실행할 수 있다. 검증 코드는 `Assets/02_Scripts/Combat/Editor/PlayerDodgeChargeVerification.cs`에 있다.
- 실제 Play Mode FSM에서 후방 회피→후방 회피로 2회가 소모되고 세 번째는 거부되는 것을 확인했다. 남은 0.6초에서 두 번째 사용 시 1초로 재시작, 0.99초 경과 시 0회 유지, 추가 0.01초 후 2회 동시 복구를 단계별로 검사했으며 HUD 표시도 일치했다. HUD와 PlayerCore를 재활성화해도 소진된 0회가 임의로 충전되지 않았다.
- 실제 Play Mode에서 `AddSkillGauge`로 49→50 사용 가능 전환, `UseSkillGauge`로 소비 반영, `TryTakeDamage`의 35 피해 반영을 확인했다.
- DemoScene Play Mode의 실제 후방 회피 두 번이 우하단 `0 / 2`와 `1.0s`에 반영되었으며 회피·스킬 키캡은 `SHIFT`, `E`로 표시되었다.
- HUD 비활성화·재활성화 후 현재 값 동기화, `PlayerCore` 비활성 상태 표시와 재활성화 후 실제 값 복구를 확인했다.
- `Time.timeScale = 0`에서도 게이지 보간, 피격 잔량 감소, 알림 종료가 진행됨을 확인했다. 편집기 백그라운드 검증 중 임시로 변경한 `Application.runInBackground`는 원래 값으로 복원했다.
- 실제 `BeginPerfectDodge`에서 `OnPerfectDodgeStarted`에 반응하여 반격 안내가 표시되고, 종료 시 안내가 제거되는 것을 확인했다.

`Preview`의 PNG 네 개는 모두 얇은 좌상단 PlayerStatus·우하단 ActionDock 배치로 갱신한 기본 상태 또는 검토용 샘플 화면이다. `DemoHUD_FeedbackPreview.png`의 체력 68, 게이지 72, `PERFECT DODGE` 문구와 `DodgeHUD_CooldownPreview.png`의 0/2회·잔여 0.6초는 표시 검토용 샘플이다. 체력과 스킬 값은 좌상단 바의 길이로만 표현된다. 게임의 시작 수치나 밸런스 설정을 뜻하지 않으며 실제 Play Mode에서는 연결된 `PlayerCore`의 상태를 표시한다.

`PlayerHUDCapture.Capture(width, height, path)`는 현재 씬의 HUD와 URP Main Camera로 지정 크기의 PNG를 만드는 레이아웃 검토용 편집 도구다. 캡처 중에만 RenderTexture, 카메라 비율, Canvas 렌더 모드·배율·SafeArea를 조정하고 씬 후처리를 잠시 끈다. 종료 시 해당 속성과 후처리 설정을 원래대로 복구한다. 임시 UI 카메라를 만들거나 카메라 스택을 변경하지 않는다.

따라서 저장된 PNG의 배경은 후처리가 포함된 실제 Game 뷰와 색감이 다를 수 있으며, Game 뷰의 최종 렌더를 그대로 기록한 스크린샷은 아니다. 실제 HUD는 Screen Space - Overlay로 렌더링되어 씬 후처리의 영향을 받지 않는다. 캡처는 화면 전체 영역을 사용하므로 모바일 노치 모의 검증에는 해당하지 않는다.
