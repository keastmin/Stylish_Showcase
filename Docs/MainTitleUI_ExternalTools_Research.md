# 메인 타이틀 UI 외부 도구·연출 조사

조사일: 2026-11-07. 이 문서는 기획 및 구현 제안과 외부 근거를 구분한다. 실제 프리팹에 적용된 항목과 검증 결과는 프리팹의 사용 문서를 기준으로 확인한다.

## 기획과 현재 프로젝트에 맞춘 판단

`01_게임기획_개요.md`, `02_게임기획_메뉴와진행.md`의 확정 메뉴는 **새 시작 / 스테이지 선택 / 설정 / 종료**다. 새 시작은 첫 스테이지를 시작하며 저장 기록 초기화는 미정이다. 스테이지를 선택하면 해당 스테이지의 일반 적 구간부터 시작한다. `04_게임기획_스테이지와보스.md`의 세 보스는 미확정 콘셉트 예시이므로 확정된 콘텐츠나 해금 조건으로 취급하지 않는다.

현재 `Packages/manifest.json`에는 uGUI 2.5.0, Input System 1.20.0, URP 17.5.0이 있다. `Assets/Download Assets/Heat - Complete Modern UI` 1.1.8이 이미 존재한다. DOTween, Feel, Rive, Spine UI 런타임은 이번 로컬 검색에서 확인하지 못했다. 기존 PlayerHUD는 `Time.unscaledDeltaTime` 기반 애니메이션과 흰 중심광·연청록 `#BAF3FF`·청회색 `#718A9A`·금속 낫 문양을 사용한다.

권장 구성은 **외부에서 제작한 키아트 + 편집 가능한 UGUI 도형 + 실제 TMP 텍스트 + 입력에 반응하는 짧은 코드 애니메이션**이다. 생성 이미지에 메뉴 글자를 합치지 않으면 한글 수정, 포커스 표현, 해상도 대응을 유지할 수 있다. 외부 도구를 쓰는 지점은 고유 키아트와 복잡한 연출의 제작이며, 기본 메뉴 상호작용은 프로젝트의 기존 구성으로 완성한다.

## 외부 도구 비교

| 도구·방식 | Unity로 가져오는 방식 | 적합한 사용처 | 이번 작업 판단 |
| --- | --- | --- | --- |
| 이미지 생성 | 텍스트 없는 배경 PNG, 필요하면 별도 투명 전경 PNG를 Sprite로 임포트 | 폐허, 거대한 보스의 암시, 낫 실루엣, 안개와 금속 질감 | 키아트 제작에 적극 사용. 글자·메뉴·보스 이름은 TMP에 유지 |
| Blender | 기존 모델과 카메라를 이용한 정지 렌더 또는 이미지 시퀀스 | 실제 캐릭터와 동일한 장비·실루엣, 후속 시네마틱 배경 | 실제 모델의 정합성이 필요한 후속 단계에 특히 유용. 공유 Blender 세션은 담당 에이전트와 먼저 조율 |
| After Effects | PNG 시퀀스 등을 내보내 Sprite 시퀀스/영상으로 사용 | 로고의 균열, 짧은 슬래시 전환, 정교한 합성 | 짧은 장식에 한정. 글자·상호작용을 영상에 굽지 않음 |
| DOTween | UGUI/TMP 대상 tween과 Sequence | 페이드, 이동, 눌림, 오버슈트, 단계적 진입 | 이미 도입된 프로젝트에서는 좋은 선택. 현재 네 메뉴만을 위해 새 의존성을 추가할 필요는 낮음 |
| Feel | MMF_Player에 여러 피드백을 묶고 UnityEvent에서 재생 | UI 소리·스케일·빛·진동을 디자이너가 함께 조정 | 연출 라이브러리가 커질 때 유용. 현재는 같은 피드백 묶음 원칙을 코드로 적용 가능 |
| Heat UI | 기존 ButtonManager, 패널 및 사운드 연동 | 빠른 메뉴 확장, 기존 UI 스타일 재사용 | 보유 폰트·사운드·컴포넌트 활용 가능. 새 프리팹이 글로벌 UI Manager 설정을 변경하지 않게 분리 |
| Rive | `.riv` + Unity 패키지 + uGUI Canvas의 Rive Panel | 인터랙티브 벡터 마스코트, 복잡한 상태 기반 메뉴 | 별도 런타임·렌더 타깃이 추가됨. TMP 중심 메뉴 전체를 대체하기보다는 후속 독립 장식에 검토 |
| Lottie | After Effects/디자인 도구 → JSON → Unity용 플레이어 | 사전 제작한 벡터 로고/아이콘 루프 | Unity 연결이 있으나 외부 네이티브 런타임 검증 필요. 단순 버튼 피드백에는 과함 |
| Spine | skeleton/atlas → SkeletonGraphic | 보스 초상화의 호흡, 옷·머리카락 움직임 | 초상화 일러스트 리깅을 제작할 때 검토. UI 전용 재질·아틀라스 조건 확인 필요 |

Blender는 정지 이미지와 이미지 시퀀스 출력을 지원하며, 시퀀스는 일부 프레임을 다시 렌더하기 쉽다. Adobe 역시 스틸 이미지 시퀀스 내보내기를 제공한다. 이 특성 때문에 고비용 연출은 외부에서 제작하고 Unity에서는 제한된 장식으로 재생하는 구성이 가능하다. [Blender 렌더링 문서](https://projects.blender.org/blender/blender-manual/src/branch/main/manual/render/introduction.rst?display=source), [Adobe 이미지 시퀀스 내보내기](https://helpx.adobe.com/after-effects/desktop/render-and-export/render-and-export-images/rendering-exporting-still-images-still.html)

DOTween에는 `DOAnchorPos`, `DOScale`, `DOFade`, punch/shake, Sequence가 있다. 일시정지와 무관하게 움직이려면 최상위 tween/Sequence에 `SetUpdate(true)`를 적용한다. `SetLink`는 Sequence 내부 tween에서는 작동하지 않으므로 컨테이너 수명과 취소를 별도로 설계해야 한다. [DOTween 공식 문서](https://dotween.demigiant.com/documentation.php)

Feel의 `MMF_Player`는 피드백 목록을 재생하고 각 항목의 지연, 쿨다운, 반복을 조절한다. 버튼 UnityEvent에 `PlayFeedbacks`를 연결할 수 있다. Heat UI의 ButtonManager는 클릭·호버·이탈 이벤트, 텍스트·아이콘 및 사운드 옵션을 제공한다. [Feel MMF_Player](https://feel-docs.moremountains.com/mmf-player.html), [Heat UI Button](https://docs.michsky.com/docs/heat-ui/ui-elements/button/)

Rive 공식 Unity 가이드는 `.riv`를 uGUI Canvas에 표시하는 경로를 제공한다. 기본적으로 Panel마다 RenderTexture가 생기므로, 개별 버튼마다 패널을 만들기보다 큰 UI 구역을 묶는 방식을 권장한다. 따라서 UGUI와 TMP가 핵심인 이번 프리팹에는 즉시 필수 의존성으로 넣기보다 별도 장식의 범위를 정한 뒤 도입하는 편이 합리적이다. [Rive 시작 가이드](https://rive.app/docs/game-runtimes/unity/getting-started), [Rive Unity 성능 지침](https://rive.app/docs/game-runtimes/unity/best-practices)

LottieFiles의 Unity 항목은 `gindemit/unity-rlottie`로 연결된다. 이 구현은 JSON 애니메이션을 Texture2D로 렌더하고 `RawImage` 계열에서 표시한다. 따라서 Lottie 파일만 넣으면 Unity 기본 기능으로 실행되는 것은 아니다. 플러그인과 네이티브 라이브러리의 대상 플랫폼 검증을 함께 해야 한다. [LottieFiles 통합 목록](https://lottiefiles.com/integrations), [Unity rlottie 구현](https://github.com/gindemit/unity-rlottie)

Spine의 UI 표시는 `SkeletonGraphic`과 CanvasRenderer 전용 재질을 사용한다. 일반 MeshRenderer용 재질을 그대로 옮기지 않으며, 가능하면 단일 페이지 아틀라스를 사용한다. [Spine 4.2 UI 컴포넌트 문서](https://us.esotericsoftware.com/spine-unity-main-components-4.2)

## 이미지 제작 방향과 인계 규칙

아래는 기획의 확정 세계관이 아니라 현재 HUD에 맞춘 **타이틀용 시각 제안**이다.

- 차콜·은색·연청록 중심. 왼쪽 약 40%는 메뉴를 위한 어두운 여백, 오른쪽은 폐허와 무기 실루엣 또는 압도적인 구조물로 무게를 둔다.
- 광원은 흰 중심과 연청록 가장자리로 제한한다. 큰 형태를 먼저 읽게 하고 메뉴 뒤의 미세 입자·선·강한 명암은 억제한다.
- 이미지는 텍스트·가짜 UI·배지·워터마크 없이 제작한다. 타이틀 로고도 가능하면 TMP 또는 별도 벡터/도형으로 유지한다.
- 배경과 투명 전경을 별도 생성하면 작은 패럴랙스가 가능하다. 분리할 수 없는 키아트는 무리한 깊이 변형 대신 완만한 단일 배경 이동을 적용한다.
- 원본 프롬프트, 생성일, 최종 파일명, 사용 위치를 함께 보존한다. UI 버전 변경 때문에 같은 이미지를 재생성할 필요가 없도록 기능 레이어와 분리한다.

프롬프트 예시:

> Wide cinematic key art for an original third-person stylish action game. Monumental ruined architecture and a restrained scythe-inspired silhouette on the right, misty depth, brushed silver and gunmetal, white luminous core and pale cyan light #BAF3FF, blue-gray shadows #718A9A. Left 40 percent remains dark, quiet, low-detail negative space for a readable game menu. Strong elegant silhouettes, premium atmospheric game art, controlled highlights. No text, letters, numerals, logo, menu, frame, watermark, or readable symbols. Wide landscape composition with generous crop-safe margins.

임포트 권장값은 배경 크기와 대상 장치에 맞춰 결정한다. UGUI용 Sprite, Clamp, 불필요한 Mipmap Off, 투명 전경만 Alpha Is Transparency를 사용한다. 화면 전체 이미지의 압축 여부와 최대 크기는 실제 1080p/4K 화면에서 확인한다. 모든 외부 이미지에 무조건 무압축 4K를 적용하지 않는다.

## 게임 필을 높이는 실제 동작 설계

아래 시간은 특정 라이브러리의 공식 표준이 아니라 이 메뉴에 맞춘 **초기 튜닝값**이다. 입력 결과는 즉시 전달하고 장식 애니메이션 때문에 조작을 오래 기다리게 하지 않는다.

| 상황 | 제안 초기값 | 체감 목적 |
| --- | --- | --- |
| 최초 진입 | 배경 0.45초 페이드, 타이틀 0.6초, 메뉴 행마다 0.055초 지연 | 화면의 무게와 계층을 순서대로 전달 |
| 선택/호버 | 0.10~0.14초 색·선 이동, 텍스트 6~10px 이동 | 어느 항목이 선택되었는지 즉시 전달 |
| 누르는 순간 | 0.05~0.07초에 0.97배 | 눌리는 감각 |
| 놓는 순간 | 0.14~0.18초에 원래 크기, 작은 오버슈트 선택 가능 | 무게를 되돌리는 짧은 반응 |
| 패널 진입 | 0.18~0.25초 알파 + 14~20px 이동 | 메뉴 사이의 관계 전달 |
| 시작 결정 | 즉시 버튼 잠금, 0.25~0.4초 페이드 후 장면 전환 | 중복 요청을 막고 선택을 확정 |
| 배경 대기 | 4~8px 이동 또는 6~10초의 낮은 강도 호흡 | 정지 화면에 깊이 부여 |

색만으로 선택을 구분하지 않고 왼쪽 마커, 밑줄, 위치 변화 중 두 가지 이상을 함께 사용한다. 호버 사운드는 짧은 저음량 클릭, 확정은 더 낮고 단단한 소리로 나누고 빠르게 선택을 바꿀 때 겹치지 않게 제한한다. 게임패드 선택과 마우스 호버를 같은 시각 피드백 경로로 연결한다.

필수 구현 원칙:

1. `Time.unscaledDeltaTime`으로 애니메이션을 갱신해 전투 슬로우/일시정지에서도 메뉴가 반응하도록 한다.
2. 기존 전환을 취소하거나 현재 값에서 이어가도록 한다. 빠른 호버 반복이 coroutine/tween 누적으로 이어지지 않아야 한다.
3. 패널을 닫으면 Raycast와 Selectable을 함께 정리하고 이전 메뉴 선택을 복원한다. 알파 0인 패널이 입력을 막아서는 안 된다.
4. 애니메이션 도중 비활성화·재활성화될 때 위치·스케일·알파를 기준 상태로 복구한다.
5. '동작 줄이기' 옵션은 배경 이동과 오버슈트·큰 위치 이동을 끄고 선택 색/마커와 짧은 페이드를 유지한다. Microsoft의 게임 접근성 지침도 텍스트가 있는 UI의 자동 움직임을 정지할 수 있도록 안내한다. [Xbox 접근성 지침 117](https://learn.microsoft.com/en-us/xbox/accessibility/xbox-accessibility-guidelines/117)

## 성능·입력·한글 구현 근거

Unity는 변화 빈도가 다른 UI를 Canvas별로 나누고, 드물게 변하는 UI에 상시 Animator를 달기보다 코드나 tween을 사용하는 방법을 안내한다. 이번 메뉴에서는 배경 장식과 상호작용 영역을 분리하되 버튼마다 Canvas를 추가하지 않는다. 장식 Image와 TMP의 `raycastTarget`은 끄고 실제 입력 영역만 Raycast 대상이 되도록 한다. [Unity UI 최적화 지침](https://create.unity3d.com/Unity-UI-optimization-tips)

uGUI는 Input System 액션을 EventSystem으로 전달하는 `InputSystemUIInputModule`을 사용한다. 기존 EventSystem이 있는 씬에 프리팹을 넣어도 중복 생성되지 않게 구성하고, 메뉴에 진입했을 때 첫 항목을 선택하며, 모달의 취소 후 원래 선택을 복구해야 한다. [Unity Input System UI 모듈 문서](https://github.com/Unity-Technologies/InputSystem/blob/develop/Packages/com.unity.inputsystem/Documentation~/using-ui-input-module.md)

한글은 실제 한글 글리프가 있는 TMP Font Asset이 필요하다. 현재 HUD의 RobotoCondensed만으로 한글 메뉴를 완성할 수 있다고 가정하지 않는다. 고정 메뉴 문구를 정적 아틀라스에 미리 포함하고, 개발 중 누락 글리프 대응은 한글 소스 폰트를 가진 동적 fallback으로 구성할 수 있다. 동적 폰트는 원본 폰트와 런타임 연산·빌드 용량 비용이 따른다. [Unity uGUI 동적 폰트 문서](https://github.com/Unity-Technologies/uGUI/blob/main/com.unity.ugui/Documentation~/TextMeshPro/FontAssetsDynamicFonts.md)

## 검증 기준

- 저장된 prefab에 Missing Script, 누락 Sprite, 한글 누락 glyph가 없다.
- 1920×1080, 1280×720, 1024×768에서 메뉴·모달이 겹치거나 잘리지 않는다. 와이드 화면은 배경 crop과 오른쪽 포커스를 확인한다.
- 마우스, 키보드 선택/확정/취소가 같은 기능을 수행한다. 게임패드는 연결 가능한 환경에서 확인한다.
- 빠른 연속 호버/클릭, 패널 전환 중 취소, 비활성화 후 재활성화에서 상태가 안정된다.
- 설정을 저장·다시 열어 확인하고, 동작 줄이기 옵션이 배경과 버튼 움직임에 실제 반영된다.
- 연결되지 않은 스테이지는 잘못된 씬을 로드하지 않고 개발 상태를 명확히 알린다. 새 시작이 임의로 저장 기록을 삭제하지 않는다.
- 실제 Unity 화면을 캡처해 한글 가독성, 입력 포커스, 선택 피드백을 확인한다. 외부 이미지 제작에 성공한 것과 Unity 렌더 검증은 별도 결과로 기록한다.
