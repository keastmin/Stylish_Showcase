CHROMATIC AFTERIMAGE / ProjectR
================================

독립적으로 저장된 플레이어 잔상 + 푸른 GPU 입자 VFX입니다.
Dodge Studio의 ‘잔상 VFX’ 탭에서 회피에 선택 적용할 수 있습니다.
현재 PlayerDodge.asset은 완벽 회피 성공 시 이 VFX를 사용합니다.
E 스킬의 기존 효과와 원래 메시 잔상 셰이더는 유지합니다.

보기
----
Preview/ChromaticAfterimage_Preview.unity 를 열고 Play를 누르면 자동 반복됩니다.
Tools > ProjectR VFX > Chromatic Afterimage Preview 에서 Play 모드 재생/일시정지,
회피/E 스킬 포즈 전환, 시간 이동, 잔상 수명/투명도/입자량을 조절할 수 있습니다.
Inspect strongest trail은 이동 직후의 잔상과 입자가 보이는 0.64초로 이동합니다.
시간 이동은 실제 Play 모드에서 GPU가 포즈를 순서대로 처리한 후 목표 시점에 일시정지합니다.

저장 파일
---------
Prefabs/ChromaticAfterimage_VFX.prefab : 모델에 Bind하여 재사용하는 독립 VFX
Graphs/ChromaticWake.vfx             : 편집 가능한 실제 VFX Graph (GPU 시스템 2개)
Materials/ChromaticAfterimage.mat    : 원래 색을 유지하는 디더/노이즈 잔상 머티리얼
Shaders/ChromaticAfterimage.shader   : URP용 컬러 보존 잔상 셰이더
Textures/WakeSpeck.png              : 가장자리가 투명한 작은 조각과 은은한 중심광 마스크
Textures/WakeGlintSoft.png          : 짧고 부드러운 반짝임 마스크
Textures/WakeFlake.png, WakeGlint.png : 기존 마스크 보관본 (현재 그래프에서는 미사용)
Textures/WakeWisp.png, WakeMote.png   : 폐기된 긴 잔광 실험 마스크 (현재 그래프에서는 미사용)

동작
----
스킨드 메시의 현재 포즈를 BakeMesh로 고정합니다. 무기처럼 일반 MeshRenderer인
부위도 당시의 월드 변환으로 고정됩니다. 알베도 텍스처, 타일링/오프셋, 컬러,
기존 발광 및 MaterialPropertyBlock의 색 오버라이드를 캡처합니다.
잔상 자체는 색을 복사한 메시 + 전용 URP 셰이더로 렌더링하며,
입자의 생성/수명/난류/감속/페이드는 VFX Graph에서 GPU로 처리합니다.
최대 12개 포즈를 재사용하고, 수명이 끝난 포즈는 다시 사용합니다.

GPU 입자는 잔상과 같은 베이크 포즈 표면에서 생성됩니다. 월드 좌표의 표면을
샘플링하므로 플레이어가 떠난 후에도 입자가 따라가지 않고 그곳에서 흩날립니다.
작은 청색 조각과 반짝임이 따로 생성되며, 기존의 컬 노이즈와 드래그로 흩날립니다.
각 입자는 작고 짧은 형태를 유지하고, 생애 후반에 작아지며 투명해집니다.

기본 연출값
-----------
포즈 간격 0.065초 / 잔상 수명 0.64초 / 불투명도 0.66 / 노이즈 크기 27
작은 입자 720개/초 / 반짝임 90개/초 / GPU 용량 2048 + 256
작은 입자 수명 0.45~0.95초 / 반짝임 수명 0.24~0.6초 (선택한 연출 시간 기준)
입자 크기 0.024~0.055 / 반짝임 크기 0.07~0.13 / 난류 주파수 1.8
게임 설정 데이터와 프리팹의 입자량을 함께 적용했습니다.
미리보기 속도 0.65배 / 회피 이동 거리 4.2m
몸체와 무기는 현재 프로젝트의 플레이어 모델과 원래 머티리얼을 그대로 참조합니다.
미리보기 모델에서는 기존 게임플레이 스크립트와 ParticleSystem을 제거했습니다.

회피 조정 툴에서 게임에 적용
--------------------------
Tools > Character > Player > Dodge Studio > 잔상 VFX 에서
‘제작한 VFX 연결 · 적용’ 또는 ‘컬러 디더 VFX’를 선택하고 설정 저장을 누릅니다.
다음 회피부터 반영됩니다. ‘기존 + 컬러 디더 VFX’로 함께 재생하거나 모두 끌 수 있습니다.
회피 시작/완벽 회피 성공/슬로우 시작을 고르고 생성 시간, 수명, 투명도, 노이즈,
무기 포함, 입자량 등을 조절합니다. 전투 속도/실제 시간을 선택할 수 있으며 히트스톱에 정지합니다.
‘이 창에서 입자 VFX 재생’으로 회피 조정 창 안에서 잔상과 실제 VFX Graph 입자를 함께 봅니다.
편집 모드에서 재생/일시정지/프레임 이동/시간 슬라이더를 사용할 수 있습니다.
잔광이 다 사라지는 모습까지 볼 수 있도록 회피 조정 미리보기의 종료 여유를 늘렸습니다.
입자량 등의 값은 미리보기에 반영되며, 게임 적용 방식과 미저장 설정은 그대로 유지합니다.

다른 이벤트에 직접 연결하는 경우
--------------------------
별도 VFX 프리팹을 생성하고 ChromaticAfterimageTrail.Bind(플레이어모델.transform),
Play(0.34f)를 호출합니다. StopEmission()은 입자 생성만 멈추고 기존 입자는 남깁니다.
Clear()는 현재 잔상과 입자를 지웁니다. E 스킬 자동 연결은 추가하지 않았습니다.
프리팹은 Source Root가 비어 있는 상태로 저장되어 있습니다.
독립 미리보기 씬에서는 복제된 모델이 할당되어 있으며 Play 시 자동 재생됩니다.

스냅샷 색 보존은 현재 프로젝트의 URP Lit 알베도/컬러/발광 기준입니다.
다른 커스텀 셰이더의 모든 조명/후처리 계산을 픽셀 단위로 복제하는 방식은 아닙니다.
완벽 회피 입자 품질 목표와 참고 자료
-----------------------------------
사용자 피드백에 따라 긴 곡선형 잔광을 폐기하고 이전의 작은 입자 스타일로 복구했습니다.
방출량, 크기, 수명, 난류를 원래 값으로 되돌리고 개별 입자의 마스크와 알파만 다듬었습니다.
원래 마스크 생성식은 Mathf.SmoothStep을 셰이더의 smoothstep처럼 사용하여
사각형 모서리에도 알파가 남았습니다. InverseLerp로 범위를 정규화하여 이 오류를
수정하고, 작은 조각의 부드러운 가장자리와 중심광을 표현했습니다.
반짝임의 긴 십자 끝을 짧게 줄이고 생애별 알파 곡선은 0에서 시작해 0으로 끝납니다.
생성 중단 후에도 기존 입자는 수명을 마칩니다. VFX Graph의 GPU 시스템 2개를 유지합니다.

같은 시드, 카메라, 블룸에서 원래 작은 입자와 수정본을 재생하여 비교했습니다.
비교 캡처: Artifacts/PerfectDodgeParticles/SpeckRevision (프로젝트 루트 기준).
상위 PerfectDodgeParticles의 최초 비교 자료는 폐기된 긴 잔광 실험 기록입니다.

Unity 공식 Turbulence: curl noise를 입자 속도에 적용하는 VFX Graph 블록.
https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.0/manual/Block-Turbulence.html
Unity 공식 Set Attribute from Curve/Gradient: age/lifetime 기반 알파·크기 변화.
https://docs.unity3d.com/Packages/com.unity.visualeffectgraph@17.0/manual/Block-SetAttributeFromCurve.html
