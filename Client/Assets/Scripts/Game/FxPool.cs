using System.Collections.Generic;
using UnityEngine;

namespace Multiplayer.Game
{
    // Instances of one prefab, kept inactive while free.
    public sealed class FxPool
    {
        readonly GameObject prefab;
        readonly Transform parent;
        readonly Stack<GameObject> free = new Stack<GameObject>();
        readonly float lifetime;

        public FxPool(GameObject prefab, Transform parent, int prewarm)
        {
            this.prefab = prefab;
            this.parent = parent;
            lifetime = LongestLifetime(prefab);
            for (int i = 0; i < prewarm; i++)
                free.Push(Create());
        }

        // Seconds until every particle of a burst at time zero has died.
        public float Lifetime
        {
            get { return lifetime; }
        }

        public GameObject Take()
        {
            GameObject instance = free.Count > 0 ? free.Pop() : Create();
            instance.SetActive(true);
            return instance;
        }

        public void Return(GameObject instance)
        {
            if (instance == null)
                return;

            instance.SetActive(false);
            free.Push(instance);
        }

        GameObject Create()
        {
            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;
            instance.SetActive(false);
            return instance;
        }

        static float LongestLifetime(GameObject prefab)
        {
            float longest = 0f;
            ParticleSystem[] systems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem.MainModule main = systems[i].main;
                longest = Mathf.Max(longest, main.startDelay.constantMax + main.startLifetime.constantMax);
            }
            return longest;
        }
    }
}
