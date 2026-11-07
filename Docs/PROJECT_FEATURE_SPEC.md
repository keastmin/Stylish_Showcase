# ProjectR 기능 명세 및 코드 추적 가이드

> 분석 기준: Unity `6000.5.8f1`, 2026-09-09 현재 워크스페이스의 정적 코드/에셋 상태  
> 주 분석 범위: `Assets/02_Scripts`, `Assets/01_Scenes`, `Assets/03_Animations`, `Assets/04_Timelines`, `Assets/05_Timeline Signals`, `Assets/06_VFX`, `Assets/07_Datas`, `Assets/08_Prefabs`, `Assets/Input Actions`  
> 줄 번호는 이 문서를 작성한 시점의 파일 기준이며 이후 코드 수정 시 달라질 수 있다.

## 1. 범위와 판독 원칙

이 문서는 `MonoBehaviour`, `StateMachineBehaviour`, `PlayableDirector`, `Animator`, `Rigidbody`, `Collider` 같은 Unity 자체 타입의 일반 동작을 설명하지 않는다. 대신 프로젝트에서 직접 작성한 클래스가 Unity 콜백과 에셋을 어떻게 이용해 게임 기능을 구성하는지에 초점을 둔다.

다음 항목은 구현 대상으로 보지 않고 의존 대상으로만 기록했다.

- Unity 패키지 및 엔진 타입
- `Assets/Download Assets`, `Assets/SRMove`, `Assets/FogCity` 내부의 서드파티 구현
- 자동 생성된 `PlayerInputAction.cs`
- 에디터 검증 도구는 런타임 기능과 분리해서 기록

판독 우선순위는 다음과 같다.

1. 현재 씬/프리팹/Timeline/SO에 직렬화된 값
2. 직접 작성된 런타임 코드
3. 기존 프로젝트 문서

즉 코드 기본값이나 기존 문서가 현재 에셋과 다르면, 실제 플레이에 쓰이는 직렬화값을 본문의 기준으로 삼는다.

## 2. 전체 구조 요약

프로젝트의 핵심은 플레이어와 적이 각각 독립적인 순수 C# 상태 머신을 갖고, 애니메이션의 이동량과 Timeline/Animation Event의 타격 시점을 받아 전투를 진행하는 구조다.

```text
Input Action
  -> PlayerInputCollector
  -> PlayerCore.Update
  -> PlayerStateMachine
  -> 상태별 Animator Trigger / PlayableDirector 재생 / Root Motion 명령
  -> Timeline Signal 또는 Animation Event
  -> PlayerAttackInstanceContainer / EnemyCore.HandleAttack
  -> Physics Overlap 기반 IDamageable 탐색
  -> DamageData 전달
  -> 피격 상태, 체력, 게이지, SFX/VFX, Hitstop

EnemyTargetDetector
  -> EnemyStateMachine: Idle -> Engage -> Notice -> CloseAttack
  -> EnemyAttackTimingController: 공격 순번과 간격
  -> EnemyPositioningController: 거리·궤도·개인 공간·공격 통로
  -> EnemyAttackTargetingController: 예고 중 추적, 공격 직전 고정, Root Motion 보정
```

시간 계층은 두 갈래다.

- 전투 시간: `CombatTimeController.DeltaTime`을 사용하는 회피 슬로모션, 적 공격 쿨다운, 포지셔닝, 투사체 반복 타격
- 현실 시간: `Time.unscaledDeltaTime`과 DSP 시간을 사용하는 Hitstop 해제, UI 보간, 화면 효과

## 3. 씬과 프리팹 조립

### 3.1 주요 씬

| 씬 | 역할 | 특징 |
|---|---|---|
| `Assets/01_Scenes/CombatPrototypeScene.unity` | 전투 프로토타입 | Player, 복수 Striker, 전투 UI, 공격 타이밍/포지셔닝 관리자, 후방 적 경고, VFX Pool을 조립한다. |
| `Assets/01_Scenes/DemoScene 1.unity` | 환경 포함 데모 | 위 전투 구성에 FogCity 환경을 더한다. Player 프리팹 인스턴스에 `PlayerSkillCameraDirector`와 `CinemachineImpulseSource`를 추가·오버라이드한다. |

두 씬 모두 씬 인스턴스에서 다음과 같은 프리팹의 비어 있는 참조를 채운다.

- `PlayerCore._mainCamera`
- 적 목록을 필요로 하는 `PlayerPerfectDodgeDetector`, `EnemyPositioningController`, `EnemyAttackTimingController`
- Player/Enemy를 표시하는 UI와 후방 경고 시스템

따라서 프리팹만 단독으로 띄우거나 새 씬으로 복사할 때는 씬 오버라이드가 빠지지 않았는지 확인해야 한다.

### 3.2 Player 프리팹의 직접 작성 컴포넌트

`Assets/08_Prefabs/Player/Player.prefab`의 주 구성은 다음과 같다.

- 중심: `PlayerCore`, `PlayerStateMachine`(런타임 생성)
- 입력/이동: `PlayerInputCollector`, `PlayerMover`, `PlayerRotator`, `PlayerTargetDetector`, `FootPositionDetector`
- 애니메이션: `PlayerAnimatorController`, `PlayerAnimationEventSender`, `PlayerAnimationEvent`
- Timeline: `TimelineDirectorContainer`, `PlayerSkillAttackReceiver`
- 공격: `PlayerAttackInstanceContainer`, `ProjectileHitbox`
- 효과: `MeshTrailEffect`, `PlayerSFXController`

현재 프리팹 기준 주요 값은 최대 체력 100, 스킬 사용 요구량 50, 퍼펙트 회피 게이지 획득량 5다. 코드 선언부의 기본값과 다를 수 있으므로 프리팹 값을 우선한다.

### 3.3 Striker 프리팹의 직접 작성 컴포넌트

`Assets/08_Prefabs/Enemy/Striker.prefab`의 주 구성은 다음과 같다.

- 중심: `EnemyCore`, `EnemyStateMachine`(런타임 생성)
- 감지/이동: `EnemyTargetDetector`, `EnemyMover`, `EnemyRotator`
- 공격: `EnemyHitboxPool`, `EnemyAttackSimulator`, `EnemyAttackTargetingController`(런타임 생성)
- 애니메이션: `EnemyAnimatorCallback`, `EnemyAnimationEventSender`, `EnemyAnimationEvent`
- 효과: `EnemySFX`
- 미완성 또는 비활성 흐름: `EnemyTimelineEvent`, `EnemyAttackSimulator`
- 에디터 전용 미리보기: `AnimationRootMotionMultiplierPreview`

현재 프리팹 최대 체력은 1000이고, 공격 데이터는 `Close Attack Hit1.asset`, `Close Attack Hit2.asset`을 참조한다.

## 4. 런타임 생명주기와 실행 순서

### 4.1 명시된 실행 순서

| 순서 | 클래스 | 의미 |
|---:|---|---|
| -10000 | `HitstopCoordinator` | 애니메이션 평가 전에 기존 Hitstop 해제를 갱신하고, 새 요청은 `LateUpdate`에서 보류/적용한다. |
| -9000 | `CombatTimeController` | 전투용 스케일과 DeltaTime을 먼저 계산한다. |
| -8000 | `PlayerInputCollector`, `CombatVfxTime` | 플레이어 FSM보다 먼저 입력을 수집하고, VFX 시간 연결을 준비한다. |
| -100 | `EnemyAttackTimingController` | 적 상태 갱신 전에 공격 가능 시계를 진행한다. |
| -90 | `EnemyPositioningController` | 적 상태 갱신 전에 공격자/공격 통로 상태를 정리한다. |
| 100 | `PlayerMover` | 상태가 낸 이동 명령을 물리 단계에서 소비한다. |

### 4.2 Player 생명주기

`PlayerCore`는 `Assets/02_Scripts/Player/PlayerCore.cs:5`에서 시작한다.

| 단계 | 위치 | 수행 내용 |
|---|---|---|
| `Awake` | `PlayerCore.cs:84` | 필수 컴포넌트를 수집하고 HP/게이지를 초기화한다. `PlayerStateMachine`과 `DirectionCalculator`를 만들고 Animator tick, VFX 계층을 연결한다. |
| `OnEnable` | `PlayerCore.cs:114` | `CombatTimeController.ScaleChanged`를 구독한다. |
| `Start` | `PlayerCore.cs:120` | Idle 상태로 초기화하고 커서를 잠근다. |
| `Update` | `PlayerCore.cs:128` | 퍼펙트 회피 시간 갱신 -> 회피 반격 입력 -> Hitstop 여부 확인 -> FSM `UpdateTick` 순서다. |
| `FixedUpdate` | `PlayerCore.cs:146` | FSM 물리 tick을 호출한다. 실제 속도 적용은 `PlayerMover.FixedUpdate`(`PlayerMover.cs:43`)가 담당한다. |
| `LateUpdate` | `PlayerCore.cs:154` | FSM 후처리 tick을 호출한다. |
| Animator tick | `PlayerCore.cs:162` | `PlayerAnimatorController.OnAnimatorMove`가 발생시키는 이벤트를 FSM의 `AnimatorTick`으로 전달한다. |
| `OnDisable` | `PlayerCore.cs:170` | 퍼펙트 회피, 타깃, Hitstop, 시간 스케일 구독을 정리한다. |

`PlayerCore.Awake`는 여러 sibling 컴포넌트가 반드시 존재한다고 가정한다. 새 Player 변형 프리팹을 만들 때 위 3.2 목록을 함께 복제해야 한다.

### 4.3 Enemy 생명주기

`EnemyCore`는 `Assets/02_Scripts/Enemy/EnemyCore.cs:5`에서 시작한다.

| 단계 | 위치 | 수행 내용 |
|---|---|---|
| `Awake` | `EnemyCore.cs:81` | 공격 SO 사전, 하위 컴포넌트, `EnemyAttackTargetingController`, HP/경직 상태, `EnemyStateMachine`을 만든다. Animation Event와 공격 이벤트도 연결한다. |
| `OnEnable` | `EnemyCore.cs:107` | 전투 시간 스케일을 적용하고 가까운 포지셔닝 관리자를 찾는다. |
| `Start` | `EnemyCore.cs:119` | Idle 상태로 초기화한다. |
| `Update` | `EnemyCore.cs:124` | 전투 타이머와 FSM 일반 tick을 갱신한다. |
| `FixedUpdate` | `EnemyCore.cs:134` | 상태별 이동 명령을 처리한다. |
| `LateUpdate` | `EnemyCore.cs:142` | 상태별 후처리를 호출한다. |
| Animator tick | `EnemyCore.cs:150` | `EnemyAnimatorCallback.OnAnimatorMove`를 FSM에 전달한다. |
| `OnDisable` | `EnemyCore.cs:158` | 공격 타깃, 공격 권한, 포지셔닝, Hitstop, 시간 구독을 해제한다. |

## 5. 플레이어 기능 명세

### 5.1 입력

`PlayerInputCollector`는 `Assets/02_Scripts/Player/Input/PlayerInputCollector.cs:5`에서 시작한다. `Awake`(`:31`)에서 `PlayerInput`의 Action 이름으로 참조를 찾고, `Update`(`:40`)에서 현재 프레임 값을 캐시한다.

| Action | 현재 바인딩 | 소비 흐름 |
|---|---|---|
| Move | WASD | 카메라 기준 방향으로 변환되어 Run 계열 상태와 이동에 사용된다. |
| Attack | 마우스 왼쪽 | Idle/Run/Combo/퍼펙트 회피 중 서로 다른 공격 상태로 전이한다. |
| Dodge | Left Shift | 이동 방향에 따라 전방 회피 또는 후방 회피를 선택한다. |
| Skill | E | 게이지가 50 이상일 때 `PlayerSkillState`로 진입한다. |
| Parry | Space | Input Action에는 있으나 현재 `PlayerInputCollector`가 읽지 않으며 런타임 사용처도 없다. 현재 기능상 비활성이다. |

### 5.2 상태 머신과 공통 피격

`PlayerStateMachine`은 `Assets/02_Scripts/Player/FSM/PlayerStateMachine.cs:3`에서 모든 상태 인스턴스를 한 번 생성한다. 초기화는 `:64`, 상태 전이는 `:95`다. 전이 시 현재 상태의 `Exit`, 다음 상태의 `Enter`를 호출하고 회피 반격 타깃이 불필요해지면 정리한다.

`PlayerStateBase`는 `Assets/02_Scripts/Player/FSM/PlayerStateBase.cs:3`이다.

- `Enter`(`:16`)에서 피격, 애니메이션 종료, 행동 허용 등 공통 이벤트를 구독한다.
- `Exit`(`:46`)에서 같은 이벤트를 해제한다.
- 피격 처리 `HandleDamaged`(`:57`)는 공격자의 방향을 계산하여 Front/Back Hit 상태로 전이한다.
- Root Motion 이동 헬퍼는 `:78`이다.

현재 `PlayerHitLargeState`는 생성되고 Animator Trigger도 존재하지만 공통 피격 분기에서 선택되지 않는다. 플레이어 체력이 0이 되었을 때 별도의 Dead 상태나 사망 이벤트로 전이하는 코드도 없다.

### 5.3 이동 상태

| 상태 | 클래스 시작 | 핵심 동작과 종료 조건 |
|---|---:|---|
| Idle | `PlayerIdleState.cs:3` | 입력을 보고 Run Start, 기본 공격, 후방 회피, Skill로 전이한다. |
| Run Start | `PlayerRunStartState.cs:3` | 시작 애니메이션 Root Motion을 적용하고 Run Loop로 이어진다. |
| Run Loop | `PlayerRunLoopState.cs:3` | 방향 입력을 계속 반영한다. 약 2초 연속 이동하면 Fast Run Loop로 전이한다. |
| Run Stop L/R | 각 `PlayerRunStopLeftState.cs:3`, `PlayerRunStopRightState.cs:3` | 앞발 판정과 방향에 맞는 정지 애니메이션을 선택한다. |
| Fast Run Loop | `PlayerFastRunLoopState.cs:3` | 빠른 이동, 급회전, 정지, Run Attack 분기를 담당한다. |
| Fast Run Turn | `PlayerFastRunTurnState.cs:3` | 큰 방향 변경을 전용 Turn 애니메이션으로 처리한다. |
| Fast Run Stop | `PlayerFastRunStopState.cs:3` | 빠른 달리기 종료 후 Idle/재이동으로 복귀한다. |

이동 계산은 다음 세 클래스가 분담한다.

- `DirectionCalculator` (`Assets/02_Scripts/Player/Direction Calculator/DirectionCalculator.cs:3`): 카메라 전방/우측을 지면에 투영해 입력 방향을 월드 방향으로 변환
- `PlayerMover` (`Assets/02_Scripts/Player/Mover/PlayerMover.cs:5`): 이동 명령을 모아 `FixedUpdate`에서 적용하고 SphereCast로 지면 hover를 보정
- `PlayerRotator` (`Assets/02_Scripts/Player/Rotator/PlayerRotator.cs:3`): 논리 루트와 시각 모델의 회전을 분리하고 보간/즉시 회전 제공

### 5.4 기본 콤보와 달리기 공격

기본 공격 공통 상태는 `PlayerBasicAttackState` (`Assets/02_Scripts/Player/FSM/States/Basic Attack/PlayerBasicAttackState.cs:4`)다.

1. `Enter`(`:18`)에서 공격 Trigger를 설정하고 대응 Timeline을 재생한다.
2. `PlayerTargetDetector.AcquireBasicAttackTarget`(`PlayerTargetDetector.cs:85`)로 콤보 타깃을 고정하고 타깃 방향을 본다.
3. Timeline이 Next Combo Enable/Disable Signal을 보내 입력 접수 구간을 연다.
4. 같은 Timeline의 Hit Signal이 `PlayerAttackInstanceContainer`의 공격별 메서드를 호출한다.
5. 입력이 예약되면 1 -> 2 -> 3 -> 4 상태로 전이하고, 아니면 정규화 시간 약 0.92 이후 종료한다.
6. `Exit`(`PlayerBasicAttackState.cs:99`)에서 타깃·이벤트·Director를 정리한다.

공격 상태 클래스는 `PlayerBasicAttack1State.cs:1`부터 `PlayerBasicAttack4State.cs:1`까지이며, 각 클래스가 Trigger, `DirectorID`, 다음 콤보 상태만 지정한다.

달리기 공격은 `PlayerRunAttackState` (`Assets/02_Scripts/Player/FSM/States/Run Attack/PlayerRunAttackState.cs:4`)가 담당한다. `Enter`(`:15`)에서 RunAttack Timeline과 타깃을 시작하고, Signal이 다른 행동 허용 시점과 애니메이션 종료 시점을 전달한다. `Exit`는 `:101`이다.

### 5.5 회피와 퍼펙트 회피

일반 회피의 공통 클래스는 `PlayerDodgeState` (`Assets/02_Scripts/Player/FSM/States/Dodge/PlayerDodgeState.cs:3`)다. 진입 시 `PlayerCore.IsPerfectDodge`를 확인하고 성공하면 `BeginPerfectDodge`를 호출한다.

| 흐름 | 클래스 | 설명 |
|---|---|---|
| 전방 회피 | `PlayerFrontDodgeState.cs:3` | 이동 방향으로 Root Motion 회피를 수행하고, 입력/애니메이션 상태에 따라 Stop 또는 다음 행동으로 간다. |
| 전방 회피 정지 | `PlayerFrontDodgeStopState.cs:3` | 전방 회피 후 감속·정지를 처리한다. |
| 후방 회피 | `PlayerBackDodgeState.cs:3` | 뒤로 빠진 후 Idle 또는 후속 행동으로 간다. |
| 성공 판정 | `PlayerPerfectDodgeDetector.cs:5` | 구성된 적을 거리순으로 검사하고 `EnemyCore.IsPlayerInEnemyAttackRange`에 현재 공격 예고 범위인지 질의한다. |
| 슬로모션 | `CombatTimeController.cs:24`, `CombatSlowMotion.cs:4` | 성공 시 전투 시간만 느리게 하고 현실 시간 기준으로 진입/유지/복귀한다. |
| 반격 시작 | `PlayerCore.cs:310` | 성공 창 중 Attack을 누르면 저장한 적을 향해 `DodgeAttackStartState`로 전이한다. |

현재 Player 프리팹 설정은 Slow Scale 0.08, 시작 지연 0.07초, 최대 지속 1.65초, Fade In 0.06초, Fade Out 0.07초다. 성공 시 스킬 게이지 5를 얻는다.

회피 반격은 `PlayerDodgeAttackState` (`Assets/02_Scripts/Player/FSM/States/Dodge Attack/PlayerDodgeAttackState.cs:4`)를 공통 기반으로 Start -> Loop -> End의 세 Timeline을 연속 재생한다. 세 상태 모두 `IsInvulnerable`이 true다.

- Start: `PlayerDodgeAttackStartState.cs:3`
- Loop: `PlayerDodgeAttackLoopState.cs:3`
- End: `PlayerDodgeAttackEndState.cs:3`

Start의 `MotionWarpToEnemyHurtbox` (`PlayerDodgeAttackStartState.cs:38`)는 현재 구현이 비어 있다. 따라서 저장 타깃을 바라보는 회전은 하지만 이름이 암시하는 위치 워핑은 수행하지 않는다.

### 5.6 스킬

`PlayerSkillState`는 `Assets/02_Scripts/Player/FSM/States/Skill/PlayerSkillState.cs:4`에서 시작한다.

1. `Enter`(`:15`)에서 게이지를 소모하고 Skill Timeline을 재생한다.
2. 기본 공격 타깃을 획득하여 시작 방향을 맞춘다.
3. 상태 전체가 무적이다.
4. Root Motion은 `FixedTick`(`:51`)과 `AnimatorTick`(`:57`)에서 처리한다.
5. Timeline의 Custom Marker는 `PlayerSkillAttackReceiver.OnNotify`(`PlayerSkillAttackReceiver.cs:31`)로 들어가 해당 `AttackDamageField`를 즉시 판정한다.
6. Director 시간이 2.05초를 넘으면 `UpdateTick`(`PlayerSkillState.cs:39`)이 강제로 Idle로 복귀한다.

`Idle Animation Transition` 이벤트는 `SetTransitionIdle`(`:85`)에 연결되어 `_isTransitionIdle`을 바꾸지만 현재 `UpdateTick`은 이 값을 검사하지 않는다. 실제 상태 종료 기준은 2.05초다.

### 5.7 플레이어 공격 판정

`AttackDamageField` (`Assets/02_Scripts/Player/Attack Instance Container/Instances/AttackDamageField.cs:11`)는 컴포넌트가 아닌 직렬화 데이터 클래스다. Box/Sphere/Capsule, 위치/회전/크기, 피해량, Hitstop 프레임, 경직, 게이지 획득량, LayerMask를 가진다.

`PlayerAttackInstanceContainer` (`Assets/02_Scripts/Player/Attack Instance Container/PlayerAttackInstanceContainer.cs:5`)의 실제 처리점은 `GiveDamageField`(`:132`)다.

- Physics Overlap으로 대상 Collider를 찾는다.
- 한 번의 판정 안에서 중복 대상을 제거한다.
- Hashing 판정은 콤보 동안 이미 맞은 대상을 기억하고, NoHashing은 각 Signal마다 다시 맞을 수 있다.
- `DamageData`를 만들고 `IDamageable.TryTakeDamage`를 호출한다.
- 적중 성공 시 게이지, SFX, VFX, Hitstop을 적용한다.
- 콤보 상태 종료 시 `ClearDamagedTargets`(`:184`)로 기억을 비운다.

현재 Player 프리팹의 주요 수치는 다음과 같다.

| 판정 | Damage | Hitstop frame | Stagger | Gauge | 비고 |
|---|---:|---:|---:|---:|---|
| Basic 1 | 2 | 3 | Level1 | 0.5 | Hashing |
| Basic 2 | 2 | 3 | Level1 | 0.5 | Hashing |
| Basic 3 | 4 | 3 | Level1 | 0.5 | Hash/NoHash Signal 분리 |
| Basic 4 | 6 | 3 | Level1 | 0.5 | Hash/NoHash Signal 분리 |
| Run 1 | 8 | 3 | Level1 | 0.5 | NoHashing |
| Run 2 | 12 | 3 | Level1 | 0.5 | NoHashing |
| Dodge 1 | 5 | 7 | Level2 | 0.5 | Trigger 포함 탐색 |
| Dodge 2 | 5 | 7 | Level2 | 0.5 | Trigger 포함 탐색 |
| Dodge End | 5 | 2 | Level2 | 0.5 | 근접 |
| Dodge End Range | 4 | 2 | Level2 | 0.2 | 반복 범위 판정 |

모든 플레이어 공격 LayerMask는 현재 `Enemy Hurtbox` 레이어를 가리킨다.

### 5.8 회피 투사체

`ProjectileHitbox`는 `Assets/02_Scripts/Player/ProjectileHitbox.cs:6`이다.

- `OnEnable`에서 VFX 시간을 등록하고 런타임 Particle bake용 카메라를 준비한다.
- `LateUpdate`(`:117`)에서 전투 시간 간격을 누적한다.
- `TickDamage`(`:126`)에서 파티클 메시의 현재 bounds를 Box 범위로 사용한다.
- `GiveDamage`(`:144`)에서 `EnemyCore`만 선별하여 피해, 게이지, SFX, victims-only Hitstop을 적용한다.

현재 프리팹 값은 Damage 2, Gauge 0.2, 판정 간격 전투 시간 0.2초, Hitstop 1 frame, Stagger Level2다. 수명과 소멸은 코드 풀보다 파티클/프리팹 구성에 의존한다.

## 6. 적 기능 명세

### 6.1 상태 흐름

`EnemyStateMachine`은 `Assets/02_Scripts/Enemy/StateMachine/EnemyStateMachine.cs:3`에서 상태를 생성하며 전이는 `:60`이다.

```text
Idle
  -> 타깃 감지: Engage
Engage
  -> 포지셔닝 행동: Hold / Forward / Backward / Left / Right
  -> 공격 권한 + 거리 + 공격 통로 확보: CloseAttackNotice
CloseAttackNotice
  -> 타깃 추적/회전/워프 계획
  -> 전이 시각 도달: CloseAttack + 타깃 고정
CloseAttack
  -> Animation Event Hit1
  -> Animator의 두 번째 콤보 상태 진입 감지
  -> Animation Event Hit2
  -> Animation End: Engage 또는 Idle
Any living state
  -> 경직 조건 충족: FrontHit / BackHit
  -> HP <= 0: Dead
```

관련 클래스 시작점은 다음과 같다.

- Idle: `EnemyIdleState.cs:3`
- Engage: `EnemyEngageState.cs:4`
- 이동 공통: `EnemyCombatMoveSubState.cs:3`
- 공격 예고 공통: `EnemyAttackNoticeState.cs:3`
- 근접 예고: `EnemyCloseAttackNoticeState.cs:3`
- 공격 공통: `EnemyAttackState.cs:3`
- 근접 공격: `EnemyCloseAttackState.cs:3`
- 피격: `EnemyFrontHitState.cs:4`, `EnemyBackHitState.cs:4`
- 사망: `EnemyDeadState.cs:3`

### 6.2 감지, 포지셔닝, 공격 순번

`EnemyTargetDetector` (`Assets/02_Scripts/Enemy/EnemyTargetDetector.cs:3`)가 Player Hurtbox LayerMask에서 타깃을 찾는다. 현재 감지 거리 18, 상실 거리 20이다.

`EnemyPositioningController` (`Assets/02_Scripts/Combat/EnemyPositioningController.cs:19`)는 슬롯 기반 진형 대신 각 적의 유동적인 전투 반경과 궤도 방향을 관리한다.

- 등록/탐색: `OnEnable` `:76`, `Register` `:145`
- 행동 결정: `GetRecommendedAction` `:174`
- 공격 통로 검사: `CanBeginAttack` `:190`
- 공격자 확정/해제: `NotifyAttackStarted` `:220`, `NotifyAttackEnded` `:232`
- 충돌 회피 이동 보정: `AdjustMovement` `:238`
- 개인 반경/궤도/분리 steering: `CalculateSteering` `:300`

현재 주요 설정은 전투 반경 2.1~4.4, 개인 공간 1.35, 공격 가능 거리 1.15~4.25, 공격 통로 반폭 0.9다. 한 적이 공격자로 확정되면 통로를 막는 다른 적에게 옆으로 양보하도록 요청한다.

`EnemyAttackTimingController` (`Assets/02_Scripts/Combat/EnemyAttackTimingController.cs:5`)는 동시에 여러 적이 무작위로 공격하지 않도록 전역 공격 권한을 관리한다.

- 등록: `:54`
- 공격 권한 요청: `:72`
- 권한 반환: `:95`
- 피격된 적 우선권 부여: `:106`

현재 프리팹의 첫 공격 대기는 1.5~3.5 전투초, 같은 적의 반복 공격 대기는 18~25 전투초, 전역 공격 간격은 0.35~1.1 전투초다. 이 값은 코드의 기본 반복 간격보다 훨씬 길다.

### 6.3 공격 예고, 추적, Root Motion 워핑

`EnemyAttackTargetingController`는 `Assets/02_Scripts/Enemy/Attack/EnemyAttackTargetingController.cs:8`에서 시작하는 순수 C# 객체다.

1. `Begin`(`:38`)이 공격 SO와 현재 타깃으로 예고/피해 범위와 워프 계획을 만든다.
2. 예고 상태 중 `UpdateTarget`(`:58`)이 제한된 회전 속도와 최대 워프 거리를 지키며 계획을 갱신한다.
3. 실제 공격 전환 순간 `Lock`(`:124`)으로 타깃과 계획을 고정한다.
4. 공격 애니메이션 Root Motion은 `WarpRootMotion`(`:142`)에서 목표 접촉 거리 쪽으로 보정된다.
5. `EnemyCore.HandleAttack`(`EnemyCore.cs:337`)은 충돌 판정 직전에 `CompleteAttackWarpForImpact`(`:343`)를 호출하고 Physics Transform을 동기화한다.

적 쪽에 퍼펙트 회피 Detector가 별도로 있는 것이 아니라, 플레이어의 `PlayerPerfectDodgeDetector`가 각 적의 `EnemyCore.IsPlayerInEnemyAttackRange`(`EnemyCore.cs:381`)를 호출한다. 이 판정은 같은 공격 SO의 예고 확장 범위를 사용하므로 화면의 공격 예고와 회피 판정이 같은 데이터에서 나온다.

### 6.4 적 공격 데이터와 판정

`EnemyAttackSO`는 `Assets/02_Scripts/Enemy/Attack/Attack Data/EnemyAttackSO.cs:4`, 개별 판정 구조체는 `EnemyAttackHitboxInfo.cs:5`다.

| 에셋 | Attack ID | Damage | Hitstop | 범위 | 전이 | Warp | 회전 속도 |
|---|---:|---:|---:|---|---:|---:|---:|
| `Close Attack Hit1.asset` | 0 | 10 | 5 | Box, size 약 (2.10, 1.99, 1.29), offset 약 (0, 1.03, 1.27) | 0.5 | 10 | 360 |
| `Close Attack Hit2.asset` | 1 | 10 | 4 | Box, size 약 (2.10, 1.99, 1.16), offset 약 (0, 1.03, 1.04) | 0.5 | 5 | 360 |

두 공격 모두 Player Hurtbox LayerMask를 사용하고 예고 범위에 각 축 0.5를 더한다.

`EnemyHitboxPool` (`Assets/02_Scripts/Enemy/Attack/EnemyHitboxPool.cs:4`)은 Box/Sphere 판정 오브젝트를 미리 만들고 재사용한다.

- 초기 풀 생성: `Awake` `:24`
- SO의 모든 판정 배치/검사: `SpacingHitboxes` `:36`
- 중복 제거 후 피해: `GiveDamage` `:79`
- 부족한 Box/Sphere 생성: `:119`, `:131`

피격 데이터는 `DamageData` (`Assets/02_Scripts/Combat/DamageData.cs:13`)로 Player에 전달된다.

### 6.5 적 경직과 사망

`EnemyCore.TryTakeDamage`(`EnemyCore.cs:168`)는 체력을 먼저 감소시키고 피격 이벤트와 VFX를 보낸다. `ShouldEnterHitReaction`(`:191`)은 다음을 함께 본다.

- 들어온 `StaggerLevel`
- 최소 반응 레벨과 강제 반응 레벨
- 누적 피격 횟수의 무작위 임계치
- 현재 경직 저항 시간
- 공격 중인지와 우선순위

현재 Striker 프리팹은 피격 반응 임계치를 6~10회, 임계치 초기화 1.25초, 반응 후 저항 1~1.5초로 직렬화한다.

피격 방향 상태는 Animator의 정확한 전체 경로 문자열에 의존한다.

- `EnemyFrontHitState.cs:6`: `Base Layer.Hit.Hit Front`
- `EnemyBackHitState.cs:6`: `Base Layer.Hit.Hit Back`

HP가 0 이하가 되면 `EnemyDeadState`로 가고, `EnemyCore.DeadStart`(`:416`)가 Collider를 Trigger로 바꾸고 사망 VFX/SFX와 이벤트를 발생시킨다.

## 7. 비코드 에셋과 상호관계

### 7.1 레이어 계약

`ProjectSettings/TagManager.asset`의 현재 사용자 레이어는 다음 계약으로 쓰인다.

| Layer | 이름 | 소비자 |
|---:|---|---|
| 6 | Enemy Hurtbox | 플레이어 근접 공격, 스킬, 투사체 |
| 7 | Ground | Player/Enemy 지면 보정 및 환경 충돌 |
| 8 | Player Hurtbox | 적 감지와 공격 SO 판정 |

레이어 이름이나 인덱스를 변경하면 프리팹과 SO의 직렬화된 bit mask도 함께 갱신해야 한다.

### 7.2 Player Animator Controller

컨트롤러는 `Assets/03_Animations/Player/Controllers/PlayerAnimatorController.controller`다. 직접 작성한 FSM이 Trigger를 켜고, Animator는 다음 표현 상태를 재생한다.

- Idle
- Run Start/Loop/Stop Left/Stop Right
- Fast Run Loop/Stop/Turn
- Basic Attack 1~4
- Front Dodge/Stop, Back Dodge
- Dodge Attack Start/Loop/End
- Run Attack
- Skill
- Hit Front/Back/Large

대부분의 모션 클립은 `Assets/Download Assets/Scythe Animation Pack`에서 오지만, 상태 선택·Root Motion 소비·타격 시점은 직접 작성 코드와 Timeline이 결정한다. 플레이어 공격 모션에서 gameplay Animation Event는 발견되지 않았고, 공격 판정 시점은 주로 Timeline Signal/Marker가 담당한다.

컨트롤러에 `IsSkill` Trigger와 Skill 상태가 있으나 `PlayerSkillState.Enter`는 이 Trigger를 직접 설정하지 않는다. 현재 Skill Timeline의 Animation Track이 스킬 모션 재생의 실질 경로이므로, Timeline binding이 누락되면 Animator 상태만으로는 대체되지 않는다.

### 7.3 Player Timeline Director 매핑

`DirectorID`는 `Assets/02_Scripts/Player/Timeline Director Container/DirectorID.cs:1`, 매핑 구조체는 `DirectorInfo.cs:6`, 실제 사전 구성은 `TimelineDirectorContainer.InitTimelineDirectorContainer`(`TimelineDirectorContainer.cs:18`)다.

| ID | Timeline |
|---:|---|
| 0 | Basic Attack 1 |
| 1 | Basic Attack 2 |
| 2 | Basic Attack 3 |
| 3 | Basic Attack 4 |
| 4 | Run Attack |
| 5 | Dodge Attack Start |
| 6 | Dodge Attack Loop |
| 7 | Dodge Attack End |
| 8 | Skill |

현재 모든 Director의 `HitStopParticleMode`는 0, 즉 `FreezeWithHitStop`이다. `TimelineDirectorContainer.ConfigureParticleControl`(`:58`)이 Control Track 아래 Particle/VFX를 찾아 `CombatVfxTime`에 등록하고, `SetCombatSpeed`(`:89`)가 playable graph 속도를 전투 시간 스케일에 맞춘다.

### 7.4 공격 Timeline의 실제 Signal 시각

아래 시간은 각 `.playable` 에셋의 현재 직렬화값이며 초 단위다.

| Timeline | Signal 시각 |
|---|---|
| Basic 1 | SFX 0.0333, Hit 0.2833, Next Combo Enable 0.5000, Disable 0.8000 |
| Basic 2 | SFX 0.0333, Ground Crack 0.2500, Hit 0.3167, Enable 0.4000, Disable 0.7833 |
| Basic 3 | SFX 0.0333/0.2167, NoHash Hit 0.1667, Hash Hit 0.4000, Enable 0.6500, Disable 1.0333 |
| Basic 4 | SFX 0/0.2667/0.4500, NoHash Hit 0.1333/0.4000, Hash Hit 0.6667, Enable 0.8500, Disable 1.2000 |
| Run Attack | SFX 0.0833/0.3333, Hit1 0.3000, Hit2 0.5000, 다른 행동 허용 0.9000, 종료 1.8167 |
| Dodge Start | gameplay Signal 없음 |
| Dodge Loop | SFX 0/0.2833, Effect1 0, Damage1 0.0500, Effect2 0.2500, Damage2 0.3333 |
| Dodge End | Damage 0.0667, Range Damage 0.1000/0.1500/0.2000/0.2500, Projectile 0.2333 |

Signal 자산은 `Assets/05_Timeline Signals/Player`에 있고, Player 프리팹의 `SignalReceiver`가 `PlayerAnimationEvent`, `PlayerAnimationEventSender`, `PlayerAttackInstanceContainer`, `PlayerSFXController` 메서드에 연결한다.

### 7.5 Skill Timeline: 타격, 잔상, 투명도

`PlayerSkillHitTrack` (`PlayerSkillHitTrack.cs:5`)의 `PlayerSkillHitMarker` (`PlayerSkillHitMarker.cs:6`)가 `PlayerSkillAttackReceiver`에 notification을 보낸다. Receiver는 자신의 Skill Director에서 온 marker인지와 track 소유권을 확인하고 `Physics.SyncTransforms` 후 공격 판정을 실행한다.

현재 `SkillTimeline.playable`에 존재하는 Custom Hit Marker는 9개다.

| 시간 | 사용 필드 | 선택 방식 |
|---:|---:|---|
| 0.7167 | Hit 1 | 자동 |
| 0.7500 | Hit 3 | 명시 |
| 0.7833 | Hit 5 | 명시 |
| 0.8167 | Hit 7 | 명시 |
| 0.8500 | Hit 9 | 명시 |
| 0.8833 | Hit 11 | 명시 |
| 0.9167 | Hit 12 | 명시 |
| 0.9500 | Hit 8 | 자동(시간 순번) |
| 0.9833 | Hit 9 | 자동(시간 순번) |

즉 현재 판정 순서는 `1, 3, 5, 7, 9, 11, 12, 8, 9`다. Skill Slash 1~8 Signal은 별도 Transform 인자를 사용해 시각 효과를 발생시키므로 “Slash Signal 개수”와 “Damage Marker 개수”는 같은 개념이 아니다.

현재 Player 프리팹의 Skill Hit 1~11은 Damage 10, Hitstop 5이고, Hit 12는 Damage 10, Hitstop 7이다. 모두 Level2 경직과 attacker+victims Hitstop을 사용한다. 게이지 보상은 Hit 1에만 2가 설정되어 있다.

투명도는 다음 커스텀 Playable이 담당한다.

- Clip: `PlayerOpacityClip.cs:5`
- Track: `PlayerOpacityTrack.cs:8`
- Mixer: `PlayerOpacityMixer.cs:4`
- 런타임 복제 Material 관리: `PlayerOpacityMaterials.cs:8`
- 비활성화/그래프 종료 안전 정리: `PlayerOpacityLifetime.cs:7`

현재 Skill의 opacity 구간은 약 0.7000~0.9167초(42~55 frame)이며, 0.7000~0.7500 fade-in, 0.7500~0.8500 투명 유지, 0.8500~0.9167 fade-out이다. 원본 Material을 직접 변경하지 않고 런타임 복제본을 Renderer에 적용한 뒤 정리한다.

`MeshTrailEffect` (`Assets/02_Scripts/Trail/MeshTrailEffect.cs:5`)는 `ActiveSkill`(`:44`) 또는 회피용 `ActiveDodge`(`:35`)로 시작하고, 코루틴이 SkinnedMesh를 반복 bake해 유령 메시를 만든다. Combat slow scale과 소유자의 Hitstop을 모두 반영한다.

### 7.6 Skill 카메라 관계와 씬 차이

`PlayerSkillCameraDirector` (`Assets/02_Scripts/Player/PlayerSkillCameraDirector.cs:4`)는 카메라 Transform의 원래 local pose를 저장하고, Signal에 따라 부모를 제거하거나 Player Model에 다시 붙인다.

현재 중요한 점은 다음과 같다.

- 이 컴포넌트와 Skill hit impulse 참조는 `DemoScene 1`의 Player 인스턴스에만 추가/오버라이드되어 있다.
- Demo 씬의 `SignalReceiver`에는 `Skill Cine Cam Parent Null/Model Signal` 두 항목과 `CinemachineRemoveParent`/`CinemachineAddParent` 콜백이 추가되어 있다.
- 그러나 두 Signal 자산 GUID는 현재 Skill Timeline을 포함한 어떤 `.playable`에서도 참조되지 않는다. Receiver 등록만 있고 Emitter가 없으므로 현 에셋 상태에서는 두 콜백이 자동 호출되지 않는다.
- `CombatPrototypeScene`에는 위 Demo 전용 카메라 확장이 없다.

스킬 카메라 연출을 기대한다면 Skill Timeline에 두 Signal Emitter를 명시적으로 배치하고 두 씬 모두에서 Receiver와 카메라 참조를 검증해야 한다.

### 7.7 Enemy Animator와 Animation Event

Enemy Animator Controller는 `Assets/03_Animations/Enemy/Striker/Striker Animator Controller.controller`다. Idle, 전후좌우 이동, Close Attack 2연타, Front/Back Hit, Dead 상태를 가진다.

적 공격 판정은 Player와 달리 Timeline Signal이 아니라 Animation Clip Event가 실질 호출점이다.

| Clip | 시간 | Animation Event | 호출 결과 |
|---|---:|---|---|
| `Attack_Combo_01_01_Anim.anim` | 0 | `OnCloseAttackSlashSFX` | 베기 SFX |
| 동일 | 0.1667 | Trail Active | 무기 Trail 켜기 |
| 동일 | 0.2333 | `OnAttack(Close Attack Hit1)` | `EnemyCore.HandleAttack` -> Hit1 SO 판정 |
| 동일 | 0.3667 | Trail Inactive | 무기 Trail 끄기 |
| `Attack_Combo_01_02_Anim.anim` | 0 | `OnCloseAttackSlashSFX` | 베기 SFX |
| 동일 | 0.2333 | Trail Active | 무기 Trail 켜기 |
| 동일 | 0.3500 | `OnAttack(Close Attack Hit2)` | Hit2 SO 판정 |
| 동일 | 0.4500 | Trail Inactive | 무기 Trail 끄기 |
| 동일 | 1.7333 | `OnAnimationEnd` | 공격 상태 종료 |

호출 브리지는 다음 순서다.

```text
Animation Clip Event
  -> EnemyAnimationEventSender
  -> EnemyAnimationEvent의 직접 메서드 호출
  -> C# event
  -> EnemyCore / EnemyCloseAttackState / EnemySFX
```

`EnemyCloseAttackState`는 두 번째 타격 진입을 `Attack_Combo_01_02_Anim`이라는 exact short state name hash로 판별한다(`EnemyCloseAttackState.cs:8`). Animator 상태 이름을 바꾸면 코드도 함께 수정해야 한다.

`Assets/04_Timelines/Striker/Attack/Close Attack 1 Timeline.playable`과 `Close Attack 2 Timeline.playable`은 존재하지만, 해당 GUID를 참조하는 프리팹/씬/다른 에셋이 현재 없다. 활성 공격 흐름은 Animator와 Animation Event 기반이다.

## 8. 공통 전투 시스템

### 8.1 피해 계약

- `IDamageable` (`Assets/02_Scripts/Combat/IDamageable.cs:1`): `TryTakeDamage` 계약
- `DamageData` (`Assets/02_Scripts/Combat/DamageData.cs:13`): 공격자, 피해량, Hitstop frame, 경직 레벨
- `IHitStopParticipant` (`Assets/02_Scripts/Combat/IHitStopParticipant.cs:1`): Hitstop 시작/종료 계약
- `HitDirectionCalculator` (`Assets/02_Scripts/Combat/HitDirectionCalculator.cs:3`): 공격자 방향을 Front/Back으로 분류

플레이어는 무적 상태에서 `PlayerCore.TryTakeDamage`(`PlayerCore.cs:210`)가 false를 반환한다. 적은 피해를 받은 뒤 경직 조건을 별도로 평가한다.

### 8.2 Hitstop

`HitstopCoordinator`는 `Assets/02_Scripts/Combat/HitstopCoordinator.cs:5`의 정적 진입형 singleton이다.

- attacker+victims 요청: `Request` `:28`
- victims-only 요청: `RequestVictimsOnly` `:51`
- 현실 시간 기준 해제: `Update` `:92`
- 애니메이션 평가가 끝난 뒤 새 정지 적용: `LateUpdate` `:142`

프레임은 60Hz 기준 시간으로 변환된다. attacker+victims 모드에서는 공격자를 N frame, 피해자를 N+1 frame 멈춰 공격자의 애니메이션이 피해자보다 먼저 재개되도록 한다.

Player/Enemy의 `BeginHitStop`은 Animator와 물리 이동을 정지시키고, 종료 시 현재 `CombatTimeController.Scale`을 다시 적용한다. Timeline graph와 VFX도 별도 어댑터를 통해 같은 상태를 따른다.

### 8.3 전투 슬로모션과 VFX 시간

`CombatTimeController` (`Assets/02_Scripts/Combat/CombatTimeController.cs:6`)는 `Time.timeScale`을 전역 변경하지 않고 자체 `Scale`, `DeltaTime`을 제공한다. `Begin`(`:24`)과 `End`(`:44`)는 소유자별 `CombatSlowMotion`을 만들거나 해제한다.

`CombatVfxTime` (`Assets/02_Scripts/Combat/CombatVfxTime.cs:7`)은 `RegisterHierarchy`(`:20`)로 ParticleSystem, VisualEffect, Tiny.Trail을 찾아 동적으로 붙는다. 전투 scale과 소유자 Hitstop을 조합해 `ApplyScale`(`:85`)에서 효과 재생 속도를 조절한다.

### 8.4 VFX, 화면 효과, 오디오

| 기능 | 클래스/위치 | 동작 |
|---|---|---|
| 공용 이펙트 요청 | `CombatEffectRequestBus`, `CombatEffectInfo.cs:49` | 공격 예고 등 생성 요청을 정적 이벤트로 전달한다. |
| 공용 이펙트 풀 | `CombatEffectPool.cs:7` | 요청을 받아 미리 만든 인스턴스를 활성화하고 전투 시간 수명 후 회수한다. 현재 AttackNotice prewarm 4, max 12, duration 1초다. |
| 퍼펙트 회피 화면 효과 | `GlobalVolumeEffectController.cs:7` | Volume profile의 Vignette 런타임 인스턴스를 만들고 성공 이벤트에 unscaled-time fade를 실행한다. |
| Player 효과 중계 | `PlayerAnimationEvent.cs:4` | Timeline/Animation Event를 dash, dodge, projectile, trail, slash VFX로 변환한다. |
| Player 오디오 | `PlayerSFXController.cs:3`, `ProjectileSFX.cs:3` | 공격 종류별/투사체 반복 SFX를 재생한다. |
| Enemy 효과 중계 | `EnemyAnimationEvent.cs:5` | 공격 예고 pool 요청, trail, SFX, 공격 이벤트를 중계한다. |

## 9. UI와 경고 표시

| 기능 | 클래스 시작 | 참조와 생명주기 |
|---|---:|---|
| Player HP | `PlayerHealthUI.cs:4` | `Awake`에서 Player HP 이벤트를 구독하고 `Update`에서 Slider를 보간한다. 현재 구독 해제가 없다. |
| Skill Gauge | `PlayerSkillGaugeUI.cs:4` | 게이지 이벤트를 구독하고 Slider를 보간한다. 현재 구독 해제가 없다. |
| 단일 적 HP Bar | `EnemyHealthBar.cs:4` | 적 피격/사망 이벤트를 구독하고 월드 위치를 화면으로 투영한다. `OnDestroy`에서 해제한다. |
| 적 HP Bar 관리자 | `EnemyHealthesUI.cs:5` | 지정 적마다 bar를 만들고 거리/피격/사망 기준으로 alpha를 조절한다. |
| 후방 적 경고 | `CameraBehindEnemyDetector.cs:9` | 적별 marker를 만들고, 화면 밖이면서 살아 있는 적을 Player 주위 링에 표시한다. 공격 예고 중에는 warning marker를 사용한다. |

Player HP/Gauge UI는 Player보다 먼저 파괴되거나 다시 활성화되는 수명 구조에서 중복 구독/잔존 delegate 문제가 생길 수 있으므로 `OnDestroy` 또는 `OnDisable` 정리가 필요하다.

## 10. 참조 및 호출 위치 빠른 인덱스

### 10.1 Player 핵심

| 클래스 | 시작 줄 | 먼저 볼 메서드 |
|---|---:|---|
| `PlayerCore` | `PlayerCore.cs:5` | `Awake:84`, `Update:128`, `TryTakeDamage:210`, `BeginPerfectDodge:236`, `TryBeginDodgeAttack:310` |
| `PlayerInputCollector` | `PlayerInputCollector.cs:5` | `Awake:31`, `Update:40` |
| `PlayerStateMachine` | `PlayerStateMachine.cs:3` | 생성자 영역 `:35`, `Transition:95` |
| `PlayerStateBase` | `PlayerStateBase.cs:3` | `Enter:16`, `Exit:46`, `HandleDamaged:57` |
| `PlayerMover` | `PlayerMover.cs:5` | `FixedUpdate:43`, `Move:65`, `SetHitStopped:81`, `ApplyGroundHover:160` |
| `PlayerRotator` | `PlayerRotator.cs:3` | `Update:22`, `RotateLerp:27`, `RotateToward:33` |
| `PlayerTargetDetector` | `PlayerTargetDetector.cs:3` | `DetectTargets:54`, `AcquireBasicAttackTarget:85` |
| `PlayerAttackInstanceContainer` | `PlayerAttackInstanceContainer.cs:5` | 공격별 Signal 수신 `:40~127`, 공통 판정 `:132` |
| `TimelineDirectorContainer` | `TimelineDirectorContainer.cs:6` | `Init:18`, `Play:35`, `SetCombatSpeed:89` |
| `PlayerSkillAttackReceiver` | `PlayerSkillAttackReceiver.cs:7` | `Awake:22`, `OnNotify:31` |
| `PlayerAnimationEvent` | `PlayerAnimationEvent.cs:4` | 이벤트 중계 `:31~143` |
| `MeshTrailEffect` | `MeshTrailEffect.cs:5` | `ActiveDodge:35`, `ActiveSkill:44`, 생성 코루틴 `:53` |

### 10.2 Player 상태

| 기능군 | 클래스 |
|---|---|
| 대기 | `PlayerIdleState` (`PlayerIdleState.cs:3`) |
| 일반 달리기 | `PlayerRunStartState`, `PlayerRunLoopState`, `PlayerRunStopLeftState`, `PlayerRunStopRightState` (각 파일 `:3`) |
| 빠른 달리기 | `PlayerFastRunLoopState`, `PlayerFastRunTurnState`, `PlayerFastRunStopState` (각 파일 `:3`) |
| 기본 공격 | `PlayerBasicAttackState` (`:4`), `PlayerBasicAttack1~4State` (각 `:1`) |
| 달리기 공격 | `PlayerRunAttackState` (`:4`) |
| 회피 | `PlayerDodgeState`, `PlayerFrontDodgeState`, `PlayerFrontDodgeStopState`, `PlayerBackDodgeState` (각 `:3`) |
| 회피 반격 | `PlayerDodgeAttackState` (`:4`), Start/Loop/End (각 `:3`) |
| 스킬 | `PlayerSkillState` (`:4`) |
| 피격 | `PlayerHitState`, Front/Back/Large (각 `:3`) |

### 10.3 Enemy 핵심

| 클래스 | 시작 줄 | 먼저 볼 메서드 |
|---|---:|---|
| `EnemyCore` | `EnemyCore.cs:5` | `Awake:81`, `Update:124`, `TryTakeDamage:168`, `TryBeginAttack:222`, `HandleAttack:337`, `DeadStart:416` |
| `EnemyStateMachine` | `EnemyStateMachine.cs:3` | 상태 생성 `:17`, `Transition:60` |
| `EnemyTargetDetector` | `EnemyTargetDetector.cs:3` | `Awake:15`, `Update:20` |
| `EnemyAttackTargetingController` | `EnemyAttackTargetingController.cs:8` | `Begin:38`, `UpdateTarget:58`, `Lock:124`, `WarpRootMotion:142` |
| `EnemyHitboxPool` | `EnemyHitboxPool.cs:4` | `SpacingHitboxes:36`, `GiveDamage:79` |
| `EnemyAttackTimingController` | `EnemyAttackTimingController.cs:5` | `TryBeginAttack:72`, `ReleaseAttack:95` |
| `EnemyPositioningController` | `EnemyPositioningController.cs:19` | `GetRecommendedAction:174`, `CanBeginAttack:190`, `AdjustMovement:238` |
| `EnemyAnimationEventSender` | `EnemyAnimationEventSender.cs:5` | Animation Event 진입 메서드 `:10~35` |
| `EnemyAnimationEvent` | `EnemyAnimationEvent.cs:5` | SO 공격/VFX/SFX 중계 |

### 10.4 Enemy 상태

| 기능군 | 클래스 |
|---|---|
| 공통 | `EnemyStateBase` (`EnemyStateBase.cs:5`), `EnemyCompositeState` (`EnemyCompositeState.cs:3`) |
| 대기/교전 | `EnemyIdleState` (`:3`), `EnemyEngageState` (`:4`) |
| 교전 이동 | `EnemyCombatMoveSubState`와 Hold/Forward/Backward/Left/Right (각 파일 `:3`) |
| 공격 예고 | `EnemyAttackNoticeState`, `EnemyCloseAttackNoticeState` (각 `:3`) |
| 공격 | `EnemyAttackState`, `EnemyCloseAttackState` (각 `:3`) |
| 피격 | `EnemyFrontHitState`, `EnemyBackHitState` (각 `:4`) |
| 사망 | `EnemyDeadState` (`:3`) |

### 10.5 에디터 검증 및 개발 보조 코드

아래 파일도 직접 작성 코드지만 플레이어 빌드의 정상 전투 루프에는 포함되지 않는다.

| 클래스 | 시작/진입 줄 | 용도 |
|---|---:|---|
| `AnimationRootMotionMultiplierPreview` | `AnimationRootMotionPreview.cs:7` | `#if UNITY_EDITOR`와 `[ExecuteAlways]` 기반의 Root Motion 배율 미리보기다. Play Mode에서는 동작하지 않는다. |
| `PlayerSkillHitVerification` | `PlayerSkillHitVerification.cs:11`, 메뉴 `:38` | 임시 공격자/피해자를 만들어 Skill marker, 판정, Hitstop을 검증한다. |
| `PlayerOpacityVerification` | `PlayerOpacityVerification.cs:12`, 메뉴 `:30` | Skill Timeline의 시점별 opacity와 Material 복원 여부를 검증한다. |
| `PerfectDodgeVerification` | `PerfectDodgeVerification.cs:12`, 메뉴 `:23` | 슬로모션 곡선, 무적, Hitstop, 상태 인계 등을 검증한다. |
| `SkillMotionVerification` | `SkillMotionVerification.cs:11`, 메뉴 `:42` | 여러 frame rate와 Hitstop 길이에서 Skill Root Motion 보존을 검증한다. |
| `PlayerSkillAttackReceiverEditor` | `PlayerSkillHitEditors.cs:7` | Skill Receiver Inspector 보조 UI다. |
| `PlayerSkillHitMarkerEditor` | `PlayerSkillHitEditors.cs:44` | Timeline marker의 자동/명시 필드 선택 Inspector다. |
| `PlayerSkillHitTestActor` | `PlayerSkillHitTestActor.cs:8` | 위 검증 도구가 임시 생성하는 `IDamageable`/`IHitStopParticipant` 시험체다. 씬/프리팹 참조는 없다. |

## 11. 미완성·비활성·주의 항목

### 높은 우선순위

1. **Build Settings가 존재하지 않는 씬을 가리킨다.** `ProjectSettings/EditorBuildSettings.asset:9`의 `Assets/Scenes/SampleScene.unity`는 현재 없다. 실제 두 전투 씬도 Build Settings에 등록되어 있지 않다.
2. **Player 사망 흐름이 없다.** 체력은 0까지 감소하지만 Dead 상태/입력 차단/사망 이벤트가 없다.
3. **Skill 카메라 Signal은 Receiver만 있고 Timeline Emitter가 없다.** Demo 씬 설정만으로는 카메라 parent 콜백이 호출되지 않는다.
4. **퍼펙트 회피 반격 Start의 motion warp가 빈 메서드다.** `PlayerDodgeAttackStartState.cs:38`.

### 데이터/문서 드리프트

1. 현재 Skill Timeline Custom Hit Marker는 9개이며, 16개로 적힌 기존 설명과 다르다.
2. 현재 Skill Hitstop은 Hit 1~11이 5, Hit 12가 7이며, 기존 문서의 더 작은 값과 다르다.
3. 이 문서의 표는 현재 YAML을 기준으로 하므로 Timeline/SO/Prefab 수정 후 다시 동기화해야 한다.

### 사용되지 않거나 불완전한 코드

- `AttackSO` (`Assets/02_Scripts/Combat/AttackSO.cs:4`): DamageAmount 하나만 가진 별도 SO이며 활성 전투 흐름 참조가 없다.
- `EnemyTimelineEvent` (`Assets/02_Scripts/Enemy/EnemyTimelineEvent.cs:3`): 빈 `Start`/`Update`만 있다.
- `EnemyAttackSimulator.AttackRangeSimulation` (`EnemyAttackSimulator.cs:19`): SO와 회전을 지역 변수에 넣고 아무 작업도 하지 않는다.
- Enemy Close Attack 1/2 Timeline: 현재 참조되지 않는다.
- `PlayerHitLargeState`: 상태는 있으나 피격 분기에서 선택되지 않는다.
- Parry Input Action: 바인딩은 있으나 수집/소비 코드가 없다.
- `EnemyAttackKnockBackType`: 데이터 타입은 있으나 현재 피해 처리에서 사용되지 않는다.

### 유지보수 위험

- Striker 프리팹의 `EnemyHitboxPool` 스크립트 GUID는 맞지만 직렬화된 `m_EditorClassIdentifier`가 과거 이름인 `EnemyAttackRequester`로 남아 있다. Unity는 보통 `m_Script` GUID를 우선하지만 이름 변경 흔적으로 정리 대상이다.
- Animator의 exact state name/hash와 Input Action 문자열은 컴파일러가 검증하지 않는다. 이름 변경 시 런타임에서만 깨질 수 있다.
- Player HP/Gauge UI는 이벤트 구독을 해제하지 않는다.
- `PlayerSkillState`의 `_isTransitionIdle` 값은 설정만 되고 전이에 사용되지 않는다.
- Player prefab 단독 실행 시 씬에서 주입하던 Camera/Enemy/UI 참조가 빠질 수 있다.

## 12. 권장 코드 추적 순서

플레이어 기본 공격 한 번을 따라가려면 다음 순서가 가장 짧다.

1. `PlayerInputCollector.Update` (`PlayerInputCollector.cs:40`)
2. 현재 Player 상태의 `UpdateTick`
3. `PlayerStateMachine.Transition` (`PlayerStateMachine.cs:95`)
4. `PlayerBasicAttackState.Enter` (`PlayerBasicAttackState.cs:18`)
5. `TimelineDirectorContainer.Play` (`TimelineDirectorContainer.cs:35`)
6. 해당 `.playable`의 Hit Signal
7. `PlayerAttackInstanceContainer`의 공격별 메서드 (`:40~127`)
8. 공통 `GiveDamageField` (`:132`)
9. `EnemyCore.TryTakeDamage` (`EnemyCore.cs:168`)
10. `EnemyStateBase`/현재 상태의 피격 이벤트와 `ShouldEnterHitReaction` (`EnemyCore.cs:191`)

적 근접 공격을 따라가려면 다음 순서가 적합하다.

1. `EnemyTargetDetector.Update` (`EnemyTargetDetector.cs:20`)
2. `EnemyIdleState` -> `EnemyEngageState`
3. `EnemyPositioningController.GetRecommendedAction` (`EnemyPositioningController.cs:174`)
4. `EnemyCore.TryBeginAttack` (`EnemyCore.cs:222`)
5. `EnemyAttackTimingController.TryBeginAttack` (`EnemyAttackTimingController.cs:72`)
6. `EnemyPositioningController.CanBeginAttack` (`EnemyPositioningController.cs:190`)
7. `EnemyCloseAttackNoticeState`
8. `EnemyAttackTargetingController.Begin/UpdateTarget/Lock` (`:38/:58/:124`)
9. Striker 공격 Animation Clip의 `OnAttack` Event
10. `EnemyCore.HandleAttack` (`EnemyCore.cs:337`)
11. `EnemyHitboxPool.SpacingHitboxes` (`EnemyHitboxPool.cs:36`)
12. `PlayerCore.TryTakeDamage` (`PlayerCore.cs:210`)

스킬을 따라가려면 `PlayerSkillState.Enter` -> Skill Director -> Skill Timeline의 `PlayerSkillHitMarker` -> `PlayerSkillAttackReceiver.OnNotify` -> `PlayerAttackInstanceContainer.GiveDamageFieldNoHashing` 순서로 보면 된다.

---

이 문서는 실제 플레이 모드 동적 로그가 아니라 코드 및 직렬화 에셋을 상호 대조한 정적 분석 결과다. Animator transition 조건, Timeline binding, 씬 오버라이드는 기록했지만 최종 체감 타이밍과 충돌 범위는 Unity Play Mode에서 Gizmo/로그와 함께 검증하는 것이 안전하다.
