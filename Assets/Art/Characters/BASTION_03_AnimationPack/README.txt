BASTION 03 — 케이블 수정 및 애니메이션 팩
제작: 2026-11-07 / Unity 6000.5.8f1, URP / Generic Rig / 30 fps

시작 파일
  Prefabs/BASTION_03.prefab
  Animator/BASTION_03.controller
  Source~/BASTION_03_Cabled_Animated.blend

케이블 수정
  양팔 2개: 위팔과 아래팔의 독립 소켓에 연결.
  등 2개: 골반과 등 장갑의 독립 소켓에 연결.
  소켓 8개에 플랜지, 금속 커플링, 잠금 링, 스트레인 릴리프 추가.
  케이블 양 끝은 해당 본에 100% 고정, 중간은 정규화된 2본 가중치로 변형.

애니메이션 (Animations/Clips)
  Idle.anim          4.0초 / 반복 / 미세 체중 이동과 센서 탐색.
  Walk.anim          1.6초 / 반복 / 제자리 중량 보행, 양발 교대 접지.
  BladeSlash.anim    2.4초 / 1회 / 준비, 머리 위 들기, 내려베기, 복귀.
  GatlingFire.anim   3.2초 / 1회 / 자세 고정, 총열 가속, 사격 반동, 감속.
  MissileLaunch.anim 4.0초 / 1회 / 좌우 해치 개방, 발사 반동, 해치 닫힘.

Animator 사용
  기본 상태: Idle.
  Speed(float) > 0.1 이면 Walk, < 0.1 이면 Idle.
  BladeSlash / GatlingFire / MissileLaunch Trigger로 각각 재생.
  공격이 끝나면 Idle로 복귀하며, Speed가 높으면 Walk로 전환.
  Apply Root Motion은 꺼져 있습니다. 게임 코드에서 이동을 적용하세요.
  Walk 원속도에 맞는 전진 속도는 약 0.458333 m/s입니다.
  Generic 전용 리그이며 다른 Humanoid 캐릭터에 직접 리타기팅하는 클립은 아닙니다.

게임 연결 지점
  SOCKET_BladeTip.L / SOCKET_Muzzle.R / SOCKET_Missile.L 본을 유지했습니다.
  Documentation/animation_metadata.json에 발 접지, 타격 구간, 사격과 발사 시점이 있습니다.
  프레임 번호는 Blender 기준 1부터이며 Unity 시간은 (frame-1)/30초입니다.
  타격 구간: 33~39 프레임. 기관총 사격: 32~68 프레임.
  미사일 발사 시점: 48, 60 프레임.
  MissileLaunch는 캐릭터/발사대 동작입니다. 탄체 생성, 총구 화염, 피해 판정과 소리는
  게임 시스템에서 연결할 수 있도록 별도로 두었습니다. 콜백 없는 AnimationEvent는 넣지 않았습니다.
  기존 Blender 탄체 데모는 원본 .blend 안에 보존했으며 기본 표시에서는 숨겼습니다.

파일 구성
  Models: 케이블 수정 모델 FBX.
  Animations/FBX: 본 변환을 매 프레임 베이크한 원본 5개.
  Animations/Clips: 독립 Unity .anim 5개.
  Materials: 기존 어두운 금속/붉은 센서 색을 이식한 URP 머티리얼 10개.
  Source~: 편집 가능한 Blender 파일과 제작 스크립트. Unity 자동 임포트 제외.
  Documentation: 전체 프레임 검사와 케이블/타이밍 자료.
  Validation: Unity 검사 보고서와 실제 스킨 변형으로 만든 앞/뒤/공격 자세 이미지.
  Editor: 이 폴더의 에셋만 다시 생성하는 도구.
  재생성 메뉴: Tools > Project R > BASTION 03 > Build Animation Pack.

검증
  Blender 5개 액션 461프레임: 케이블 끝 연결, 본 유한값, 반복 연결, 발 접지 및 칼끝 지면 간격 통과.
  케이블 연결 최대 오차 약 0.000000726m. 모든 정점 가중치 정규화, 최대 영향 본 2개.
  Unity: 유효 Generic Avatar, 5개 클립의 경로 누락 0, 움직임 검증 통과.
  기존 게임 씬에 배치하거나 기존 캐릭터를 교체하지 않았습니다.
  원본 부품 구조를 유지한 389개 스킨 렌더러입니다. 다수 동시 스폰에 사용할 때에는 별도 메시 통합/LOD 작업을 권장합니다.
