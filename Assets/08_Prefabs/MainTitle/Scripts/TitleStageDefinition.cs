using System;
using UnityEngine;

namespace ProjectR.UI.MainTitle
{
    [Serializable]
    public sealed class TitleStageDefinition
    {
        public string id;
        public string displayName;
        public string bossName;
        [TextArea(2, 5)] public string description;
        [Tooltip("Full scene path included in the active Build Profile, or a unique scene name.")]
        public string scenePath;
        public Sprite artwork;
        public bool available = true;
    }
}
