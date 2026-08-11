using System;
using System.Collections.Generic;
using UnityEngine;

namespace Wayfu.Lamkn
{
    // Database ánh xạ AudioType -> danh sách clip. Mỗi type có thể có nhiều clip để random cho đỡ lặp.
    [CreateAssetMenu(menuName = "Wayfu/Audio Database", fileName = "AudioData")]
    public class AudioData : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            public AudioType type;

            public List<AudioClip> clips;
        }

        public List<Entry> entries;
    }
}
