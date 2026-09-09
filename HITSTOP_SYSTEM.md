# ProjectR 고정 프레임 히트스탑 시스템

최종 정리일: 2026-08-27

2026-09-05 갱신: 히트스톱 요청은 애니메이션 평가가 끝난 LateUpdate에 적용하며,
시작·해제 시계는 `Time.unscaledTimeAsDouble`을 사용한다. 플레이어 스킬과 적의 대기 중 루트 모션을 보존하고,
Rigidbody의 실제 물리 위치를 저장해 보간 위치로 되감기는 문제를 수정했다.
VFX/잔상도 소유 캐릭터의 정지 시간을 따른다. 최신 동작과 이동 거리 회귀 검증은
[PlayerSkillHits.md](Docs/PlayerSkillHits.md)의 히트스톱 설명을 참고한다. 아래 항목은 최초 구현 기록이다.

이 문서는 현재 프로젝트에 구현된 히트스탑의 구조, 파일별 변경 내용, 설정 방법과 주의점을 정리한다. 아직 코드에 반영되지 않은 아이디어는 별도 항목으로 구분한다.

## 1. 현재 동작 요약

- 전역 `Time.timeScale`을 바꾸지 않고, 타격에 관여한 플레이어와 적만 정지한다.
- 히트스탑 길이는 초가 아니라 **60Hz 기준 디자인 프레임 수**로 지정한다.
- 공격자인 플레이어는 `N`프레임, 실제 피해를 받은 적은 `N + 1`프레임 동안 정지한다.
- 현재 씬에 설정된 `Hit Stop Frame`은 모두 `2`이므로 다음과 같이 동작한다.

| 대상 | 정지 프레임 | 60Hz 환산 시간 |
|---|---:|---:|
| 플레이어 | 2프레임 | 약 33.3ms |
| 피격된 적 | 3프레임 | 약 50.0ms |

- 같은 타이밍에 여러 적을 맞혀도 플레이어 히트스탑은 한 번만 요청하고, 피해가 실제로 적용된 적들만 함께 정지한다.
- 히트스탑 중 새 히트스탑이 겹치면 종료 시각이 더 늦은 요청을 유지한다.
- 파티클은 Timeline별로 `히트스탑과 함께 정지` 또는 `계속 재생`을 선택할 수 있다.
- `HitstopCoordinator`는 씬에 직접 배치하지 않아도 런타임에 자동 생성되는 `MonoBehaviour` 컴포넌트다.

## 2. 전체 실행 흐름

```text
Timeline Signal
  -> PlayerAttackInstanceContainer.GiveDamageField()
     -> 물리 범위 검색 및 중복 Collider 제거
     -> IDamageable.TryTakeDamage()
     -> 피해 적용에 성공한 IHitStopParticipant만 수집
     -> HitstopCoordinator.Request(플레이어, 적 목록, N)
        -> 플레이어 BeginHitStop(), N프레임 뒤 해제
        -> 적 BeginHitStop(), N+1프레임 뒤 해제
        -> realtime 기준으로 종료 시각을 검사
        -> 각 대상 EndHitStop()
```

히트스탑 중 각 참가자는 자신의 Animator, FSM, 이동, 루트 모션 및 필요한 Timeline 재생 시간을 정지한다. 입력 수집 컴포넌트 자체와 게임 전체 시간은 계속 흐른다.

## 3. 파일별 핵심 변경 내용

### 3.1 전투 공통 계층

#### [HitstopCoordinator.cs](<Assets/Scripts/Combat/HitstopCoordinator.cs>)

히트스탑의 시작과 종료 시점을 중앙에서 관리한다.

- `[RuntimeInitializeOnLoadMethod(BeforeSceneLoad)]`로 런타임 시작 전에 인스턴스를 준비한다.
- 씬에 인스턴스가 없으면 `HitstopCoordinator` GameObject를 만들고 컴포넌트를 붙인 뒤 `DontDestroyOnLoad`로 유지한다.
- `CombatFrameRate = 60`을 기준으로 디자인 프레임을 실제 시간으로 환산한다.
- `Time.realtimeSinceStartupAsDouble`을 사용하므로 `timeScale`과 무관하게 종료 시점을 계산한다.
- 공격자는 `N`, 피격자는 `N + 1`프레임으로 예약한다.
- 중첩 요청은 기존 종료 시각보다 더 긴 경우에만 연장한다.
- 파괴된 참가자는 목록에서 제거하고, Coordinator가 파괴되면 남은 참가자의 정지를 모두 해제한다.
- 실행 순서를 앞당기기 위해 `[DefaultExecutionOrder(-10000)]`가 적용되어 있다.

> 이 클래스는 `MonoBehaviour`가 맞으며 실제 컴포넌트로 사용된다. 다만 씬/Prefab에 수동 배치하는 방식이 아니라 런타임 자동 생성 방식이라 Hierarchy에서 사전 참조가 보이지 않는다.

#### [IHitStopParticipant.cs](<Assets/Scripts/Combat/IHitStopParticipant.cs>) — 신규

히트스탑에 참여할 객체의 공통 계약이다.

```csharp
bool IsHitStopped { get; }
void BeginHitStop();
void EndHitStop();
```

현재 `PlayerCore`와 `EnemyCore`가 구현한다. 이후 다른 캐릭터나 파괴 가능한 오브젝트를 정지시키려면 이 인터페이스를 구현하면 된다.

#### [HitStopParticleMode.cs](<Assets/Scripts/Combat/HitStopParticleMode.cs>) — 신규

Timeline Control Track 파티클의 정책을 나타내는 enum이다.

- `FreezeWithHitStop`: 히트스탑과 함께 멈춘다.
- `ContinueDuringHitStop`: 캐릭터와 Timeline 시간이 멈춰도 파티클은 계속 재생한다.

#### [IDamageable.cs](<Assets/Scripts/Combat/IDamageable.cs>)

피해 API를 `TakeDamage()`에서 `TryTakeDamage()`로 변경했다.

```csharp
bool TryTakeDamage(DamageData damageData);
```

실제 피해가 적용되었는지를 반환하게 하여, 죽었거나 피해를 받을 수 없는 적 때문에 히트스탑이 잘못 발생하지 않도록 했다.

### 3.2 공격 판정 계층

#### [AttackDamageField.cs](<Assets/Scripts/Player/Attack Instance Container/Instances/AttackDamageField.cs>)

- 기존 `_hitStopFrame` 필드를 그대로 활용한다.
- 이 값이 60Hz 기준 프레임이며 적은 한 프레임 더 정지한다는 설명을 Inspector 툴팁에 추가했다.
- 현재 씬의 공격 판정들은 모두 `2`로 설정되어 있다.

#### [PlayerAttackInstanceContainer.cs](<Assets/Scripts/Player/Attack Instance Container/PlayerAttackInstanceContainer.cs>)

공격 판정과 히트스탑 요청을 연결하는 핵심 지점이다.

- 소유 플레이어의 `IHitStopParticipant`를 캐시한다.
- 한 번의 물리 검색에서 같은 적의 자식 Collider가 여러 개 잡혀도 한 번만 피해를 준다.
- 해싱 모드에서는 여러 Signal에 걸쳐 같은 적을 다시 타격하지 않도록 기존 피격 대상 집합도 유지한다.
- `TryTakeDamage()`가 `true`인 대상 중 `IHitStopParticipant`인 객체만 히트스탑 피해자 목록에 넣는다.
- 모든 피해 처리가 끝난 뒤 Coordinator에 한 번만 요청한다. 광역 공격에서도 플레이어 정지가 적 수만큼 중복 호출되지 않는다.

### 3.3 플레이어 계층

#### [PlayerCore.cs](<Assets/Scripts/Player/PlayerCore.cs>)

`IHitStopParticipant`를 구현하며 플레이어 정지를 총괄한다.

- 히트스탑 중 `Update`, `FixedUpdate`, `LateUpdate`, `OnAnimatorMove`에서 FSM과 루트 모션 처리를 진행하지 않는다.
- 시작 시 현재 State에 누적된 루트 모션을 비운다.
- `PlayerMover`를 정지시키고 Animator의 기존 `speed`를 저장한 뒤 `0`으로 만든다.
- 재생 중인 `PlayableDirector`의 유효한 PlayableGraph 루트 속도를 저장하고 `0`으로 만든다.
- 종료 시 Animator, 이동기, PlayableGraph의 기존 값을 복구한다.
- 비활성화될 때 히트스탑이 남아 있으면 안전하게 해제한다.

`PlayableDirector.Pause()` 대신 PlayableGraph 루트 속도를 `0`으로 만든 이유는 Control Track이 활성화한 파티클 GameObject를 유지하면서 Timeline의 로컬 시간과 Signal 진행만 멈추기 위해서다.

#### [PlayerMover.cs](<Assets/Scripts/Player/Mover/PlayerMover.cs>)

- 히트스탑 시작 시 대기 중인 이동량과 Rigidbody 속도를 제거한다.
- 기존 `RigidbodyConstraints`를 저장하고 `FreezeAll`로 고정한다.
- 정지 중 `FixedUpdate`에서도 속도가 다시 생기지 않게 한다.
- 종료 또는 비활성화 시 원래 Constraints를 복구한다.

#### [PlayerStateBase.cs](<Assets/Scripts/Player/FSM/PlayerStateBase.cs>)

`ClearAccumulatedMotion()`을 추가해 State가 들고 있던 `AnimDeltaPos`를 즉시 제거할 수 있게 했다. 해제 직후 정지 전 루트 모션이 한꺼번에 적용되는 현상을 방지한다.

#### [PlayerStateMachine.cs](<Assets/Scripts/Player/FSM/PlayerStateMachine.cs>)

현재 State의 `ClearAccumulatedMotion()`을 호출하는 전달 메서드를 추가했다.

#### 공격 State 5개

- [PlayerBasicAttack1State.cs](<Assets/Scripts/Player/FSM/States/Basic Attack/PlayerBasicAttack1State.cs>)
- [PlayerBasicAttack2State.cs](<Assets/Scripts/Player/FSM/States/Basic Attack/PlayerBasicAttack2State.cs>)
- [PlayerBasicAttack3State.cs](<Assets/Scripts/Player/FSM/States/Basic Attack/PlayerBasicAttack3State.cs>)
- [PlayerBasicAttack4State.cs](<Assets/Scripts/Player/FSM/States/Basic Attack/PlayerBasicAttack4State.cs>)
- [PlayerRunAttackState.cs](<Assets/Scripts/Player/FSM/States/Run Attack/PlayerRunAttackState.cs>)

Timeline을 직접 `time = 0`, `Play()` 하던 코드를 `TimelineDirectorContainer.Play(DirectorID)` 호출로 통일했다. 그래야 재생 직전에 해당 공격의 파티클 정책을 PlayableGraph 생성에 반영할 수 있다. State 종료 시 Director를 멈추기 위한 기존 참조는 유지한다.

### 3.4 적 계층

#### [EnemyCore.cs](<Assets/Scripts/Enemy/EnemyCore.cs>)

`IDamageable`과 `IHitStopParticipant`를 구현한다.

- 죽었거나 피해를 받을 수 없으면 `TryTakeDamage()`가 `false`를 반환한다.
- 정상 피해 처리 후 HP 감소와 이벤트 호출을 마치면 `true`를 반환한다.
- 히트스탑 중 FSM의 Update/FixedUpdate/LateUpdate와 Animator 루트 모션 콜백을 막는다.
- 시작 시 누적 루트 모션을 비우고, 이동기와 Animator를 정지한다.
- 종료 시 Animator 속도와 이동 상태를 복구한다.
- `PlayHitReaction()`에서 피격 State를 즉시 재생한 뒤 `Animator.Update(0f)`로 첫 피격 포즈를 평가한다. 그 다음 Animator 속도가 0이 되므로 타격 순간의 읽기 쉬운 포즈가 고정된다.

#### [EnemyMover.cs](<Assets/Scripts/Enemy/EnemyMover.cs>)

플레이어 이동기와 같은 방식으로 Rigidbody 속도 제거, Constraints 저장/고정/복구를 수행한다.

#### [EnemyStateBase.cs](<Assets/Scripts/Enemy/StateMachine/EnemyStateBase.cs>)

적 State의 누적 `AnimDeltaPos`를 제거하는 `ClearAccumulatedMotion()`을 추가했다.

#### [EnemyStateMachine.cs](<Assets/Scripts/Enemy/StateMachine/EnemyStateMachine.cs>)

현재 State에 루트 모션 초기화를 전달하는 메서드를 추가했다.

#### [EnemyFrontHitState.cs](<Assets/Scripts/Enemy/StateMachine/States/EnemyFrontHitState.cs>) / [EnemyBackHitState.cs](<Assets/Scripts/Enemy/StateMachine/States/EnemyBackHitState.cs>)

Animator Trigger만 설정하던 방식 대신 `EnemyCore.PlayHitReaction()`을 호출하도록 변경했다.

- 전방 피격: `Base Layer.Hit.Hit Front`
- 후방 피격: `Base Layer.Hit.Hit Back`

위 문자열로 Animator State 해시를 만들기 때문에 Animator Controller에서 State 이름이나 계층을 바꾸면 코드도 함께 수정해야 한다.

#### [EnemyAnimatorCallback.cs](<Assets/Scripts/Enemy/EnemyAnimatorCallback.cs>)

외부에서 직렬화하던 public Animator 필드를 없애고, `Awake()`에서 얻은 private Animator를 읽기 전용 프로퍼티로 제공하도록 정리했다. 씬에 남아 있던 중복 직렬화 데이터도 제거했다.

### 3.5 Timeline 및 씬 설정

#### [DirectorInfo.cs](<Assets/Scripts/Player/Timeline Director Container/DirectorInfo.cs>)

각 `DirectorID` 항목에 `ParticleMode` 설정을 추가했다. 공격별로 파티클 정지 정책을 다르게 선택할 수 있다.

#### [TimelineDirectorContainer.cs](<Assets/Scripts/Player/Timeline Director Container/TimelineDirectorContainer.cs>)

- 공격 Timeline을 재생하는 단일 진입점을 제공한다.
- 재생 직전에 Timeline의 각 `ControlPlayableAsset.updateParticle` 값을 `ParticleMode`에 맞게 임시 변경한다.
- `PlayableDirector.Play()`로 그래프가 만들어진 뒤 원본 Asset 값을 복구한다.
- 이 방식으로 Timeline Asset 자체를 영구 수정하지 않고 Director 인스턴스별 정책을 적용한다.

#### [CombatPrototypeScene.unity](<Assets/Scenes/CombatPrototypeScene.unity>)

- 다섯 개 `DirectorInfo`에 `ParticleMode: 0`을 명시했다.
- 현재 `0`은 `FreezeWithHitStop`이므로 모든 공격 파티클이 기본적으로 히트스탑과 함께 멈춘다.
- `EnemyAnimatorCallback`에 남아 있던 제거된 Animator 직렬화 필드 데이터를 정리했다.

## 4. 파티클 정책

Inspector의 플레이어 `TimelineDirectorContainer > Director Infos > Particle Mode`에서 공격별로 선택한다.

| 모드 | Timeline | Control Track 파티클 |
|---|---|---|
| `FreezeWithHitStop` | 정지 | Timeline 평가와 함께 정지 |
| `ContinueDuringHitStop` | 정지 | 독립적으로 계속 재생 |

주의할 점:

- 이 선택은 **Timeline Control Track이 제어하는 ParticleSystem**에 적용된다.
- 코드에서 직접 `Instantiate()`한 파티클은 이 정책의 대상이 아니며 현재는 기본적으로 계속 재생된다. 예: Basic Attack 2의 지면 균열 계열 효과.
- `ContinueDuringHitStop` 파티클은 Control Track이 재생 중 GameObject를 활성화한 상태에서 `PlayOnAwake` 등으로 자체 재생되어야 한다.
- 파티클을 새로 추가할 때 Timeline 제어 대상인지 독립 생성 대상인지 먼저 결정해야 한다.

## 5. 주요 조절 지점

### 공격별 히트스탑 길이

각 공격의 `AttackDamageField > Hit Stop Frame`을 조절한다.

- `0`: 히트스탑 요청 안 함
- `1`: 플레이어 1프레임, 적 2프레임
- `2`: 플레이어 2프레임, 적 3프레임

### 디자인 기준 프레임레이트

`HitstopCoordinator.CombatFrameRate`의 값이 `60`이다. 이 값을 바꾸면 모든 `Hit Stop Frame`의 실제 시간이 함께 변하므로 특별한 마이그레이션 목적이 아니라면 유지하는 편이 안전하다.

### 적의 추가 정지 프레임

현재 Coordinator 요청 처리에서 피해자의 프레임 수에 `+ 1`을 적용한다. 나중에 공격별 차이를 원한다면 `AttackDamageField`에 별도의 victim bonus frame을 추가하고 Request 인자로 전달하는 구조가 자연스럽다.

## 6. 새 공격을 추가할 때 체크리스트

1. Timeline Signal에서 `PlayerAttackInstanceContainer.GiveDamageField()`를 사용한다.
2. `AttackDamageField.HitStopFrame`을 설정한다.
3. 공격 State에서 Timeline을 직접 재생하지 말고 `TimelineDirectorContainer.Play(DirectorID)`를 호출한다.
4. 해당 `DirectorInfo.ParticleMode`를 선택한다.
5. 적의 복수 Collider가 같은 `IDamageable` 컴포넌트로 귀결되는지 확인한다.
6. 실제 플레이에서 60fps뿐 아니라 낮은 렌더 프레임에서도 체감을 확인한다.

## 7. 새 피격 대상을 추가할 때 체크리스트

1. 피해 처리 컴포넌트에 `IDamageable.TryTakeDamage()`를 구현한다.
2. 실제 피해가 적용되었을 때만 `true`를 반환한다.
3. 히트스탑이 필요하면 `IHitStopParticipant`도 구현한다.
4. `BeginHitStop()`에서 Animator, 이동, 물리, 자체 타이머 중 멈춰야 할 항목을 정지한다.
5. 모든 기존 값을 저장하고 `EndHitStop()`에서 정확히 복구한다.
6. 비활성화/파괴 경로에서도 정지 상태가 남지 않게 처리한다.

## 8. 현재 구조에서 특히 주의할 점

### 회피 입력과 히트스탑 취소는 아직 구현되지 않음

입력 수집 컴포넌트는 히트스탑 중에도 작동하지만, `PlayerCore.Update()`가 조기 반환하므로 FSM은 입력을 소비하거나 회피 State로 전환하지 않는다. 입력 버퍼가 별도로 보장되지 않는 짧은 회피 입력은 히트스탑 도중 유실될 수 있다.

앞서 논의한 **회피 입력 시 플레이어 히트스탑을 즉시 끝내고 회피로 전환하는 hitstop cancel**은 가능한 설계지만, 현재 코드에는 아직 구현하지 않았다. 현재 동작과 향후 아이디어를 혼동하지 않아야 한다.

구현한다면 다음 경계를 유지하는 것이 안전하다.

```text
히트스탑 중에도 최소 입력/인터럽트 검사만 수행
  -> 회피 입력 확인
  -> HitstopCoordinator에서 플레이어의 hold만 즉시 해제
  -> PlayerCore.EndHitStop()
  -> 회피 State로 전환
  -> 적의 N+1프레임 정지는 그대로 유지
```

즉, 플레이어와 적의 정지 예약을 독립적으로 취소할 수 있어야 하며 전체 요청을 한꺼번에 취소하면 의도한 적의 한 프레임 추가 잔상이 사라진다.

### Rigidbody Constraints 복구 경쟁

히트스탑 시작 시 Constraints를 저장하고 종료 시 복구한다. 다른 시스템이 히트스탑 도중 같은 Rigidbody의 Constraints를 변경한다면 종료 시 이전 값으로 덮어쓸 수 있다. Constraints를 동적으로 바꾸는 기능이 추가되면 물리 잠금 소유권을 통합해야 한다.

### Animator State 이름 의존성

적 피격 포즈는 전체 State 경로 문자열로 해시를 만든다. Animator Controller 리팩터링 시 전/후방 피격 State 상수도 함께 갱신해야 한다.

### 런타임 자동 생성 Coordinator

현재 방식은 별도 씬 설정 없이 동작하지만 Inspector에서 사전 설정하거나 Hierarchy로 의존성을 파악하기 어렵다. 향후 설정값이 많아지면 Bootstrap 씬 또는 명시적 서비스 오브젝트에 배치하고 중복 인스턴스만 방지하는 방식도 고려할 수 있다.

### 실제 렌더 프레임과 디자인 프레임

길이는 60Hz 기준의 고정된 실제 시간으로 계산한다. 렌더링이 크게 끊기는 프레임에서는 2프레임과 3프레임의 시각적 차이가 한 번의 화면 갱신 안에 포함될 수 있다. 로직상 시간 차이는 유지되지만 체감 검증은 실제 목표 기기에서 해야 한다.

## 9. 검증 상태

- 구현 직후 `dotnet build ProjectR.slnx`를 실행해 **경고 0, 오류 0**을 확인했다.
- 정적 빌드 검증과 별개로 Unity Play Mode에서 다음 항목은 직접 체감 확인을 권장한다.
  - 단일/다중 적 타격
  - 앞/뒤 피격 포즈
  - 연속 공격 중 히트스탑 중첩
  - 두 파티클 모드의 차이
  - 낮은 프레임레이트에서의 정지 체감
  - 오브젝트 비활성화 또는 적 사망과 히트스탑이 겹치는 경우

## 10. 변경 범위 참고

현재 Git 변경 목록에 보이는 `Assets/Animations/Player/Controllers/PlayerAnimatorController.controller`는 이번 히트스탑 구현의 핵심 변경 파일로 취급하지 않는다. 사용자가 진행한 다른 변경과 섞여 있을 수 있으므로 히트스탑 작업을 분리하거나 커밋할 때 포함 여부를 별도로 확인해야 한다.
