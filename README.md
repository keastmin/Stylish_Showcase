# Project R

3인칭 스타일리시 액션의 전투 감각을 구현한 개인 프로젝트입니다. 4연속 기본 공격, 완벽 회피와 반격, 다수의 적이 플레이어를 둘러싸고 공격 기회를 나누는 전투를 하나의 프로토타입으로 구성했습니다.

`Unity 6000.5.8f1` · `C#` · `URP` · `Input System` · `Cinemachine` · `Timeline`

- 개발 기간: 2026.08 ~ 개발 중
- 담당: 전투 시스템 설계, 게임플레이 프로그래밍, 카메라·VFX·사운드 연동

## 구현 내용

### 플레이어 전투

- 기본 공격 4단 콤보와 달리기 공격을 각각의 상태로 분리했습니다.
- 이동, 회피, 피격, 스킬을 명시적인 FSM으로 관리해 상태 전환 조건과 무적 구간을 한곳에서 추적할 수 있게 했습니다.
- 공격 애니메이션과 판정, 이펙트는 Timeline Signal로 동기화했습니다.
- 카메라 방향을 기준으로 이동하며, 큰 방향 전환과 빠른 달리기는 별도 상태로 처리합니다.

### 타격감

- `Time.timeScale`을 변경하지 않고 공격자와 실제 피격 대상만 멈추는 히트스톱을 구현했습니다.
- 정지 시간은 60Hz 기준 프레임으로 설정하며, 피격자는 공격자보다 한 프레임 더 길게 멈춥니다.
- 같은 공격에 여러 콜라이더가 잡혀도 대상별 피해와 히트스톱이 한 번만 적용됩니다.
- Timeline 파티클은 공격별로 히트스톱 동기화 여부를 선택할 수 있습니다.

### 완벽 회피

- 적의 공격 범위 안에서 회피하면 가장 가까운 유효 대상을 기준으로 완벽 회피를 판정합니다.
- 완벽 회피 중에는 전투 객체만 감속하고 UI, 입력, 카메라는 정상 속도를 유지합니다.
- 무적 구간과 반격 입력 구간을 분리했으며, 반격 시 저장한 적을 향해 전용 공격 상태로 전환합니다.
- 감속 진입과 복귀는 커브로 조절할 수 있고 히트스톱과 겹쳐도 각 시스템의 요청이 독립적으로 유지됩니다.

### 다수 적 전투

- 한 번에 한 적만 공격권을 갖도록 공격 주기를 조율합니다.
- 적마다 선호 거리와 선회 방향을 두고, 이웃 회피와 개인 공간을 반영해 플레이어 주변에서 자연스럽게 움직이도록 했습니다.
- 공격 경로가 다른 적에게 막히면 진입로를 요청하고 앞의 적이 잠시 비켜서도록 처리했습니다.
- 카메라 밖의 적은 화면 가장자리 마커로 위치와 공격 경고를 표시합니다.

## 구조

| 영역 | 역할 | 주요 코드 |
| --- | --- | --- |
| Player FSM | 이동, 콤보, 회피, 피격, 스킬 상태 전환 | [PlayerStateMachine.cs](<Assets/02_Scripts/Player/FSM/PlayerStateMachine.cs>) |
| Enemy FSM | 대기, 교전, 공격 예고, 공격, 피격, 사망 상태 전환 | [EnemyStateMachine.cs](<Assets/02_Scripts/Enemy/StateMachine/EnemyStateMachine.cs>) |
| Hitstop | 공격자와 피격 대상의 정지 요청 및 해제 시점 관리 | [HitstopCoordinator.cs](<Assets/02_Scripts/Combat/HitstopCoordinator.cs>) |
| Combat Time | 완벽 회피 감속과 전투 전용 시간 배율 관리 | [CombatTimeController.cs](<Assets/02_Scripts/Combat/CombatTimeController.cs>) |
| Enemy Coordination | 공격 순서, 전투 거리, 충돌 회피, 공격 진입로 조율 | [EnemyAttackTimingController.cs](<Assets/02_Scripts/Combat/EnemyAttackTimingController.cs>), [EnemyPositioningController.cs](<Assets/02_Scripts/Combat/EnemyPositioningController.cs>) |
| Timeline Combat | 공격 판정, VFX, 스킬 연출과 커스텀 트랙 연결 | [TimelineDirectorContainer.cs](<Assets/02_Scripts/Player/Timeline Director Container/TimelineDirectorContainer.cs>) |

## 조작

현재 데모 씬에 연결된 키보드·마우스 조작입니다.

| 입력 | 동작 |
| --- | --- |
| `WASD` | 이동 |
| 마우스 왼쪽 버튼 | 기본 공격 / 완벽 회피 반격 |
| `Left Shift` | 회피 |
| `E` | 스킬 |

## 실행 방법

1. Unity Hub에서 이 저장소를 `Unity 6000.5.8f1`로 엽니다.
2. [DemoScene 1.unity](<Assets/01_Scenes/DemoScene 1.unity>)를 엽니다.
3. Play Mode로 실행합니다.

사용 중인 주요 패키지 버전은 [Packages/manifest.json](Packages/manifest.json)에서 확인할 수 있습니다.

## 기술 문서

- [히트스톱 시스템](HITSTOP_SYSTEM.md): 프레임 기반 정지 방식과 참가 객체별 복구 처리
- [완벽 회피 슬로모션](Docs/PerfectDodge.md): 전투 시간 배율, 무적 및 반격 구간
- [스킬 돌진 페이드](Docs/PlayerSkillFade.md): Timeline 커스텀 트랙과 머티리얼 복구

## 프로젝트 구성

```text
Assets/
├─ 01_Scenes/       실행 씬
├─ 02_Scripts/      전투, 플레이어, 적, UI 코드
├─ 03_Animations/   애니메이션과 컨트롤러
├─ 04_Timelines/    공격 및 스킬 시퀀스
├─ 06_VFX/          전투 이펙트
├─ 08_Prefabs/      플레이어, 적, UI 프리팹
├─ 09_Shaders/      회피 잔상, 왜곡, 공격 경고 셰이더
└─ 12_SFX/          전투 사운드
```

## 라이선스 및 에셋

직접 작성한 소스 코드는 [MIT License](LICENSE)를 따릅니다. `Assets/Download Assets`와 일부 아트·사운드 리소스에는 외부 제작자의 에셋이 포함되어 있으며, 해당 리소스의 권리는 각 제작자와 원 라이선스에 있습니다.
