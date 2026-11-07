# Timeline 구간 활성화와 Impact Frame

## 플레이어 스킬에서 사용하기

Player 프리팹의 **Skill Director**에 `PlayerSkillImpactFrame`을 연결하고,
`SkillTimeline`의 **Impact Frame** 트랙으로 Easy Impact Frames 파티클을 제어합니다.
`DemoScene 1`과 `CombatPrototypeScene`의 플레이어는 프리팹의 연결을 상속합니다.

1. Hierarchy에서 플레이어의 **Skill Director**를 선택합니다.
2. **Window > Sequencing > Timeline**에서 `SkillTimeline`을 엽니다.
3. **Impact Frame** 트랙의 클립을 원하는 타격 시점으로 이동합니다.
4. 클립 양끝을 드래그하거나 클립 Inspector의 **Start / Duration**으로 활성 구간을 정합니다.
5. 여러 번 점멸하려면 클립을 복제해 각각 배치하고, 꺼지는 시간이 필요하면 클립 사이에 빈 구간을 둡니다.

현재 E 연출은 **돌진 전 준비 자세**를 강조합니다. 임팩트 클립은 60fps 기준
**32프레임부터 50프레임 직전까지**, Start `0.5333333`초, Duration `0.3`초입니다.
원본 파티클 수명도 `0.3`초이며, 클립의 정규화된 시간으로 원본 전체를 샘플링합니다.
클립을 늘리면 효과 전체가 그 길이에 맞춰 느려지므로, 준비 동작을 더 유지할 때는
`Skill - Impact Windup` 애니메이션과 이후 돌진·타격·이펙트 구간도 함께 맞춰야 합니다.
새 공용 클립의 기본 길이는 해당 타임라인의 프레임 레이트 기준 2프레임입니다.

클립 구간에서 ON, 구간 밖에서는 OFF입니다. 종료를 위한 별도 Signal이나 OFF 노드는 필요하지 않습니다.
서로 닿거나 겹친 클립은 연속으로 켜지며, 한 클립이 끝나도 다른 활성 클립이 있으면 유지됩니다.
`PC_Renderer`의 Impact Frame 체크박스는 평소 OFF로 두세요.

### 임팩트 구간의 후처리와 실루엣

- `Player > Skill Impact Frame`의 Global Volume은 파티클과 같은 구간에 활성화됩니다.
- Profile은 `Assets/Settings/Skill Impact Frame Volume.asset`입니다. 우선순위 2000으로
  **Bloom Intensity와 Motion Blur Intensity만 0**으로 덮어씁니다. 구간 종료·중단 시
  Volume이 꺼지면서 현재 씬의 설정으로 돌아갑니다. Vignette 등 다른 설정은 유지합니다.
- E 전용 `Assets/09_Shaders/Impact Frame/Skill Impact Frame.mat`은 화면 왜곡을
  `0.00025`로 줄여 준비 자세가 방사형 선으로 무너지는 현상을 억제합니다.
- `PlayerSkillImpactFrame > Emphasize Player Silhouette`는 카메라 렌더 중에만
  플레이어 재질의 밝기 입력을 분리합니다. 몸과 무기가 배경과 반대 명암으로 읽히며,
  렌더 종료 즉시 원래 재질로 복귀합니다. 마지막 페이드 구간에는 원래 색을 보여줍니다.
  `Player Impact Silhouette.mat` 참조는 빌드에서도 필요한 Emission 변형을 포함합니다.
- 씬 Global Volume Profile과 다운로드한 파티클·머티리얼 원본은 수정하지 않습니다.

## 임팩트 프레임 효과음

`SkillTimeline`의 **Skill Impact Frame SFX** 트랙에 `Skill Impact Frame SFX Signal`을 추가했습니다.
이 시그널은 플레이어의 `PlayerSFXController.PlayOneShotSkillImpactFrameSFX()`를 호출합니다.
Player 프리팹과 두 전투 씬에 Signal Receiver와 트랙 Binding을 연결했습니다.

- 소리 재생 시점은 시그널 마커를 드래그하거나 Inspector의 **Time**을 수정해 조절합니다.
- 초기 위치는 현재 Impact Frame 클립 시작점인 **32프레임 / 0.5333333초**입니다.
- 화면 효과 클립과 효과음 마커는 각각 이동할 수 있습니다. 화면 효과를 옮긴 뒤 소리도 맞추려면 마커를 함께 옮기세요.
- 여러 번 재생하려면 마커를 복제합니다. **Emit Once**와 **Retroactive**는 꺼져 있습니다.
- 소리 파일은 **Player Audio > Player SFX Controller > Skill Impact Frame SFX**에서 변경합니다. 기본 파일은 `Impact_sfx_2.wav`입니다.
- 타임라인을 스크럽하는 동안에는 효과음이 재생되지 않습니다. 실제 스킬 재생으로 확인하세요.

## 다른 타임라인에서 새로 연결하기

1. 씬 오브젝트에 **Add Component > Timeline > Renderer Feature Activation Target**을 추가합니다.
2. 컴포넌트의 **Renderer Data**에 대상 Renderer 에셋을 지정합니다.
3. **Renderer Feature** 드롭다운에서 제어할 Feature를 선택합니다. Impact Frame은 `PC_Renderer > Impact Frame`입니다.
4. Timeline에 `TimelineActivationTrack`을 추가하고 트랙의 Binding 칸에 이 컴포넌트를 연결합니다.
5. 트랙에 `TimelineActivationClip`을 추가하고 위치와 길이를 편집합니다. 트랙/클립 이름은 원하는 이름으로 변경할 수 있습니다.

스킬 연결을 다시 구성할 때는 **Tools > Combat > Set Up Skill Impact Frame**을 사용할 수 있습니다.
이미 있는 Impact Frame 트랙과 클립은 유지하고 프리팹 연결을 구성합니다.

## 시간과 복원 규칙

- 앞뒤로 스크럽하면 현재 Timeline 위치에 맞춰 ON/OFF를 다시 계산합니다.
- 일시정지와 히트스톱은 현재 상태를 유지합니다. 클립 길이는 실제 시계 시간이 아닌 Timeline 시간입니다.
- 스킬 취소, Director 정지/비활성화, 오브젝트 비활성화/파괴, 그래프 재생성, 미리보기 종료 시 제어 전 상태로 복원합니다.
- 여러 트랙이나 대상 컴포넌트가 같은 Feature를 연결하면 활성 요청을 합산합니다. 마지막 소유자가 해제될 때 최초 상태로 복원합니다.
- 미리보기 중 에디터 저장은 원래 활성 상태를 저장하고, 저장 후 미리보기 상태를 다시 적용합니다. 스크립트 리로드와 플레이 모드 전환도 정리합니다.

효과는 해당 Renderer를 사용하는 카메라들에 적용됩니다.
타임라인 1프레임보다 화면 갱신 간격이 길면 아주 짧은 클립을 건너뛸 수 있습니다.
낮은 FPS에서도 보여야 하는 구간은 길이를 충분히 확보하고 실제 재생으로 확인하세요.

## 코드 구조와 확장

- `Timeline/Common/Activation`: Track, Clip, Mixer, Target 기반 클래스, 요청 Registry와 Director 수명 감시.
- `Timeline/URP`: `RendererFeatureActivationTarget`과 Feature 선택 Inspector.
- `Timeline/Editor`: 에디터 리로드/저장/플레이 모드 전환 정리.
- `Combat/Editor`: 이 프로젝트의 스킬 연결과 검증용 도구.

공용 실행 코드는 Player, Impact Frame, URP 타입에 의존하지 않습니다.
다른 종류의 활성화 대상은 `TimelineActivationTarget`을 상속해 실제 `Resource`와
`ReadActive(resource)` / `WriteActive(resource, active)`를 구현합니다.
Resource는 동일한 실제 대상에 대해 같은 Unity Object를 반환해야 합니다.
복원할 때도 최초 Resource가 전달되므로, 변경된 Binding을 통해 이전 대상의 상태를 잘못 쓰지 않습니다.

공용 활성화 트랙은 대상의 ON/OFF와 복원을 담당합니다. E 전용 파티클 샘플링과
실루엣 제어는 `PlayerSkillImpactFrame`에, 후처리 값은 별도의 Volume Profile에 있습니다.

## 검증

편집 모드에서 **Tools > Timeline > Validate Activation Timeline**을 실행합니다.
독립된 미리보기 씬에서 구간 경계, 역방향 스크럽, 중첩, 여러 트랙/어댑터, 일시정지/중단,
재사용, 히트스톱, 비활성화, 원래 ON/OFF 복원, 연결 변경, 그래프 재생성, 트랙 Mute,
저장/리로드 복원과 30/60/120fps 시간 샘플을 확인합니다.
두 프로젝트 씬의 프리팹 Binding과 실제 Feature 제어도 확인하며 Renderer/머티리얼 파일 내용은 보존합니다.
결과는 `Temp/TimelineActivationVerification.result`에 기록합니다.
