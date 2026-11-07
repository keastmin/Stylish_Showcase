# Main Title Menu — UGUI + TextMesh Pro

기준 기획: `Docs/01_게임기획_개요.md`, `02_게임기획_메뉴와진행.md`, `04_게임기획_스테이지와보스.md`. 제작일: 2026-11-07.

## 사용

- 프리팹: `Assets/08_Prefabs/MainTitle/MainTitleMenu.prefab`
- 단독 미리보기 씬: `Assets/08_Prefabs/MainTitle/Scenes/MainTitlePreview.unity`
- 빈 타이틀 씬에 프리팹을 하나 배치한다. Canvas / CanvasScaler / GraphicRaycaster가 포함되어 있으며, 실행 시 EventSystem이 없을 때만 Input System UI용 EventSystem을 생성한다.
- `Tools > Project R > Main Title > Rebuild Prefab`은 이 폴더의 프리팹과 미리보기 씬을 다시 생성한다. 수동 수정한 프리팹에 재실행하면 제작 소스의 기본값으로 덮어쓰므로 복제본 또는 Variant에서 사용자 편집을 유지한다.
- 기존 게임 씬과 Build Settings는 변경하지 않았다. 게임 진입점으로 사용하려면 별도의 타이틀 씬에 프리팹을 배치하고 프로젝트의 Build Profile에서 시작 씬으로 지정한다.

## 구현 범위

메인 항목은 **새 시작 / 스테이지 선택 / 설정 / 종료**다. UI 객체와 모든 텍스트는 직렬화된 UGUI/TMP 구성으로 수정할 수 있다. 생성 이미지에 버튼이나 글자가 포함되어 있지 않다.

`MainTitleController.stages`에서 스테이지 ID, 표시 이름, 보스 이름, 설명, 대표 이미지, 씬 경로, 사용 가능 여부를 편집한다. 첫 항목이 새 시작의 진입점이다. 새 시작은 기존 기록을 삭제하지 않는다. 자동 로드는 Build Profile에 등록되어 실제 로드 가능한 씬인지 확인한다.

현재 프로젝트에는 완성된 보스 스테이지 진행 시스템이 없다. 기본 첫 항목은 **전투 테스트 구역 / 프로토타입 전투**로 `Assets/01_Scenes/DemoScene 1.unity`에 연결된다. 나머지 두 항목은 기획 문서의 미확정 콘셉트 미리보기이며 시작이 비활성화된다. 보스 클리어 후 다음 씬 진행·해금·저장 시스템은 이 메뉴의 범위가 아니다.

외부 게임 흐름 관리자와 연결할 때는 `loadScenesAutomatically`를 끄고 `onStageRequested(int)` 이벤트를 구독한다. 배열 순서는 이벤트의 스테이지 인덱스와 일치한다.

## 설정과 입력

- 전체 음량과 음소거는 기존 PauseSettings와 `ProjectR.Audio.MasterVolume`, `ProjectR.Audio.Muted` 키를 공유한다.
- 화면 모드는 전체 화면 / 테두리 없는 창 / 창 모드를 제공한다. Unity Editor의 Game View에서는 실제 전체 화면 전환을 확인할 수 없으므로 플레이어 빌드에서 확인한다.
- 모션 줄이기는 배경 패럴랙스와 확대·이동을 끈다.
- 마우스, 키보드 화살표·Enter·Esc, 게임패드 이동·A·B를 지원한다. 슬라이더는 좌우로 조절한다.
- 모달 입력 차단과 닫은 뒤 포커스 복원, 중복 시작 차단, 실패 시 메뉴 복구를 처리한다.
- 종료는 빌드에서 `Application.Quit()`를 실행한다. Editor에서는 확인 메시지를 표시한다.

## 반영한 연출과 외부 제작

- 내장 `image_gen`으로 제작한 타이틀 키아트: `Art/TitleSanctuary.png` (1672×941). 원본 프롬프트는 `Art/GenerationPrompt.txt`.
- 타이틀·행 진입, 선택 색과 마커, 짧은 눌림, 모달 페이드, 씬 이동 페이드, 8px 안팎의 배경 패럴랙스. 모든 시간은 unscaled time을 사용한다.
- `Audio/Hover.wav`, `Confirm.wav`, `Back.wav`는 코드로 합성한 짧은 PCM 신호음이며 타사 녹음 샘플을 사용하지 않는다.
- 한글: Noto Sans CJK KR Regular + 동적 TMP SDF. 영문: 프로젝트에 이미 포함된 Roboto Condensed TMP. Noto 폰트와 OFL 라이선스를 `Fonts/`에 포함했다. [공식 Noto 배포](https://github.com/notofonts/noto-cjk/blob/main/Sans/README.md), [OFL](https://github.com/notofonts/noto-cjk/blob/main/Sans/LICENSE).
- DOTween / Feel / Rive / Lottie / Spine / Blender / After Effects / Heat 비교와 후속 사용 방법은 `Docs/MainTitleUI_ExternalTools_Research.md` 참조. 이번 프리팹에 새로운 tween·벡터 애니메이션 런타임 패키지는 설치하지 않았다.

배경 Canvas를 UI와 분리하고 장식은 Raycast를 끈다. `CanvasScaler`는 1920×1080 기준 Expand이며 안전 영역을 적용한다. 현재 미리보기 씬은 독립 확인용이므로 기존 씬과 별도로 사용한다.

## 검증 산출물

`MainTitleCapture.CaptureAll()`은 1920×1080, 1280×720, 2560×1080, 1280×960 타이틀 및 각 패널 PNG를 `output/MainTitle/`에 생성한다. `MainTitleVerification.Verify()`는 프리팹 참조·폰트·기획 항목 등을 검사한다. 실제 실행 결과는 `output/MainTitle/Validation.txt`에 기록한다.
