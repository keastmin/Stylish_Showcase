# 스킬 타격 설정

`DemoScene 1`의 **Skill Director**에 `Player Skill Attack Receiver`가 연결되어 있습니다.
`SkillTimeline`의 기존 빈 Signal Emitter 16개는 **Skill Hits** 트랙의 `Player Skill Hit Marker`로 바뀌었습니다.
카메라·VFX 등 이미 Signal이 지정된 Emitter는 그대로 사용합니다. 타격마다 Signal 에셋이나 UnityEvent를 만들 필요가 없습니다.

## 타이밍과 필드 선택

마커를 시간순으로 정렬하면 1~12번째는 `Skill Hitbox/Hit1~Hit12`, 13번째 이후는 `Hit12`를 사용합니다.
현재 43~58프레임의 타격 시점은 유지되어 있습니다. 54~58프레임의 다섯 타격은 모두 `Hit12`입니다.

- 마커를 좌우로 옮기면 타격 시간과 자동 연결 순서가 함께 바뀝니다.
- `Damage Field Number = 0`: 시간순 자동 연결입니다.
- `Damage Field Number = 1~12`: 순서와 관계없이 지정한 필드를 사용합니다.
- 타격 추가: **Skill Hits** 트랙에 `Player Skill Hit Marker`를 추가하거나 기존 마커를 복제합니다.

## 피해량과 공격 단계

**Skill Director > Player Skill Attack Receiver > Hit1~Hit12**를 펼쳐 설정합니다.
씬의 Skill Director를 선택한 상태에서 타임라인 마커를 선택하면 마커 Inspector에서도 같은 설정을 편집할 수 있습니다.
같은 Hit 번호를 사용하는 마커들은 설정을 공유합니다.

| 설정 | 의미 |
|---|---|
| Hitbox | 타격 순간에 검사할 Box/Sphere/Capsule Collider |
| Damage | 적에게 주는 피해량 |
| Stagger Level | 공격 단계(None, Level1~5). 적의 피격 저항 조건을 만족하면 피격 모션 적용 |
| Hit Stop Frame | 60Hz 기준 정지 프레임. `0`이면 히트스톱 없음 |
| Hit Stop Mode | `Attacker And Victims`: 플레이어와 적 정지. `Victims Only`: 적만 정지 |
| Target Layers | 공격 대상 레이어. 현재 `Enemy Hurtbox` |
| Trigger Interaction | 트리거 콜라이더를 공격 대상으로 포함할지 설정 |
| Skill Gauge Additive | 명중한 적 한 명당 얻는 스킬 게이지 |

초기값은 **피해 10, Level2, 히트스톱 2프레임, Attacker And Victims, 스킬 게이지 증가 0**입니다.
기존 전투 규칙에 따라 적은 설정값보다 1프레임 더 정지합니다(기본 플레이어 2 / 적 3프레임).
`Victims Only`는 적만 정지시키는 연출 선택입니다. `Attacker And Victims`에서도 돌진 총 이동량은 유지됩니다.

플레이어도 정지시키는 경우 스킬은 아직 물리에 적용하지 않은 루트 모션과 이동 명령을 보관합니다.
히트스톱이 풀리면 이 이동량을 이어서 적용하므로, 연타 때문에 타임라인에 정의한 돌진 거리가 줄어들지 않습니다.
스킬을 취소하거나 종료하면 보관한 이동량을 지웁니다.

히트스톱 요청은 타격 프레임의 애니메이션과 `OnAnimatorMove` 계산이 끝난 `LateUpdate`에서 적용됩니다.
플레이어 이동기는 FSM 뒤에 실행되어 같은 물리 틱의 이동 명령을 소비합니다.
플레이어와 적은 정지·재개할 때 Rigidbody의 실제 위치를 보존하고, 정지 중 보간을 꺼서
화면 보간 위치가 물리 위치를 덮어쓰며 이미 이동한 거리를 되돌리는 것을 방지합니다.

정지 길이는 `N / 60`초입니다(2프레임 = 약 0.0333초, 12프레임 = 0.2초, 30프레임 = 0.5초).
`Time.unscaledTimeAsDouble`로 시작·해제를 계산하므로 렌더 FPS와 슬로모션 배율에 따라 디자인 프레임 길이가 바뀌지 않습니다.
실제 해제는 종료 시각을 지난 첫 Update에서 이루어지므로 화면 갱신 간격 이내의 오차는 발생할 수 있습니다.
기존 적의 `N + 1`프레임 규칙과 중첩 요청 중 가장 늦은 해제 시각을 유지하는 규칙은 동일합니다.

Timeline 파티클은 Timeline 시간에 따라 멈추고, 일반 파티클·VFX Graph·Tiny Trail은 소유 캐릭터의 히트스톱을 따릅니다.
Timeline의 `ContinueDuringHitStop` 설정은 계속 재생하는 연출로 유지됩니다.
스킬 잔상은 생성 간격·페이드·수명 모두 전투 시간을 사용해 히트스톱 중 먼저 소멸하지 않습니다.

**Tools > Combat > Validate Skill Motion With Hit Stop**을 편집 모드에서 실행하면 현재 씬의 실제 플레이어와
스킬 타임라인으로 목표 FPS 30/60/120 × 히트스톱 0/2/12/30프레임, 총 12개 조합을 검증합니다.
실제 Rigidbody 이동 거리, 현재 타임라인에 연결된 타격 수, 정지 중 루트 모션/VFX 속도, 60Hz 환산 정지 시간을 확인하고
`Temp/SkillMotionVerification.result`에 기록한 뒤 플레이 모드를 종료합니다. 씬에는 테스트 설정을 저장하지 않습니다.

## 타격 처리

마커마다 독립적으로 범위를 검사합니다. 한 적에게 콜라이더가 여러 개 있어도 같은 마커에서는 한 번만 피해를 주고,
다음 마커에서는 같은 적을 다시 타격할 수 있습니다. 따라서 `Hit12`를 재사용하는 후속 마커도 각각 공격합니다.
공격 필드 콜라이더는 물리 충돌을 켜지 않고 쿼리용 형상을 사용합니다.

기존 `PlayerAttackInstanceContainer`를 통해 실제 체력 감소·공격 단계·스킬 게이지와 히트스톱을 처리합니다.
빗나가거나 피해가 거부되면 히트스톱도 발생하지 않습니다. 중단된 스킬과 에디터 미리보기/스크럽은 피해를 주지 않습니다.

**Tools > Combat > Validate Skill Hit Timeline**은 에셋 연결을 확인하고 플레이 모드에서 독립된 테스트 액터로
16회 타격, Hit12 반복, 중복 콜라이더, 공격 단계 전달, 두 히트스톱 모드, 재생 재시작과 피해 거부를 검증합니다.
검증이 직접 플레이 모드에 진입했다면 완료 후 편집 모드로 돌아옵니다.
