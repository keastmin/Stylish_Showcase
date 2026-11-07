using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ProjectR.UI.MainTitle.Editor
{
    public static class MainTitleBuilder
    {
        public const string Folder = "Assets/08_Prefabs/MainTitle";
        public const string PrefabPath = Folder + "/MainTitleMenu.prefab";
        public const string PreviewScenePath = Folder + "/Scenes/MainTitlePreview.unity";
        static TMP_FontAsset korean, latin, latinBold;
        static MainTitleController controller;
        static readonly Color Ice=Hex("BAF3FF"), Gold=Hex("E9CC99"), White=Hex("EDF5F6"), Muted=Hex("96ABB6"), Ink=Hex("09141E");

        [MenuItem("Tools/Project R/Main Title/Rebuild Prefab")]
        public static void Build()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Exit Play Mode before authoring.");
            Directory.CreateDirectory(Folder + "/Scenes");
            AssetDatabase.Refresh();
            LoadFonts();
            var art=ImportArt();
            var scene=EditorSceneManager.NewPreviewScene();
            try
            {
                var root=new GameObject("MainTitleMenu",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster),typeof(MainTitleController));
                SceneManager.MoveGameObjectToScene(root,scene);
                root.layer=5;
                var canvas=root.GetComponent<Canvas>(); canvas.renderMode=RenderMode.ScreenSpaceOverlay; canvas.sortingOrder=100;
                var scaler=root.GetComponent<CanvasScaler>(); scaler.uiScaleMode=CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution=new Vector2(1920,1080); scaler.screenMatchMode=CanvasScaler.ScreenMatchMode.Expand;
                controller=root.GetComponent<MainTitleController>();
                var background=Rect("Atmosphere",root.transform,0,0,0,0); Stretch(background); background.offsetMin=new Vector2(-50,-50); background.offsetMax=new Vector2(50,50);
                background.gameObject.AddComponent<Canvas>().overrideSorting=false;
                controller.artwork=background;
                var key=Picture("GeneratedKeyArt",background,0,0,0,0,art); Stretch(key.rectTransform);
                var fit=key.gameObject.AddComponent<AspectRatioFitter>(); fit.aspectMode=AspectRatioFitter.AspectMode.EnvelopeParent; fit.aspectRatio=art.rect.width/art.rect.height;
                var veil=Rect("ReadabilityVeil",root.transform,0,0,0,0); Stretch(veil);
                var grad=veil.gameObject.AddComponent<MainTitleGradientGraphic>(); grad.horizontal=true; grad.startOpacity=.93f; grad.endOpacity=0; grad.power=2.6f; grad.color=Ink; grad.raycastTarget=false;
                var lower=Rect("LowerVignette",root.transform,0,0,0,0); Stretch(lower);
                var bottom=lower.gameObject.AddComponent<PauseSettingsGradientGraphic>(); bottom.color=Ink; bottom.bottomOpacity=.72f; bottom.topOpacity=0; bottom.fadePower=4; bottom.raycastTarget=false;
                var safe=Rect("SafeArea",root.transform,0,0,0,0); Stretch(safe); safe.gameObject.AddComponent<PlayerHUDSafeArea>();
                var intro=Rect("Intro",safe,0,0,0,0); Stretch(intro); controller.introGroup=intro.gameObject.AddComponent<CanvasGroup>();
                var main=Rect("MainMenu",intro,0,0,0,0); Stretch(main); controller.menuGroup=main.gameObject.AddComponent<CanvasGroup>();
                BuildMain(main);
                controller.introRows=new[]{controller.newGameButton,controller.stageSelectButton,controller.settingsButton,controller.quitButton}.Select(b=>b.gameObject.AddComponent<CanvasGroup>()).ToArray();
                BuildStages(safe,art);
                BuildSettings(safe);
                BuildQuit(safe);
                controller.feedbackLabel=Label("Feedback",safe,"",144,112,1400,36,20,Ice,true);
                controller.transitionVeil=Image("SceneTransition",root.transform,0,0,0,0,new Color(Ink.r,Ink.g,Ink.b,0)); Stretch(controller.transitionVeil.rectTransform);
                controller.stages=new[] {
                    new TitleStageDefinition {id="prototype",displayName="전투 테스트 구역",bossName="프로토타입 전투",description="기본 전투와 회피, 스킬을 체험하세요.\n지금 준비된 전투 구역으로 이동합니다.",scenePath="Assets/01_Scenes/DemoScene 1.unity",available=true,artwork=art},
                    new TitleStageDefinition {id="hunter-concept",displayName="추격의 전장",bossName="추격의 사냥꾼 · 가칭",description="빠르게 접근하고 거리를 벌리는 추격자.\n측면 회피와 거리 조절을 중심으로 한 스테이지 콘셉트입니다.",scenePath="",available=false,artwork=art},
                    new TitleStageDefinition {id="storm-concept",displayName="폭풍의 경계",bossName="폭풍의 지배자 · 가칭",description="위험 구역 사이에서 공격 기회를 찾는 전투.\n이동 경로와 위치 선정이 중요한 스테이지 콘셉트입니다.",scenePath="",available=false,artwork=art}
                };
                controller.feedbackSource=root.AddComponent<AudioSource>(); controller.feedbackSource.playOnAwake=false; controller.feedbackSource.spatialBlend=0; controller.feedbackSource.ignoreListenerPause=true;
                controller.hoverClip=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/Audio/Hover.wav");
                controller.confirmClip=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/Audio/Confirm.wav");
                controller.backClip=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/Audio/Back.wav");
                LinkVertical(new Selectable[]{controller.newGameButton,controller.stageSelectButton,controller.settingsButton,controller.quitButton});
                LinkVertical(new Selectable[]{controller.masterSlider,controller.muteToggle,controller.screenModeDropdown,controller.reducedMotionToggle,controller.settingsBackButton});
                LinkVertical(new Selectable[]{controller.stageButtons[0],controller.stageButtons[1],controller.stageButtons[2],controller.launchStageButton,controller.stageBackButton});
                LinkVertical(new Selectable[]{controller.quitCancelButton,controller.quitConfirmButton});
                PrefabUtility.SaveAsPrefabAsset(root,PrefabPath);
                EditorUtility.SetDirty(korean); AssetDatabase.SaveAssetIfDirty(korean);
            }
            finally {EditorSceneManager.ClosePreviewScene(scene); controller=null;}
            BuildPreviewScene();
            Debug.Log("[MainTitle] Built editable UGUI/TMP prefab: "+PrefabPath);
        }

        static void BuildMain(RectTransform p)
        {
            Label("Eyebrow",p,"S T Y L I S H   A C T I O N",148,901,630,30,16,Ice,false,false);
            Image("BrandRule",p,148,879,56,2,Gold);
            Label("ProjectWordmark",p,"PROJECT",140,776,670,100,82,White,false,true,8);
            Label("RWordmark",p,"R",136,625,210,216,154,White,false,true);
            Label("TitleEdition",p,"B O S S   S E Q U E N C E",277,695,410,30,18,Gold,false,false);
            Image("TitleSeparator",p,278,682,260,1,new Color(Gold.r,Gold.g,Gold.b,.35f));
            Label("Tagline",p,"다음 전투의 시작.",148,604,650,44,24,Muted,true);
            controller.newGameButton=MenuButton("NewGame",p,"새 시작","BEGIN YOUR RUN","01",144,473,536,84,true);
            controller.stageSelectButton=MenuButton("StageSelect",p,"스테이지 선택","CHOOSE YOUR BATTLE","02",144,379,536,84);
            controller.settingsButton=MenuButton("Settings",p,"설정","MAKE IT YOURS","03",144,285,536,84);
            controller.quitButton=MenuButton("Quit",p,"종료","LEAVE THE FIELD","04",144,191,536,84);
            Image("FooterLine",p,144,88,1632,1,new Color(Ice.r,Ice.g,Ice.b,.18f));
            Label("Controls",p,"↑ ↓  이동     ENTER / A  선택     ESC / B  뒤로",146,42,1100,34,17,Muted,true);
            var build=Label("BuildLabel",p,"PROJECT R  /  PROTOTYPE",1336,42,440,34,16,Muted,false,false,1.6f); build.alignment=TextAlignmentOptions.MidlineRight;
            var stamp=Rect("ChapterStamp",p,0,0,490,110); stamp.anchorMin=stamp.anchorMax=new Vector2(1,0); stamp.pivot=new Vector2(1,0); stamp.anchoredPosition=new Vector2(-144,166);
            Image("StampAccent",stamp,0,82,30,2,Gold);
            Label("Chapter",stamp,"01  /  INTO THE RUINS",0,40,490,35,23,White,false,true,2);
            Label("ChapterCaption",stamp,"보스를 향해, 한 걸음 더.",0,3,490,30,20,Muted,true);
        }

        static void BuildStages(RectTransform p,Sprite art)
        {
            var w=Panel("StageSelection",p,1320,774,out var group); controller.stagesPanel=group;
            Label("Eyebrow",w,"SELECT A STAGE",52,700,900,28,17,Ice,false,true,3);
            Label("Title",w,"스테이지 선택",50,630,1000,64,43,White,true);
            Label("Subtitle",w,"당신의 다음 전투를 선택하세요.",53,588,1000,34,22,Muted,true);
            Image("Rule",w,52,566,1216,1,Hex("2C424C"));
            controller.stageButtons=new Button[3];
            controller.stageButtons[0]=MenuButton("Stage_01",w,"전투 테스트 구역","PLAYABLE PROTOTYPE","01",52,431,476,108,true);
            controller.stageButtons[1]=MenuButton("Stage_02",w,"추격의 전장","CONCEPT / 준비 중","02",52,309,476,108);
            controller.stageButtons[2]=MenuButton("Stage_03",w,"폭풍의 경계","CONCEPT / 준비 중","03",52,187,476,108);
            var artFrame=Rect("StageArtClip",w,578,290,690,250); artFrame.gameObject.AddComponent<RectMask2D>();
            var pic=Picture("StageArt",artFrame,0,0,690,389,art); pic.rectTransform.anchoredPosition=new Vector2(0,-55); controller.stageArtwork=pic;
            Label("ArtMark",artFrame,"FIELD ARCHIVE / 01",23,206,620,27,17,Gold,false,true,2);
            controller.stageTitle=Label("StageTitle",w,"전투 테스트 구역",578,232,690,46,32,White,true);
            controller.stageBoss=Label("StageBoss",w,"프로토타입 전투",579,198,690,32,20,Ice,true);
            controller.stageDescription=Label("StageDescription",w,"기본 전투와 회피, 스킬을 체험하세요.\n지금 준비된 전투 구역으로 이동합니다.",579,102,690,86,20,Muted,true); controller.stageDescription.textWrappingMode=TextWrappingModes.Normal;
            controller.stageStatus=Label("StageStatus",w,"플레이 가능",53,128,480,35,18,Gold,true);
            controller.stageBackButton=MenuButton("Back",w,"뒤로","","",52,40,210,62);
            controller.launchStageButton=MenuButton("Launch",w,"전투 시작","","",958,38,310,66,true);
        }

        static void BuildSettings(RectTransform p)
        {
            var w=Panel("SettingsPanel",p,1140,790,out var group); controller.settingsPanel=group;
            Label("Eyebrow",w,"TUNE YOUR EXPERIENCE",52,715,1000,28,17,Ice,false,true,3);
            Label("Title",w,"설정",50,644,1000,64,44,White,true);
            Label("Subtitle",w,"당신의 리듬에 맞춰 준비하세요.",53,603,1000,34,22,Muted,true);
            Image("Rule",w,52,580,1036,1,Hex("2C424C"));
            Label("AudioLabel",w,"전체 음량",54,514,750,38,27,White,true);
            controller.volumeLabel=Label("VolumeValue",w,"100%",882,510,206,46,34,Ice,false,true); controller.volumeLabel.alignment=TextAlignmentOptions.MidlineRight;
            controller.masterSlider=MakeSlider(w,54,454,1032,42);
            controller.muteToggle=MakeToggle("Mute",w,"모든 소리 끄기",54,376,1032,62);
            Image("Rule2",w,54,355,1032,1,Hex("21333E"));
            Label("ScreenModeLabel",w,"화면 모드",54,282,470,48,25,White,true);
            controller.screenModeDropdown=MakeDropdown(w,614,279,472,54);
            controller.reducedMotionToggle=MakeToggle("ReducedMotion",w,"모션 줄이기",54,181,1032,62);
            Label("MotionDescription",w,"배경 움직임과 확대 효과를 줄입니다.",55,144,920,32,18,Muted,true);
            controller.settingsBackButton=MenuButton("Back",w,"완료","","",54,48,300,66,true);
            var auto=Label("AutoSave",w,"설정은 자동으로 저장됩니다.",402,60,684,35,18,Muted,true); auto.alignment=TextAlignmentOptions.MidlineRight;
        }

        static void BuildQuit(RectTransform p)
        {
            var w=Panel("QuitConfirmation",p,850,444,out var group); controller.quitPanel=group;
            Label("Eyebrow",w,"LEAVE THE FIELD",50,365,750,28,17,Gold,false,true,3);
            Label("Title",w,"게임을 종료할까요?",48,266,754,70,39,White,true);
            Label("Body",w,"다음 전투에서 다시 만나요.",52,202,746,45,23,Muted,true);
            controller.quitCancelButton=MenuButton("Cancel",w,"돌아가기","","",52,62,348,72,true);
            controller.quitConfirmButton=MenuButton("Confirm",w,"종료","","",444,62,354,72);
        }

        static RectTransform Panel(string name,Transform parent,float width,float height,out CanvasGroup group)
        {
            var layer=Rect(name,parent,0,0,0,0); Stretch(layer); group=layer.gameObject.AddComponent<CanvasGroup>(); group.alpha=0; group.interactable=false; group.blocksRaycasts=false;
            var shade=Image("ModalBlocker",layer,0,0,0,0,new Color(.01f,.025f,.04f,.82f)); Stretch(shade.rectTransform); shade.raycastTarget=true;
            var w=Rect("Window",layer,0,0,width,height); w.anchorMin=w.anchorMax=w.pivot=new Vector2(.5f,.5f);
            Facet("Frame",w,0,0,width,height,Hex("0B1822"),Hex("3C5964"),16);
            Image("TopAccent",w,52,height-2,170,2,Gold);
            return w;
        }
        static Button MenuButton(string name,Transform p,string caption,string subtitle,string number,float x,float y,float w,float h,bool primary=false)
        {
            var r=Rect(name,p,x,y,w,h); var hit=r.gameObject.AddComponent<Image>(); hit.color=Color.clear; hit.raycastTarget=true;
            var b=r.gameObject.AddComponent<Button>(); b.targetGraphic=hit; b.transition=Selectable.Transition.None;
            var v=Rect("Visual",r,0,0,w,h);
            var bg=Image("Highlight",v,0,0,w,h,new Color(.45f,.75f,.84f,primary?.12f:.025f));
            Image("Baseline",v,0,0,w,1,new Color(Ice.r,Ice.g,Ice.b,.13f));
            var accent=Image("Accent",v,0,0,3,h,new Color(Gold.r,Gold.g,Gold.b,primary?1:0));
            bool numbered=!string.IsNullOrEmpty(number); float left=numbered?68:25;
            if(numbered) Label("Number",v,number,22,h/2-13,35,26,16,primary?Gold:Muted,false,true);
            var label=Label("Label",v,caption,left,string.IsNullOrEmpty(subtitle)?h/2-22:h/2-9,w-left-45,44,numbered?29:23,primary?White:Hex("C6D5DD"),true);
            if(!string.IsNullOrEmpty(subtitle)) Label("SubLabel",v,subtitle,left,10,w-left-40,23,13,Muted,subtitle.Any(c=>c>127),false,1.6f);
            Label("Arrow",v,"/",w-39,h/2-18,25,36,27,Gold,false,false);
            var feedback=r.gameObject.AddComponent<MainTitleFeedback>(); feedback.owner=controller; feedback.visual=v; feedback.label=label; feedback.highlight=bg; feedback.accent=accent;
            return b;
        }
        static Slider MakeSlider(Transform p,float x,float y,float w,float h)
        {
            var r=Rect("MasterVolume",p,x,y,w,h); var hit=r.gameObject.AddComponent<Image>(); hit.color=Color.clear;
            var slider=r.gameObject.AddComponent<Slider>(); slider.minValue=0; slider.maxValue=1; slider.value=1;
            Image("Track",r,0,h/2-2,w,4,Hex("304752"));
            var fillArea=Rect("FillArea",r,10,h/2-2,w-20,4); var fill=Image("Fill",fillArea,0,0,0,0,Ice); Stretch(fill.rectTransform); slider.fillRect=fill.rectTransform;
            var handles=Rect("HandleArea",r,10,7,w-20,h-14); var handle=Image("Handle",handles,0,0,16,0,Gold); handle.rectTransform.anchorMin=Vector2.zero; handle.rectTransform.anchorMax=Vector2.one; handle.rectTransform.pivot=new Vector2(.5f,.5f); slider.handleRect=handle.rectTransform; slider.targetGraphic=handle;
            slider.direction=Slider.Direction.LeftToRight; return slider;
        }
        static Toggle MakeToggle(string name,Transform p,string caption,float x,float y,float w,float h)
        {
            var r=Rect(name,p,x,y,w,h); var hit=r.gameObject.AddComponent<Image>(); hit.color=Color.clear;
            var toggle=r.gameObject.AddComponent<Toggle>(); Label("Label",r,caption,0,8,w-100,h-16,25,White,true);
            var box=Image("Box",r,w-50,12,38,38,Hex("2A414D")); var check=Image("Check",box.transform,9,9,20,20,Ice);
            toggle.targetGraphic=box; toggle.graphic=check; toggle.isOn=false; return toggle;
        }
        static TMP_Dropdown MakeDropdown(Transform p,float x,float y,float w,float h)
        {
            var r=Rect("ScreenMode",p,x,y,w,h); var bg=r.gameObject.AddComponent<Image>(); bg.color=Hex("233B48");
            var drop=r.gameObject.AddComponent<TMP_Dropdown>(); drop.targetGraphic=bg;
            var caption=Label("Label",r,"창 모드",22,4,w-70,h-8,21,White,true); drop.captionText=caption;
            Label("Arrow",r,"v",w-36,10,25,h-20,23,Ice,false);
            var template=Rect("Template",r,0,-168,w,162); var templateBg=template.gameObject.AddComponent<Image>(); templateBg.color=Hex("172D39");
            var viewport=Rect("Viewport",template,4,4,w-8,154); viewport.gameObject.AddComponent<RectMask2D>();
            var content=Rect("Content",viewport,0,0,w-8,154); content.anchorMin=new Vector2(0,1); content.anchorMax=new Vector2(1,1); content.pivot=new Vector2(.5f,1); content.sizeDelta=new Vector2(0,154); content.anchoredPosition=Vector2.zero;
            var item=Rect("Item",content,0,0,w-8,50); item.anchorMin=new Vector2(0,1); item.anchorMax=new Vector2(1,1); item.pivot=new Vector2(.5f,1); item.sizeDelta=new Vector2(0,50); item.anchoredPosition=Vector2.zero;
            var itemBg=item.gameObject.AddComponent<Image>(); itemBg.color=Hex("294956"); var toggle=item.gameObject.AddComponent<Toggle>(); toggle.targetGraphic=itemBg;
            var tick=Image("Check",item,8,20,5,10,Gold); toggle.graphic=tick;
            var itemText=Label("ItemLabel",item,"창 모드",25,3,w-40,44,20,White,true); drop.itemText=itemText;
            var scroll=template.gameObject.AddComponent<ScrollRect>(); scroll.viewport=viewport; scroll.content=content; scroll.horizontal=false; scroll.movementType=ScrollRect.MovementType.Clamped;
            drop.template=template; drop.options=new System.Collections.Generic.List<TMP_Dropdown.OptionData>{new("전체 화면"),new("테두리 없는 창"),new("창 모드")}; template.gameObject.SetActive(false);
            return drop;
        }
        static void LinkVertical(Selectable[] items)
        {
            for(int i=0;i<items.Length;i++){var n=new Navigation{mode=Navigation.Mode.Explicit,selectOnUp=items[(i+items.Length-1)%items.Length],selectOnDown=items[(i+1)%items.Length]}; items[i].navigation=n;}
        }
        static RectTransform Rect(string name,Transform parent,float x,float y,float w,float h)
        {
            var r=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>(); r.gameObject.layer=5; r.SetParent(parent,false); r.anchorMin=r.anchorMax=r.pivot=Vector2.zero; r.anchoredPosition=new Vector2(x,y); r.sizeDelta=new Vector2(w,h); return r;
        }
        static void Stretch(RectTransform r){r.anchorMin=Vector2.zero;r.anchorMax=Vector2.one;r.pivot=new Vector2(.5f,.5f);r.offsetMin=r.offsetMax=Vector2.zero;}
        static Image Image(string n,Transform p,float x,float y,float w,float h,Color c){var r=Rect(n,p,x,y,w,h);var i=r.gameObject.AddComponent<Image>();i.color=c;i.raycastTarget=false;return i;}
        static Image Picture(string n,Transform p,float x,float y,float w,float h,Sprite s){var i=Image(n,p,x,y,w,h,Color.white);i.sprite=s;return i;}
        static void Facet(string n,Transform p,float x,float y,float w,float h,Color fill,Color border,float cut){var r=Rect(n,p,x,y,w,h);var f=r.gameObject.AddComponent<HUDFacetGraphic>();f.color=fill;f.borderColor=border;f.cornerCut=cut;f.raycastTarget=false;}
        static TMP_Text Label(string n,Transform p,string s,float x,float y,float w,float h,float size,Color c,bool ko=false,bool bold=false,float spacing=0)
        {
            float actualHeight=Mathf.Max(h,size*(ko?1.55f:1.25f)); var r=Rect(n,p,x,y-(actualHeight-h)*.5f,w,actualHeight);var t=r.gameObject.AddComponent<TextMeshProUGUI>();t.font=ko?korean:(bold?latinBold:latin);t.text=s;t.fontSize=size;t.color=c;t.raycastTarget=false;t.textWrappingMode=TextWrappingModes.NoWrap;t.overflowMode=TextOverflowModes.Ellipsis;t.alignment=TextAlignmentOptions.MidlineLeft;t.characterSpacing=spacing;if(ko&&bold)t.fontStyle=FontStyles.Bold;return t;
        }
        static Color Hex(string s){ColorUtility.TryParseHtmlString("#"+s,out var c);return c;}
        static Sprite ImportArt()
        {
            var path=Folder+"/Art/TitleSanctuary.png";var importer=(TextureImporter)AssetImporter.GetAtPath(path);if(importer==null)throw new FileNotFoundException(path);
            importer.textureType=TextureImporterType.Sprite;importer.spriteImportMode=SpriteImportMode.Single;importer.mipmapEnabled=false;importer.alphaIsTransparency=false;importer.maxTextureSize=2048;importer.textureCompression=TextureImporterCompression.Uncompressed;importer.SaveAndReimport();return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
        static void LoadFonts()
        {
            const string source="Assets/Download Assets/Heat - Complete Modern UI/Fonts/";
            latin=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(source+"RobotoCondensed-Regular SDF.asset");latinBold=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(source+"RobotoCondensed-Bold SDF.asset");
            var path=Folder+"/Fonts/NotoSansKR SDF.asset";korean=AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
            if(korean==null){var f=AssetDatabase.LoadAssetAtPath<Font>(Folder+"/Fonts/NotoSansCJKkr-Regular.otf");korean=TMP_FontAsset.CreateFontAsset(f,48,5,GlyphRenderMode.SDFAA,2048,2048,AtlasPopulationMode.Dynamic,true);korean.name="NotoSansKR SDF";AssetDatabase.CreateAsset(korean,path);AssetDatabase.AddObjectToAsset(korean.material,korean);foreach(var a in korean.atlasTextures)AssetDatabase.AddObjectToAsset(a,korean);}
            string warm=string.Concat(Enumerable.Range(32,95).Select(c=>(char)c))+"↑↓·…";
            foreach(var file in Directory.GetFiles(Folder,"*.cs",SearchOption.AllDirectories))warm+=File.ReadAllText(file);
            korean.TryAddCharacters(new string(warm.Distinct().ToArray()),out string missing);EditorUtility.SetDirty(korean);
        }
        static void BuildPreviewScene()
        {
            var previous=SceneManager.GetActiveScene();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try{var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);PrefabUtility.InstantiatePrefab(prefab,scene);var camera=new GameObject("Main Camera",typeof(Camera),typeof(AudioListener));SceneManager.MoveGameObjectToScene(camera,scene);camera.tag="MainCamera";camera.GetComponent<Camera>().backgroundColor=Ink;camera.GetComponent<Camera>().clearFlags=CameraClearFlags.SolidColor;var light=new GameObject("Directional Light",typeof(Light));SceneManager.MoveGameObjectToScene(light,scene);light.GetComponent<Light>().type=LightType.Directional;EditorSceneManager.SaveScene(scene,PreviewScenePath);}
            finally{EditorSceneManager.CloseScene(scene,true);if(previous.IsValid())SceneManager.SetActiveScene(previous);}
        }
    }
}




