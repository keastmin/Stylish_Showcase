# 스킬 카메라 타임라인

`DemoScene 1`과 `CombatPrototypeScene`에 스킬 카메라 환경을 연결했다. 카메라는 플레이어 하이어라키 밖에 배치되며, 플레이어의 `Skill Director`가 씬 카메라를 참조한다. 스킬 카메라는 플레이어의 이동을 따라간다. 기존 스킬 타임라인의 애니메이션·타격·Impact Frame·Opacity 구간은 유지한다.

## 조절 순서

1. 씬에서 Player의 `Skill Director`를 선택하고 `Window > Sequencing > Timeline`을 연다.
2. `Skill Camera Signals`에서 **Skill Camera Position** 마커를 이동하여 위치 설정 시점을 조절한다. 반드시 카메라 활성 클립보다 앞에 둔다.
3. `Skill Camera`의 **Skill Camera Active** 클립을 이동하거나 늘려 관찰 카메라의 활성 구간을 조절한다.
4. `Skill Camera Signals`에서 **TPS Camera Recenter** 마커를 이동하여 복귀 전 리센터 시점을 조절한다.
5. `Skill Camera Noise`의 **Weak Noise + Final Impact** 클립을 이동하거나 늘려 노이즈 구간을 조절한다.
6. 노이즈 클립을 선택하고 Inspector에서 세기·주파수·마무리 충격을 조절한다.

카메라 활성 클립의 끝은 **TPS 복귀 블렌드 시작 시점**이다. 실제 복귀 완료는 여기에 복귀 블렌드 시간을 더한 시점이다. 노이즈와 마지막 충격은 카메라가 꺼지기 전에 끝나도록 배치한다.

설정된 기본 구간은 기존 카메라 Control 클립을 이어받았다.

| 요소 | 기본값 |
|---|---|
| 위치 설정 시그널 | 5프레임 / 0.08333초 |
| 카메라 활성 | 7~104프레임 / 0.11667~1.73333초 |
| 노이즈 | 0.31667~1.53333초 |
| TPS 리센터 시그널 | 1.53333초 |
| 진입·복귀 블렌드 | 각각 0.2초 |

프레임 표기는 60fps Timeline 기준이다. 마커나 클립을 이동하면 이 기본값 대신 편집한 시점이 사용된다.

## 배치와 복귀

`Skill Director`의 `PlayerSkillCameraDirector` Inspector에서 조절한다.

| 설정 | 의미 |
|---|---|
| Skill Cinemachine | 플레이어 바깥의 스킬 카메라 |
| Player TPS Orbit Follow | TPS 카메라의 CinemachineOrbitalFollow |
| Main Camera Brain | 실제 렌더 카메라의 CinemachineBrain |
| Player / Facing Transform | 플레이어 위치와 모델의 방향 |
| Offset Position | 돌진 방향 기준 좌우·높이·앞뒤 거리. 기본 `(0, 5, -8)` |
| X Rot Offset | 기본 관찰 각도. 기본 16도 |
| Preview Skill Origin | 편집 모드에서 사용할 고정 시작 위치와 방향 |
| Recenter Mode | Instant / Smooth. 기본 Instant |
| Recenter Duration | Smooth 리센터 소요 시간. 기본 0.15초 |

실제 스킬 진입 시 모델을 돌진 방향으로 회전한 뒤 시작 위치와 방향을 저장한다. 위치 설정 마커는 그 저장된 시작 포즈로 카메라를 배치한다. 이후 플레이어가 이동한 만큼 카메라도 함께 이동한다. 돌진 시작 방향을 기준으로 한 거리·높이·카메라 각도는 유지하므로, 도중에 모델 방향이 바뀌어도 관찰 방향이 회전하지 않는다. 스킬 중단 시 이동 추적을 종료한다.

리센터 시그널이 실행되면 `Facing Transform`에 연결된 모델의 현재 정면 방향을 수평 축 목표로 저장한다. TPS 카메라는 그 방향의 뒤쪽으로 복귀하며, 수직 축은 해당 축의 Center로 되돌린다. 스킬 시작 때 저장한 돌진 방향과 별개로 리센터 시점의 방향을 사용한다. Smooth 모드도 시그널에서 저장한 방향을 목표로 보간한다. 자동 리센터 Enabled 설정은 유지한다.

이 수평 각도 계산은 현재 TPS의 `OrbitalFollow > Binding Mode = World Space` 설정을 기준으로 한다. TPS Follow / Look At은 위치 추적용 `Camera Anchor`에 계속 연결하고, `Facing Transform`에는 실제로 회전하는 플레이어 `Model`을 연결한다. 루트와 Camera Anchor의 회전이 고정되어 있어도 Model 방향으로 복귀한다. 편집 모드 미리보기에서는 현재 평가된 Model 방향을 사용하므로 모델 방향을 바꾸거나 애니메이션을 스크럽하면 리센터 결과도 다시 계산한다.

Smooth 모드에서는 리센터 마커부터 카메라 활성 클립 끝까지 Recenter Duration 이상의 시간을 확보한다. 카메라 조작 입력은 관찰 중과 복귀 블렌드 중 잠시 비활성화하고, 복귀 완료 또는 스킬 중단 시 원래 활성 상태로 복구한다. 플레이어의 이동 입력은 건드리지 않는다.

진입·복귀 블렌드는 Main Camera Brain의 `Custom Blends`에서 `Player Freelook Cinemachine ↔ Skill Camera` 항목을 조절한다.

## 노이즈 Inspector

| 설정 | 의미 |
|---|---|
| Waveform | Sine, Perlin, Profile |
| Profile | 재사용할 Cinemachine NoiseSettings. Profile 모드에서 위치 Z 신호를 사용 |
| Amplitude | Sine/Perlin에서는 위치 진동 세기. Profile에서는 프로필 진동의 배율 |
| Frequency | Sine/Perlin에서는 진동 속도. Profile에서는 프로필 주파수의 배율 |
| Direction | 카메라 공간의 진동 방향. `(0,0,1)`은 앞뒤 |
| Seed | Perlin/Profile의 재현 가능한 샘플 오프셋 |
| Fade In / Out | 노이즈 시작·종료 감쇠 시간 |
| Finisher Enabled | 마지막 한 번의 강한 충격 사용 |
| Finisher Position | 클립 진행률 기준 마무리 충격 시작. 기본 0.85 |
| Finisher Amplitude | 마무리 충격의 위치 진동 세기 |
| Finisher Duration | 충격 지속 시간. 클립 끝을 넘지 않도록 제한 |
| Finisher Curve | 밀림·반동·원위치 복귀 모양 |

기존 `Skill Camera Noise Profile`을 기본 클립에 연결했다. 프로필의 Z 진폭이 3.3이므로 초기 Amplitude를 0.01, Frequency를 1로 지정했다. 마지막 충격 기본 세기는 0.18, 지속 시간은 0.12초다.

스킬 카메라의 기존 Basic Multi Channel Perlin과 Impulse Listener는 중복 진동을 피하기 위해 비활성화한다. 프로필 에셋과 설정은 보관하며, 타격·데미지 로직은 유지한다. 타임라인에서 만든 노이즈는 Timeline Camera Noise Extension이 출력 카메라 상태에 보정값으로 더한다.

## 미리보기

Timeline Preview를 켜고 재생 헤드를 이동한다. Game 뷰에서 카메라 전환과 진동을 확인할 수 있다.

플레이어 바깥의 `Preview Skill Origin`을 이동·회전하여 미리보기의 시작 지점과 돌진 방향을 정한다. 미리보기 중 Player 루트가 이동하면 시작 지점 기준 이동량을 카메라에도 반영한다. 실제 플레이에서는 이 오브젝트 대신 스킬 진입 시 저장된 플레이어 포즈를 사용한다.

`Skill Camera Signals`는 표준 SignalEmitter와 SignalReceiver를 사용하면서, 현재 시간 이전의 카메라 시그널 결과를 함께 평가하는 전용 트랙이다. 일반 Signal Track으로 교체하면 중간 시점으로 건너뛰거나 역방향으로 이동하는 카메라 미리보기를 보장할 수 없다.

노이즈는 실제 시간이나 deltaTime을 누적하지 않고 클립 내부 시간으로 계산한다. 같은 시점은 같은 노이즈를 보여주고, 타임라인 히트스톱·일시정지는 그 위상을 유지한다. 마지막 충격은 Impulse 이벤트를 반복 발생시키지 않는다.

미리보기 종료·Director 중단·비활성화·스크립트 재컴파일 시 임시 상태를 복원한다. 저장 중에는 원래 위치·축 값·카메라 활성 상태를 저장하고, 저장이 끝나면 미리보기를 재개한다.

## 다른 씬에서 연결

1. `Assets/08_Prefabs/Combat/PlayerSkillCameraRig.prefab`을 씬에 배치한다. 플레이어의 자식으로 두지 않는다.
2. 플레이어 인스턴스의 Skill Director에서 Scene Cameras와 Preview Skill Origin을 연결한다. 플레이어 프리팹에는 외부 씬 카메라 참조를 저장하지 않는다.
3. `GameObjectActivationTarget.Target`에 Skill Camera GameObject를 연결한다.
4. `CinemachineCameraNoiseTarget.Extension`에 Skill Camera의 Timeline Camera Noise Extension을 연결한다.
5. Timeline의 Skill Camera / Skill Camera Noise / Skill Camera Signals 바인딩을 각각 활성화 어댑터 / 노이즈 어댑터 / SignalReceiver로 지정한다.
6. SignalReceiver의 Skill Camera Position → PositionSkillCamera, TPS Camera Recenter → PlayerTPSCameraRecenter 연결을 확인한다.

어댑터는 비활성화될 스킬 카메라 자체가 아닌, 항상 활성 상태인 Skill Director에 둔다. 기존 Camera 1 Control Track이나 카메라 ON/OFF 시그널을 중복 연결하지 않는다.

`Tools > Combat > Set Up Skill Camera Environment`는 두 기본 씬과 플레이어 프리팹 연결을 설정하고 해당 씬을 저장한다. 기존 새 카메라 클립과 시그널 시점은 유지한다.

검증 메뉴: `Tools > Timeline > Validate Skill Camera`, `Validate Skill Camera in Play Mode`. 후자는 격리된 런타임 카메라 검증 후 Edit 모드로 복귀한다.
